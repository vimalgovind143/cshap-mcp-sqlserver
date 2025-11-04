using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace SqlServerMcpServer
{
    [McpServerToolType]
    public static class SqlServerTools
    {
        private static string _currentConnectionString = Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING") 
            ?? "Server=localhost;Database=master;Trusted_Connection=true;TrustServerCertificate=true;";
        
        private static string _currentDatabase = GetDatabaseFromConnectionString(_currentConnectionString);
        private static string _serverName = Environment.GetEnvironmentVariable("MCP_SERVER_NAME") ?? "SQL Server MCP";
        private static string _environment = Environment.GetEnvironmentVariable("MCP_ENVIRONMENT") ?? "unknown";

        private static string GetDatabaseFromConnectionString(string connectionString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                return builder.InitialCatalog ?? "master";
            }
            catch
            {
                return "master";
            }
        }

        private static string CreateConnectionStringForDatabase(string databaseName)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(_currentConnectionString)
                {
                    InitialCatalog = databaseName
                };
                return builder.ConnectionString;
            }
            catch
            {
                return _currentConnectionString;
            }
        }

        private static bool IsReadOnlyQuery(string query, out string blockedOperation)
        {
            blockedOperation = null;
            var normalizedQuery = query.Trim().ToUpper();
            
            // Block dangerous operations and identify what was blocked
            var dangerousKeywords = new[] { 
                "INSERT", "UPDATE", "DELETE", "DROP", "CREATE", "ALTER", 
                "TRUNCATE", "EXEC", "EXECUTE", "MERGE", "BULK", "GRANT", "REVOKE", "DENY"
            };
            
            foreach (var keyword in dangerousKeywords)
            {
                if (normalizedQuery.Contains(keyword + " ") || 
                    normalizedQuery.Contains(keyword + "\n") || 
                    normalizedQuery.StartsWith(keyword))
                {
                    blockedOperation = keyword;
                    return false;
                }
            }
            
            // Ensure query starts with SELECT
            if (!normalizedQuery.StartsWith("SELECT"))
            {
                blockedOperation = "NON_SELECT_STATEMENT";
                return false;
            }
            
            return true;
        }

        [McpServerTool, Description("Get the current database connection info")]
        public static string GetCurrentDatabase()
        {
            return JsonSerializer.Serialize(new
            {
                server_name = _serverName,
                environment = _environment,
                current_database = _currentDatabase,
                connection_info = "Connected and ready",
                security_mode = "READ_ONLY",
                allowed_operations = new[] { "SELECT queries only", "Database listing", "Table schema inspection", "Database switching" }
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        [McpServerTool, Description("Switch to a different database on the same server")]
        public static string SwitchDatabase([Description("The name of the database to switch to")] string databaseName)
        {
            try
            {
                // Test connection to the new database first
                var testConnectionString = CreateConnectionStringForDatabase(databaseName);
                using var testConnection = new SqlConnection(testConnectionString);
                testConnection.Open();
                
                _currentConnectionString = testConnectionString;
                _currentDatabase = databaseName;
                
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Successfully switched to database: {databaseName}",
                    current_database = _currentDatabase
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Failed to switch to database {databaseName}: {ex.Message}",
                    current_database = _currentDatabase
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get a list of all databases on the SQL Server instance")]
        public static async Task<string> GetDatabasesAsync()
        {
            try
            {
                // Use master database connection for listing databases
                var masterConnectionString = CreateConnectionStringForDatabase("master");
                using var connection = new SqlConnection(masterConnectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        name AS database_name,
                        database_id,
                        create_date,
                        state_desc,
                        CASE WHEN name = @CurrentDb THEN 1 ELSE 0 END AS is_current
                    FROM sys.databases
                    WHERE name NOT IN ('master', 'tempdb', 'model', 'msdb')
                    ORDER BY name";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@CurrentDb", _currentDatabase);

                using var reader = await command.ExecuteReaderAsync();

                var databases = new List<Dictionary<string, object>>();
                
                while (await reader.ReadAsync())
                {
                    var database = new Dictionary<string, object>
                    {
                        ["database_name"] = reader["database_name"],
                        ["database_id"] = reader["database_id"],
                        ["create_date"] = reader["create_date"],
                        ["state_desc"] = reader["state_desc"],
                        ["is_current"] = reader["is_current"]
                    };
                    databases.Add(database);
                }

                return JsonSerializer.Serialize(databases, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return $"Error getting databases: {ex.Message}";
            }
        }

        [McpServerTool, Description("Execute a read-only SQL query on the current database")]
        public static async Task<string> ExecuteQueryAsync([Description("The SQL query to execute (SELECT statements only)")] string query)
        {
            try
            {
                // Validate read-only operation
                if (!IsReadOnlyQuery(query, out string blockedOperation))
                {
                    var errorMessage = blockedOperation switch
                    {
                        "INSERT" => "❌ INSERT operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "UPDATE" => "❌ UPDATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "DELETE" => "❌ DELETE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "DROP" => "❌ DROP operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "CREATE" => "❌ CREATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "ALTER" => "❌ ALTER operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "TRUNCATE" => "❌ TRUNCATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "EXEC" or "EXECUTE" => "❌ EXEC/EXECUTE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "MERGE" => "❌ MERGE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "BULK" => "❌ BULK operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "GRANT" => "❌ GRANT operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "REVOKE" => "❌ REVOKE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "DENY" => "❌ DENY operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "NON_SELECT_STATEMENT" => "❌ Only SELECT statements are allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        _ => $"❌ {blockedOperation} operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing."
                    };

                    return JsonSerializer.Serialize(new
                    {
                        server_name = _serverName,
                        environment = _environment,
                        database = _currentDatabase,
                        error = errorMessage,
                        blocked_operation = blockedOperation,
                        blocked_query = query,
                        operation_type = "BLOCKED",
                        security_mode = "READ_ONLY_ENFORCED",
                        allowed_operations = new[] { "SELECT queries for data retrieval", "Database listing", "Table schema inspection", "Database switching" },
                        help = "This MCP server is configured for READ-ONLY access to prevent accidental data modification. Use SELECT statements to query data."
                    }, new JsonSerializerOptions { WriteIndented = true });
                }

                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                // Set read-only intent for additional safety
                using var command = new SqlCommand(query, connection)
                {
                    CommandTimeout = 30 // Prevent long-running queries
                };
                
                using var reader = await command.ExecuteReaderAsync();

                var results = new List<Dictionary<string, object>>();
                
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var value = reader.GetValue(i);
                        row[columnName] = value is DBNull ? null : value;
                    }
                    results.Add(row);
                }

                return JsonSerializer.Serialize(new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    row_count = results.Count,
                    operation_type = "READ_ONLY_SELECT",
                    security_mode = "READ_ONLY_ENFORCED",
                    message = "✅ Read-only SELECT query executed successfully",
                    data = results
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    error = $"SQL Error: {ex.Message}",
                    operation_type = "ERROR",
                    security_mode = "READ_ONLY_ENFORCED",
                    help = "Check your SQL syntax and ensure you're only using SELECT statements."
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get a list of all tables in the current database")]
        public static async Task<string> GetTablesAsync()
        {
            try
            {
                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        t.name AS table_name,
                        s.name AS schema_name,
                        p.rows AS row_count
                    FROM sys.tables t
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    LEFT JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0,1)
                    GROUP BY t.name, s.name, p.rows
                    ORDER BY s.name, t.name";

                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                var tables = new List<Dictionary<string, object>>();
                
                while (await reader.ReadAsync())
                {
                    var table = new Dictionary<string, object>
                    {
                        ["table_name"] = reader["table_name"],
                        ["schema_name"] = reader["schema_name"],
                        ["row_count"] = reader["row_count"]
                    };
                    tables.Add(table);
                }

                return JsonSerializer.Serialize(new
                {
                    database = _currentDatabase,
                    table_count = tables.Count,
                    tables = tables
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    database = _currentDatabase,
                    error = ex.Message
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get the schema information for a specific table")]
        public static async Task<string> GetTableSchemaAsync(
            [Description("Name of the table")] string tableName, 
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo")
        {
            try
            {
                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        c.name AS column_name,
                        t.name AS data_type,
                        c.max_length,
                        c.is_nullable,
                        c.is_identity
                    FROM sys.columns c
                    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                    INNER JOIN sys.tables tbl ON c.object_id = tbl.object_id
                    INNER JOIN sys.schemas s ON tbl.schema_id = s.schema_id
                    WHERE tbl.name = @tableName AND s.name = @schemaName
                    ORDER BY c.column_id";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@tableName", tableName);
                command.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                using var reader = await command.ExecuteReaderAsync();

                var columns = new List<Dictionary<string, object>>();
                
                while (await reader.ReadAsync())
                {
                    var column = new Dictionary<string, object>
                    {
                        ["column_name"] = reader["column_name"],
                        ["data_type"] = reader["data_type"],
                        ["max_length"] = reader["max_length"],
                        ["is_nullable"] = reader["is_nullable"],
                        ["is_identity"] = reader["is_identity"]
                    };
                    columns.Add(column);
                }

                return JsonSerializer.Serialize(new
                {
                    database = _currentDatabase,
                    table_name = tableName,
                    schema_name = schemaName,
                    column_count = columns.Count,
                    columns = columns
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    database = _currentDatabase,
                    table_name = tableName,
                    schema_name = schemaName,
                    error = ex.Message
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }
    }
}
