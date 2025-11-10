using Microsoft.Data.SqlClient;
using Polly;
using Polly.CircuitBreaker;
using Serilog;
using System;
using System.Threading.Tasks;

namespace SqlServerMcpServer.Configuration
{
    /// <summary>
    /// Manages SQL Server connection pooling and retry logic
    /// Implements resilience patterns using Polly for transient fault handling
    /// </summary>
    public static class ConnectionPoolManager
    {
        private static int _maxRetryAttempts = ParseIntEnv("SQLSERVER_CONNECTION_RETRY_MAX_ATTEMPTS", 3);
        private static int _initialDelayMs = ParseIntEnv("SQLSERVER_CONNECTION_RETRY_DELAY_MS", 100);
        private static int _maxDelayMs = ParseIntEnv("SQLSERVER_CONNECTION_RETRY_MAX_DELAY_MS", 5000);
        private static double _backoffMultiplier = ParseDoubleEnv("SQLSERVER_CONNECTION_RETRY_BACKOFF_MULTIPLIER", 2.0);

        private static IAsyncPolicy<SqlConnection>? _retryPolicy;
        private static IAsyncPolicy<SqlConnection>? _circuitBreakerPolicy;
        private static IAsyncPolicy<SqlConnection>? _combinedPolicy;

        private static int _totalConnectionAttempts = 0;
        private static int _successfulConnections = 0;
        private static int _failedConnections = 0;
        private static int _retriedConnections = 0;

        private static readonly object _lockObject = new object();

        static ConnectionPoolManager()
        {
            InitializePolicies();
            Log.Information("[ConnectionPoolManager] Initialized with MaxRetries={MaxRetries}, InitialDelay={InitialDelay}ms, Multiplier={Multiplier}",
                _maxRetryAttempts, _initialDelayMs, _backoffMultiplier);
        }

        /// <summary>
        /// Initializes Polly retry and circuit breaker policies
        /// </summary>
        private static void InitializePolicies()
        {
            // Retry policy with exponential backoff
            _retryPolicy = Policy
                .Handle<SqlException>(IsTransientError)
                .Or<TimeoutException>()
                .OrResult<SqlConnection>(conn => conn == null)
                .WaitAndRetryAsync<SqlConnection>(
                    retryCount: _maxRetryAttempts,
                    sleepDurationProvider: retryAttempt =>
                        TimeSpan.FromMilliseconds(Math.Min(
                            _initialDelayMs * Math.Pow(_backoffMultiplier, retryAttempt - 1),
                            _maxDelayMs)),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        lock (_lockObject)
                        {
                            _retriedConnections++;
                        }
                        Log.Warning("[ConnectionPoolManager] Retry {RetryCount}/{MaxRetries} after {DelayMs}ms. Reason: {Reason}",
                            retryCount, _maxRetryAttempts, timespan.TotalMilliseconds,
                            outcome.Exception?.Message ?? "Connection returned null");
                    });

            // Circuit breaker policy - open after 5 consecutive failures
            _circuitBreakerPolicy = Policy
                .Handle<SqlException>(IsTransientError)
                .Or<TimeoutException>()
                .OrResult<SqlConnection>(conn => conn == null)
                .CircuitBreakerAsync<SqlConnection>(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (outcome, duration) =>
                    {
                        Log.Error("[ConnectionPoolManager] Circuit breaker opened for {Duration}s. Failure: {Reason}",
                            duration.TotalSeconds, outcome.Exception?.Message ?? "Multiple connection failures");
                    },
                    onReset: () =>
                    {
                        Log.Information("[ConnectionPoolManager] Circuit breaker reset");
                    });

            // Combine both policies: circuit breaker wraps retry
            _combinedPolicy = Policy.WrapAsync(_circuitBreakerPolicy, _retryPolicy);
        }

        /// <summary>
        /// Determines if a SQL exception is transient and retryable
        /// </summary>
        private static bool IsTransientError(SqlException ex)
        {
            // SQL Server transient error numbers
            int[] transientErrorNumbers =
            {
                -2,      // Timeout
                -1,      // Other transient errors
                2,       // Adapter not found
                53,      // Named pipe protocol error
                233,     // Connection initialization error
                40197,   // Service objective temporarily unavailable
                40501,   // Service is currently busy
                40613,   // Database unavailable
                49918,   // Cannot open database
                49919,   // Cannot open database
                49920    // Cannot open database
            };

            foreach (SqlError error in ex.Errors)
            {
                foreach (int transientNumber in transientErrorNumbers)
                {
                    if (error.Number == transientNumber)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Creates a connection with retry logic
        /// </summary>
        /// <returns>A new SqlConnection instance with retry resilience</returns>
        public static async Task<SqlConnection> CreateConnectionWithRetryAsync()
        {
            lock (_lockObject)
            {
                _totalConnectionAttempts++;
            }

            try
            {
                var connection = await _combinedPolicy!.ExecuteAsync(async () =>
                {
                    var conn = SqlConnectionManager.CreateConnection();
                    await conn.OpenAsync();
                    return conn;
                });

                lock (_lockObject)
                {
                    _successfulConnections++;
                }

                return connection;
            }
            catch (Exception ex)
            {
                lock (_lockObject)
                {
                    _failedConnections++;
                }

                Log.Error(ex, "[ConnectionPoolManager] Failed to create connection after {Retries} retries",
                    _maxRetryAttempts);
                throw;
            }
        }

        /// <summary>
        /// Creates a connection synchronously with retry logic
        /// For use in non-async contexts
        /// </summary>
        public static SqlConnection CreateConnectionWithRetry()
        {
            return CreateConnectionWithRetryAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Gets current connection pool statistics
        /// </summary>
        public static PoolStatistics GetPoolStatistics()
        {
            lock (_lockObject)
            {
                return new PoolStatistics
                {
                    TotalAttempts = _totalConnectionAttempts,
                    SuccessfulConnections = _successfulConnections,
                    FailedConnections = _failedConnections,
                    RetriedConnections = _retriedConnections,
                    SuccessRate = _totalConnectionAttempts > 0
                        ? (_successfulConnections / (double)_totalConnectionAttempts) * 100
                        : 0,
                    RetryRate = _totalConnectionAttempts > 0
                        ? (_retriedConnections / (double)_totalConnectionAttempts) * 100
                        : 0
                };
            }
        }

        /// <summary>
        /// Resets connection pool statistics
        /// </summary>
        public static void ResetStatistics()
        {
            lock (_lockObject)
            {
                _totalConnectionAttempts = 0;
                _successfulConnections = 0;
                _failedConnections = 0;
                _retriedConnections = 0;
            }
            Log.Information("[ConnectionPoolManager] Statistics reset");
        }

        /// <summary>
        /// Gets the configured retry policy
        /// </summary>
        public static IAsyncPolicy<SqlConnection> GetRetryPolicy()
        {
            return _retryPolicy ?? throw new InvalidOperationException("Retry policy not initialized");
        }

        /// <summary>
        /// Parses an integer from environment variable
        /// </summary>
        private static int ParseIntEnv(string name, int defaultValue)
        {
            var val = System.Environment.GetEnvironmentVariable(name);
            return int.TryParse(val, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }

        /// <summary>
        /// Parses a double from environment variable
        /// </summary>
        private static double ParseDoubleEnv(string name, double defaultValue)
        {
            var val = System.Environment.GetEnvironmentVariable(name);
            return double.TryParse(val, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }
    }

    /// <summary>
    /// Represents connection pool statistics
    /// </summary>
    public class PoolStatistics
    {
        public int TotalAttempts { get; set; }
        public int SuccessfulConnections { get; set; }
        public int FailedConnections { get; set; }
        public int RetriedConnections { get; set; }
        public double SuccessRate { get; set; }
        public double RetryRate { get; set; }
    }
}
