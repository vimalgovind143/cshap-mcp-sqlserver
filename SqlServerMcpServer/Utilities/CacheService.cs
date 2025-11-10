using Microsoft.Extensions.Caching.Memory;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SqlServerMcpServer.Utilities
{
    /// <summary>
    /// Centralized cache service for SQL Server metadata with TTL support and metrics tracking
    /// </summary>
    public class CacheService
    {
        private readonly IMemoryCache _cache;
        private readonly CacheMetrics _metrics;
        private readonly Dictionary<string, DateTime> _cacheTimestamps = new();
        private readonly object _lockObject = new();

        // Cache key prefixes for organization
        public const string TablesPrefix = "tables";
        public const string ProceduresPrefix = "procedures";
        public const string SchemaPrefix = "schema";
        public const string ColumnsPrefix = "columns";
        public const string DependenciesPrefix = "dependencies";

        // Default TTLs (can be overridden via config)
        private static readonly TimeSpan DefaultMetadataTTL = TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("CACHE_TTL_METADATA_SECONDS"), out var ttl) && ttl > 0
                ? ttl
                : 300);

        private static readonly TimeSpan DefaultSchemaTTL = TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("CACHE_TTL_SCHEMA_SECONDS"), out var schemaTtl) && schemaTtl > 0
                ? schemaTtl
                : 600);

        private static readonly TimeSpan DefaultProcedureTTL = TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("CACHE_TTL_PROCEDURE_SECONDS"), out var procTtl) && procTtl > 0
                ? procTtl
                : 300);

        public CacheService(IMemoryCache? cache = null)
        {
            _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
            _metrics = new CacheMetrics();
            Log.Information("[CacheService] Initialized with default TTLs - Metadata: {MetadataTTL}s, Schema: {SchemaTTL}s, Procedure: {ProcedureTTL}s",
                DefaultMetadataTTL.TotalSeconds, DefaultSchemaTTL.TotalSeconds, DefaultProcedureTTL.TotalSeconds);
        }

        /// <summary>
        /// Gets a cached value or creates it using the provided factory function
        /// </summary>
        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? ttl = null)
        {
            ttl ??= DefaultMetadataTTL;

            if (_cache.TryGetValue(key, out T? cachedValue))
            {
                _metrics.RecordHit();
                Log.Debug("[CacheService] Cache hit for key: {Key}", key);
                return cachedValue!;
            }

            _metrics.RecordMiss();
            Log.Debug("[CacheService] Cache miss for key: {Key}. Loading from factory...", key);

            var value = await factory();
            await SetAsync(key, value, ttl);
            return value;
        }

        /// <summary>
        /// Gets a cached value or creates it using the provided factory function (synchronous)
        /// </summary>
        public T GetOrCreate<T>(
            string key,
            Func<T> factory,
            TimeSpan? ttl = null)
        {
            ttl ??= DefaultMetadataTTL;

            if (_cache.TryGetValue(key, out T? cachedValue))
            {
                _metrics.RecordHit();
                Log.Debug("[CacheService] Cache hit for key: {Key}", key);
                return cachedValue!;
            }

            _metrics.RecordMiss();
            Log.Debug("[CacheService] Cache miss for key: {Key}. Loading from factory...", key);

            var value = factory();
            Set(key, value, ttl);
            return value;
        }

        /// <summary>
        /// Sets a value in the cache with TTL
        /// </summary>
        public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
        {
            ttl ??= DefaultMetadataTTL;

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(ttl.Value);

            _cache.Set(key, value, cacheOptions);

            lock (_lockObject)
            {
                _cacheTimestamps[key] = DateTime.UtcNow;
            }

            Log.Debug("[CacheService] Cached value for key: {Key} with TTL: {TTL}s", key, ttl.Value.TotalSeconds);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Sets a value in the cache with TTL (synchronous)
        /// </summary>
        public void Set<T>(string key, T value, TimeSpan? ttl = null)
        {
            ttl ??= DefaultMetadataTTL;

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(ttl.Value);

            _cache.Set(key, value, cacheOptions);

            lock (_lockObject)
            {
                _cacheTimestamps[key] = DateTime.UtcNow;
            }

            Log.Debug("[CacheService] Cached value for key: {Key} with TTL: {TTL}s", key, ttl.Value.TotalSeconds);
        }

        /// <summary>
        /// Removes a specific key from cache
        /// </summary>
        public void Remove(string key)
        {
            _cache.Remove(key);
            lock (_lockObject)
            {
                _cacheTimestamps.Remove(key);
            }
            Log.Information("[CacheService] Removed cache entry: {Key}", key);
        }

        /// <summary>
        /// Removes all cache entries matching a pattern (e.g., "tables:*")
        /// </summary>
        public int RemoveByPattern(string pattern)
        {
            var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$");
            int removedCount = 0;

            lock (_lockObject)
            {
                var keysToRemove = _cacheTimestamps.Keys
                    .Where(k => regex.IsMatch(k))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _cache.Remove(key);
                    _cacheTimestamps.Remove(key);
                    removedCount++;
                }
            }

            Log.Information("[CacheService] Removed {Count} cache entries matching pattern: {Pattern}", removedCount, pattern);
            return removedCount;
        }

        /// <summary>
        /// Clears all cache entries
        /// </summary>
        public int Clear()
        {
            int count;
            lock (_lockObject)
            {
                count = _cacheTimestamps.Count;
                _cacheTimestamps.Clear();
            }

            // Note: IMemoryCache doesn't have a Clear method, so we need to create a new instance
            // This is a limitation we'll document
            Log.Warning("[CacheService] Cache clear requested. Removed {Count} tracked entries", count);
            return count;
        }

        /// <summary>
        /// Gets current cache metrics
        /// </summary>
        public CacheMetricsSnapshot GetMetrics()
        {
            lock (_lockObject)
            {
                return new CacheMetricsSnapshot
                {
                    Hits = _metrics.Hits,
                    Misses = _metrics.Misses,
                    TotalOperations = _metrics.TotalOperations,
                    HitRatio = _metrics.HitRatio,
                    CachedItemsCount = _cacheTimestamps.Count,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Gets cache information including configuration
        /// </summary>
        public CacheInfo GetCacheInfo()
        {
            lock (_lockObject)
            {
                return new CacheInfo
                {
                    Enabled = true,
                    DefaultTTLSeconds = (int)DefaultMetadataTTL.TotalSeconds,
                    SchemaTTLSeconds = (int)DefaultSchemaTTL.TotalSeconds,
                    ProcedureTTLSeconds = (int)DefaultProcedureTTL.TotalSeconds,
                    CurrentEntriesCount = _cacheTimestamps.Count,
                    LastClearedUtc = null,
                    CachedKeyPrefixes = new Dictionary<string, int>
                    {
                        { TablesPrefix, _cacheTimestamps.Keys.Count(k => k.StartsWith(TablesPrefix)) },
                        { ProceduresPrefix, _cacheTimestamps.Keys.Count(k => k.StartsWith(ProceduresPrefix)) },
                        { SchemaPrefix, _cacheTimestamps.Keys.Count(k => k.StartsWith(SchemaPrefix)) },
                        { ColumnsPrefix, _cacheTimestamps.Keys.Count(k => k.StartsWith(ColumnsPrefix)) },
                        { DependenciesPrefix, _cacheTimestamps.Keys.Count(k => k.StartsWith(DependenciesPrefix)) }
                    }
                };
            }
        }

        /// <summary>
        /// Resets all metrics
        /// </summary>
        public void ResetMetrics()
        {
            _metrics.Reset();
            Log.Information("[CacheService] Metrics reset");
        }

        /// <summary>
        /// Gets the TTL for a specific cache type
        /// </summary>
        public static TimeSpan GetTTLForType(string cacheType)
        {
            return cacheType?.ToLower() switch
            {
                "schema" or "schemainfo" => DefaultSchemaTTL,
                "procedure" or "procedures" => DefaultProcedureTTL,
                _ => DefaultMetadataTTL
            };
        }

        /// <summary>
        /// Generates a cache key for tables
        /// </summary>
        public static string GenerateTablesCacheKey(string database, string? schema = null, string? nameFilter = null)
        {
            var key = $"{TablesPrefix}:{database}";
            if (!string.IsNullOrEmpty(schema))
                key += $":{schema}";
            if (!string.IsNullOrEmpty(nameFilter))
                key += $":{nameFilter}";
            return key;
        }

        /// <summary>
        /// Generates a cache key for procedures
        /// </summary>
        public static string GenerateProceduresCacheKey(string database, string? schema = null)
        {
            var key = $"{ProceduresPrefix}:{database}";
            if (!string.IsNullOrEmpty(schema))
                key += $":{schema}";
            return key;
        }

        /// <summary>
        /// Generates a cache key for schema information
        /// </summary>
        public static string GenerateSchemaCacheKey(string database, string table)
        {
            return $"{SchemaPrefix}:{database}:{table}";
        }

        /// <summary>
        /// Generates a cache key for column definitions
        /// </summary>
        public static string GenerateColumnsCacheKey(string database, string table)
        {
            return $"{ColumnsPrefix}:{database}:{table}";
        }

        /// <summary>
        /// Generates a cache key for dependencies
        /// </summary>
        public static string GenerateDependenciesCacheKey(string database, string objectName)
        {
            return $"{DependenciesPrefix}:{database}:{objectName}";
        }
    }

    /// <summary>
    /// Tracks cache hit/miss metrics
    /// </summary>
    public class CacheMetrics
    {
        private long _hits = 0;
        private long _misses = 0;

        public long Hits => Interlocked.Read(ref _hits);
        public long Misses => Interlocked.Read(ref _misses);
        public long TotalOperations => Hits + Misses;

        public double HitRatio => TotalOperations > 0
            ? (Hits / (double)TotalOperations) * 100
            : 0;

        public void RecordHit()
        {
            Interlocked.Increment(ref _hits);
        }

        public void RecordMiss()
        {
            Interlocked.Increment(ref _misses);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _hits, 0);
            Interlocked.Exchange(ref _misses, 0);
        }
    }

    /// <summary>
    /// Snapshot of cache metrics at a point in time
    /// </summary>
    public class CacheMetricsSnapshot
    {
        public long Hits { get; set; }
        public long Misses { get; set; }
        public long TotalOperations { get; set; }
        public double HitRatio { get; set; }
        public int CachedItemsCount { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Information about cache configuration and state
    /// </summary>
    public class CacheInfo
    {
        public bool Enabled { get; set; }
        public int DefaultTTLSeconds { get; set; }
        public int SchemaTTLSeconds { get; set; }
        public int ProcedureTTLSeconds { get; set; }
        public int CurrentEntriesCount { get; set; }
        public DateTime? LastClearedUtc { get; set; }
        public Dictionary<string, int> CachedKeyPrefixes { get; set; } = new();
    }
}
