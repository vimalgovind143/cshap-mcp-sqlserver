using Xunit;
using FluentAssertions;
using SqlServerMcpServer.Utilities;
using System;
using System.Threading.Tasks;

namespace SqlServerMcpServer.Tests
{
    public class CacheServiceTests
    {
        [Fact]
        public async Task GetOrCreateAsync_WithNewKey_CallsFactory()
        {
            // Arrange
            var cacheService = new CacheService();
            var key = "test_key_1";
            var value = "test_value";
            var factoryCalled = false;

            // Act
            var result = await cacheService.GetOrCreateAsync(key, async () =>
            {
                factoryCalled = true;
                return await Task.FromResult(value);
            });

            // Assert
            result.Should().Be(value);
            factoryCalled.Should().BeTrue();
        }

        [Fact]
        public async Task GetOrCreateAsync_WithCachedKey_DoesNotCallFactory()
        {
            // Arrange
            var cacheService = new CacheService();
            var key = "test_key_2";
            var value = "test_value";
            var factoryCallCount = 0;

            // Act - First call
            var result1 = await cacheService.GetOrCreateAsync(key, async () =>
            {
                factoryCallCount++;
                return await Task.FromResult(value);
            });

            // Act - Second call
            var result2 = await cacheService.GetOrCreateAsync(key, async () =>
            {
                factoryCallCount++;
                return await Task.FromResult(value + "_different");
            });

            // Assert
            result1.Should().Be(value);
            result2.Should().Be(value);
            factoryCallCount.Should().Be(1);
        }

        [Fact]
        public void GetOrCreate_WithNewKey_CallsFactory()
        {
            // Arrange
            var cacheService = new CacheService();
            var key = "sync_test_key_1";
            var value = "sync_test_value";
            var factoryCalled = false;

            // Act
            var result = cacheService.GetOrCreate(key, () =>
            {
                factoryCalled = true;
                return value;
            });

            // Assert
            result.Should().Be(value);
            factoryCalled.Should().BeTrue();
        }

        [Fact]
        public void GetOrCreate_WithCachedKey_DoesNotCallFactory()
        {
            // Arrange
            var cacheService = new CacheService();
            var key = "sync_test_key_2";
            var value = "sync_test_value";
            var factoryCallCount = 0;

            // Act - First call
            var result1 = cacheService.GetOrCreate<string>(key, () =>
            {
                factoryCallCount++;
                return value;
            });

            // Act - Second call
            var result2 = cacheService.GetOrCreate<string>(key, () =>
            {
                factoryCallCount++;
                return value + "_different";
            });

            // Assert
            result1.Should().Be(value);
            result2.Should().Be(value);
            factoryCallCount.Should().Be(1);
        }

        [Fact]
        public void Set_ShouldCacheValue()
        {
            // Arrange
            var cacheService = new CacheService();
            var key = "set_test_key";
            var value = "set_test_value";

            // Act
            cacheService.Set(key, value);
            var result = cacheService.GetOrCreate<string>(key, () => throw new Exception("Should not be called"));

            // Assert
            result.Should().Be(value);
        }

        [Fact]
        public async Task SetAsync_ShouldCacheValue()
        {
            // Arrange
            var cacheService = new CacheService();
            var key = "set_async_test_key";
            var value = "set_async_test_value";

            // Act
            await cacheService.SetAsync(key, value);
            var result = await cacheService.GetOrCreateAsync(key, async () => await Task.FromResult("should_not_be_called"));

            // Assert
            result.Should().Be(value);
        }

        [Fact]
        public void Remove_ShouldRemoveCachedValue()
        {
            // Arrange
            var cacheService = new CacheService();
            var key = "remove_test_key";
            var value = "remove_test_value";
            cacheService.Set(key, value);

            // Act
            cacheService.Remove(key);
            var factoryCalled = false;
            var result = cacheService.GetOrCreate<string>(key, () =>
            {
                factoryCalled = true;
                return "new_value";
            });

            // Assert
            result.Should().Be("new_value");
            factoryCalled.Should().BeTrue();
        }

        [Fact]
        public void RemoveByPattern_ShouldRemoveMatchingKeys()
        {
            // Arrange
            var cacheService = new CacheService();
            cacheService.Set("tables:db1", "value1");
            cacheService.Set("tables:db2", "value2");
            cacheService.Set("procedures:db1", "value3");

            // Act
            var removedCount = cacheService.RemoveByPattern("tables:*");

            // Assert
            removedCount.Should().Be(2);
        }

        [Fact]
        public void Clear_ShouldReturnRemovedCount()
        {
            // Arrange
            var cacheService = new CacheService();
            cacheService.Set("key1", "value1");
            cacheService.Set("key2", "value2");
            cacheService.Set("key3", "value3");

            // Act
            var removedCount = cacheService.Clear();

            // Assert
            removedCount.Should().Be(3);
        }

        [Fact]
        public void GetMetrics_ShouldTrackHits()
        {
            // Arrange
            var cacheService = new CacheService();
            var key = "metrics_test_key";
            cacheService.Set(key, "value");

            // Act
            cacheService.GetOrCreate<string>(key, () => throw new Exception("Should not be called"));
            cacheService.GetOrCreate<string>(key, () => throw new Exception("Should not be called"));

            var metrics = cacheService.GetMetrics();

            // Assert
            metrics.Hits.Should().BeGreaterThan(0);
        }

        [Fact]
        public void GetMetrics_ShouldTrackMisses()
        {
            // Arrange
            var cacheService = new CacheService();

            // Act
            cacheService.GetOrCreate<string>("key1", () => "value1");
            cacheService.GetOrCreate<string>("key2", () => "value2");

            var metrics = cacheService.GetMetrics();

            // Assert
            metrics.Misses.Should().BeGreaterThan(0);
        }

        [Fact]
        public void GetMetrics_ShouldCalculateHitRatio()
        {
            // Arrange
            var cacheService = new CacheService();
            cacheService.Set("key1", "value1");

            // Act
            cacheService.GetOrCreate<string>("key1", () => throw new Exception("Should not be called"));
            cacheService.GetOrCreate<string>("key2", () => "value2");

            var metrics = cacheService.GetMetrics();

            // Assert
            metrics.HitRatio.Should().BeGreaterThan(0);
            metrics.HitRatio.Should().BeLessThanOrEqualTo(100);
        }

        [Fact]
        public void GetCacheInfo_ShouldReturnConfiguration()
        {
            // Arrange & Act
            var cacheService = new CacheService();
            var info = cacheService.GetCacheInfo();

            // Assert
            info.Should().NotBeNull();
            info.Enabled.Should().BeTrue();
            info.DefaultTTLSeconds.Should().BeGreaterThan(0);
            info.SchemaTTLSeconds.Should().BeGreaterThan(0);
            info.ProcedureTTLSeconds.Should().BeGreaterThan(0);
        }

        [Fact]
        public void ResetMetrics_ShouldClearMetrics()
        {
            // Arrange
            var cacheService = new CacheService();
            cacheService.GetOrCreate<string>("key1", () => "value1");
            cacheService.GetOrCreate<string>("key1", () => throw new Exception("Should not be called"));

            var metricsBefore = cacheService.GetMetrics();
            metricsBefore.Hits.Should().BeGreaterThan(0);

            // Act
            cacheService.ResetMetrics();
            var metricsAfter = cacheService.GetMetrics();

            // Assert
            metricsAfter.Hits.Should().Be(0);
            metricsAfter.Misses.Should().Be(0);
        }

        [Fact]
        public void GenerateTablesCacheKey_ShouldCreateCorrectKey()
        {
            // Act
            var key = CacheService.GenerateTablesCacheKey("testdb");

            // Assert
            key.Should().Be("tables:testdb");
        }

        [Fact]
        public void GenerateTablesCacheKey_WithSchema_ShouldIncludeSchema()
        {
            // Act
            var key = CacheService.GenerateTablesCacheKey("testdb", "dbo");

            // Assert
            key.Should().Be("tables:testdb:dbo");
        }

        [Fact]
        public void GenerateProceduresCacheKey_ShouldCreateCorrectKey()
        {
            // Act
            var key = CacheService.GenerateProceduresCacheKey("testdb");

            // Assert
            key.Should().Be("procedures:testdb");
        }

        [Fact]
        public void GenerateSchemaCacheKey_ShouldCreateCorrectKey()
        {
            // Act
            var key = CacheService.GenerateSchemaCacheKey("testdb", "Users");

            // Assert
            key.Should().Be("schema:testdb:Users");
        }

        [Fact]
        public void GenerateColumnsCacheKey_ShouldCreateCorrectKey()
        {
            // Act
            var key = CacheService.GenerateColumnsCacheKey("testdb", "Users");

            // Assert
            key.Should().Be("columns:testdb:Users");
        }

        [Fact]
        public void GenerateDependenciesCacheKey_ShouldCreateCorrectKey()
        {
            // Act
            var key = CacheService.GenerateDependenciesCacheKey("testdb", "Users");

            // Assert
            key.Should().Be("dependencies:testdb:Users");
        }

        [Fact]
        public void GetTTLForType_WithSchemaType_ReturnsSchemaTTL()
        {
            // Act
            var ttl = CacheService.GetTTLForType("schema");

            // Assert
            ttl.Should().BeGreaterThan(TimeSpan.Zero);
        }

        [Fact]
        public void GetTTLForType_WithProcedureType_ReturnsProcedureTTL()
        {
            // Act
            var ttl = CacheService.GetTTLForType("procedure");

            // Assert
            ttl.Should().BeGreaterThan(TimeSpan.Zero);
        }

        [Fact]
        public void GetTTLForType_WithUnknownType_ReturnsDefaultTTL()
        {
            // Act
            var ttl = CacheService.GetTTLForType("unknown");

            // Assert
            ttl.Should().BeGreaterThan(TimeSpan.Zero);
        }

        [Fact]
        public async Task GetOrCreateAsync_WithCustomTTL_ShouldUseCustomTTL()
        {
            // Arrange
            var cacheService = new CacheService();
            var key = "custom_ttl_key";
            var customTTL = TimeSpan.FromSeconds(1);

            // Act
            var result = await cacheService.GetOrCreateAsync(key, async () => await Task.FromResult("value"), customTTL);

            // Assert
            result.Should().Be("value");
        }

        [Fact]
        public void GetCacheInfo_ShouldTrackPrefixes()
        {
            // Arrange
            var cacheService = new CacheService();
            cacheService.Set<string>("tables:db1", "value1");
            cacheService.Set<string>("procedures:db1", "value2");
            cacheService.Set<string>("schema:db1:table1", "value3");

            // Act
            var info = cacheService.GetCacheInfo();

            // Assert
            info.CachedKeyPrefixes.Should().ContainKey("tables");
            info.CachedKeyPrefixes.Should().ContainKey("procedures");
            info.CachedKeyPrefixes.Should().ContainKey("schema");
        }

        [Fact]
        public void RemoveByPattern_WithNoMatches_ShouldReturnZero()
        {
            // Arrange
            var cacheService = new CacheService();
            cacheService.Set<string>("tables:db1", "value1");

            // Act
            var removedCount = cacheService.RemoveByPattern("procedures:*");

            // Assert
            removedCount.Should().Be(0);
        }

        [Fact]
        public async Task GetOrCreateAsync_WithMultiplePatterns_ShouldCacheCorrectly()
        {
            // Arrange
            var cacheService = new CacheService();
            var key1 = "test:key:1";
            var key2 = "test:key:2";

            // Act
            var result1 = await cacheService.GetOrCreateAsync<string>(key1, async () => await Task.FromResult("value1"));
            var result2 = await cacheService.GetOrCreateAsync<string>(key2, async () => await Task.FromResult("value2"));

            // Assert
            result1.Should().Be("value1");
            result2.Should().Be("value2");
        }

        [Fact]
        public void CacheMetrics_HitRatio_WithNoOperations_ShouldBeZero()
        {
            // Arrange
            var metrics = new CacheMetrics();

            // Act
            var hitRatio = metrics.HitRatio;

            // Assert
            hitRatio.Should().Be(0);
        }

        [Fact]
        public void CacheMetrics_RecordHit_ShouldIncrementHits()
        {
            // Arrange
            var metrics = new CacheMetrics();

            // Act
            metrics.RecordHit();
            metrics.RecordHit();

            // Assert
            metrics.Hits.Should().Be(2);
        }

        [Fact]
        public void CacheMetrics_RecordMiss_ShouldIncrementMisses()
        {
            // Arrange
            var metrics = new CacheMetrics();

            // Act
            metrics.RecordMiss();
            metrics.RecordMiss();
            metrics.RecordMiss();

            // Assert
            metrics.Misses.Should().Be(3);
        }

        [Fact]
        public void CacheMetrics_Reset_ShouldClearMetrics()
        {
            // Arrange
            var metrics = new CacheMetrics();
            metrics.RecordHit();
            metrics.RecordMiss();

            // Act
            metrics.Reset();

            // Assert
            metrics.Hits.Should().Be(0);
            metrics.Misses.Should().Be(0);
        }

        [Fact]
        public void CacheMetrics_TotalOperations_ShouldSumHitsAndMisses()
        {
            // Arrange
            var metrics = new CacheMetrics();
            metrics.RecordHit();
            metrics.RecordHit();
            metrics.RecordMiss();

            // Act
            var total = metrics.TotalOperations;

            // Assert
            total.Should().Be(3);
        }
    }
}
