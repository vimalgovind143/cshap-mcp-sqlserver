# SQL Server MCP Server

A Model Context Protocol (MCP) server that provides tools for interacting with Microsoft SQL Server databases, built with the official C# SDK.

## Features

- **🔒 Read-Only Security**: Enforced SELECT-only operations to prevent accidental data modification
- **Dynamic Database Switching**: Switch between databases on the same server without restarting
- **Database Listing**: View all available databases with current database highlighted
- **Execute SQL Queries**: Run read-only SQL queries against the current database
- **List Tables**: Get all tables in the current database with row counts
- **Get Table Schema**: Retrieve column information for specific tables
- **Connection Info**: Display current database connection status

## 🔒 Security Features

This MCP server is designed with **read-only security** to prevent accidental data modification:

### **Blocked Operations:**
- ❌ INSERT, UPDATE, DELETE statements
- ❌ DROP, CREATE, ALTER statements  
- ❌ TRUNCATE, MERGE operations
- ❌ EXEC/EXECUTE stored procedures
- ❌ GRANT, REVOKE, DENY permissions
- ❌ BULK operations
- ❌ Any non-SELECT statements

### **Allowed Operations:**
- ✅ SELECT queries for data retrieval
- ✅ Database listing and switching
- ✅ Table schema inspection
- ✅ Connection status queries

### **Error Messages:**
When a blocked operation is attempted, the server provides clear, specific error messages:
```
❌ UPDATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.
```

### **Additional Safety Features:**
- Query timeout protection (30 seconds)
- Input validation and sanitization
- Detailed error reporting with helpful guidance
- Security mode indicators in all responses

## Dynamic Database Management

The MCP server now supports dynamic database switching, allowing you to:

1. **List all databases** - See every database on the SQL Server instance
2. **Switch databases** - Change the active database without restarting the server
3. **Track current context** - All operations (queries, table listing) work on the current database
4. **Connection validation** - Server validates database connections before switching

**Example Workflow:**
```
User: List all databases
Server: [Shows all databases with current one highlighted]

User: Switch to Northwind database  
Server: [Successfully switches and confirms]

User: Show me all tables
Server: [Shows tables from Northwind database]
```

This feature is particularly useful when working with multiple databases on the same server, such as development, staging, and production environments.

## Setup

### Prerequisites

- .NET 10.0 SDK (preview)
- SQL Server (local or remote)
- Access to the target database

### Installation

1. Clone or download this project
2. Navigate to the project directory
3. Restore NuGet packages:

```bash
dotnet restore
```

4. Build the project:

```bash
dotnet build
```

### Configuration

Set the connection string using an environment variable:

```bash
# Windows
set SQLSERVER_CONNECTION_STRING="Server=your_server;Database=your_database;User Id=your_username;Password=your_password;TrustServerCertificate=true;"

# PowerShell
$env:SQLSERVER_CONNECTION_STRING="Server=your_server;Database=your_database;User Id=your_username;Password=your_password;TrustServerCertificate=true;"

# Linux/macOS
export SQLSERVER_CONNECTION_STRING="Server=your_server;Database=your_database;User Id=your_username;Password=your_password;TrustServerCertificate=true;"
```

**Default Connection String**: If no environment variable is set, it defaults to:
```
Server=localhost;Database=master;Trusted_Connection=true;TrustServerCertificate=true;
```

### Running the Server

```bash
dotnet run
```

The server will start and listen for MCP protocol messages via stdio.

## Integration with Claude Desktop

1. Copy the `claude_desktop_config.json` file to your Claude Desktop configuration directory
2. Update the connection string in the config file to match your database
3. Restart Claude Desktop

The configuration file should be placed at:
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Linux**: `~/.config/claude/claude_desktop_config.json`

## Available Tools

### 1. GetCurrentDatabase
Get the current database connection info.

**Parameters:** None

**Example Usage:**
```
Show me the current database connection info
```

### 2. SwitchDatabase
Switch to a different database on the same server.

**Parameters:**
- `databaseName` (string): The name of the database to switch to

**Example Usage:**
```
Switch to the Northwind database
```

### 3. GetDatabases
Get a list of all databases on the SQL Server instance with current database highlighted.

**Parameters:** None

**Example Usage:**
```
List all databases on this SQL Server instance
```

### 4. ExecuteQuery
Execute a SQL query on the current database.

**Parameters:**
- `query` (string): The SQL query to execute

**Example Usage:**
```
Please execute "SELECT TOP 10 * FROM Users ORDER BY CreatedDate DESC" and show me the results
```

### 5. GetTables
Get a list of all tables in the current database with row counts.

**Parameters:** None

**Example Usage:**
```
Show me all tables in the current database
```

### 6. GetTableSchema
Get the schema information for a specific table.

**Parameters:**
- `tableName` (string): Name of the table
- `schemaName` (string, optional): Schema name (defaults to "dbo")

**Example Usage:**
```
Get the schema for the Users table
```

## Connection String Examples

### Windows Authentication
```
Server=localhost;Database=YourDatabase;Trusted_Connection=true;TrustServerCertificate=true;
```

### SQL Server Authentication
```
Server=localhost;Database=YourDatabase;User Id=your_username;Password=your_password;TrustServerCertificate=true;
```

### Azure SQL Database
```
Server=your_server.database.windows.net;Database=YourDatabase;User Id=your_username@your_server;Password=your_password;Encrypt=true;TrustServerCertificate=false;
```

## Security Considerations

- This server executes SQL queries directly against your database
- Ensure proper database permissions are configured
- Use parameterized queries when possible to prevent SQL injection
- Consider limiting the database user's permissions to only what's necessary
- Never expose connection strings with passwords in version control

## Error Handling

The server returns descriptive error messages for:
- Connection failures
- Invalid SQL syntax
- Permission issues
- Database not found

## Development

To modify or extend the server:

1. Add new methods to the `SqlServerTools` class
2. Decorate them with the `[McpServerTool]` attribute
3. Add `[Description]` attributes for parameters
4. Rebuild the project

Example new tool:
```csharp
[McpServerTool, Description("Get all stored procedures in the database")]
public static async Task<string> GetStoredProceduresAsync()
{
    // Implementation here
}
```

## Testing

To test the server locally:

1. Set up a local SQL Server instance
2. Configure the connection string
3. Run the server: `dotnet run`
4. Test with Claude Desktop or other MCP-compatible clients

## Dependencies

- `ModelContextProtocol` (0.4.0-preview.3) - Official MCP C# SDK
- `Microsoft.Data.SqlClient` (5.2.2) - SQL Server connectivity
- `Microsoft.Extensions.Hosting` (10.0.0-preview.3.25171.5) - Host infrastructure

## License

This project is provided as-is for educational and development purposes.
