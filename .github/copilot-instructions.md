## Repo snapshot

This repository implements a Model Context Protocol (MCP) server for Microsoft SQL Server using the official C# MCP SDK. Key files:

- `Program.cs` — host bootstrap: registers the MCP server with stdio transport and Serilog logging.
- `SqlServerMcpServer.cs` — main tool implementations (class `SqlServerTools`) that expose MCP tools via `[McpServerTool]` attributes.
- `StderrJsonSink.cs` — Serilog sink that writes structured JSON logs to stderr (keeps MCP stdio clean).
- `README.md`, `claude_desktop_config.json`, `NAMED_SERVERS.md` — runtime/runbook examples and named-server configs.

## Big picture (what an agent should know)

- This is an MCP server that communicates over stdio (MCP transport is registered in `Program.cs`).
- Tools are static methods in the `SqlServerTools` class and annotated with `[McpServerTool]`. To add new tools, add a method there and include `[Description]` on parameters.
- The server enforces READ-ONLY semantics: `SqlServerTools.IsReadOnlyQuery(...)` blocks any non-SELECT operations. It also injects/caps `TOP` clauses via `ApplyTopLimit` to enforce a 100-row cap.
- Dynamic database switching is supported via `SwitchDatabase` which updates `_currentConnectionString` and `_currentDatabase` after testing the connection.
- Configuration precedence: Environment variables → `appsettings.json` (searched in several locations) → built-in defaults. Key env vars: `SQLSERVER_CONNECTION_STRING`, `SQLSERVER_COMMAND_TIMEOUT`, `MCP_SERVER_NAME`, `MCP_ENVIRONMENT`.
- Logging: Serilog JSON to `stderr` via `StderrJsonSink` with correlation IDs using `LogStart` / `LogEnd`. Do not write to stdout from code — MCP uses stdio for protocol messaging.

## Developer workflows & useful commands

- Build: `dotnet restore` then `dotnet build` in the project folder.
- Run locally: `dotnet run` (project auto-detects appsettings.json). The server listens on stdio (used by MCP clients).
- Example named-server/test launcher: `claude_desktop_config.json` and `NAMED_SERVERS.md` include `dotnet run --project <path>` examples and environment variable settings used by Windsurf/Claude Desktop.

## Project-specific conventions and patterns

- Tool declaration: any static method in `SqlServerTools` with `[McpServerTool]` becomes an exposed tool. Keep methods small and return serialized JSON (the project uses `JsonSerializer.Serialize` for responses).
- Security-first: Always assume methods must enforce read-only behavior. Reuse `IsReadOnlyQuery` and `ApplyTopLimit` for SQL handling.
- Connection handling: methods create a `SqlConnection` with `_currentConnectionString`, open, then use `SqlCommand` with `CommandTimeout = _commandTimeout` (configurable via env or `appsettings.json`).
- Logging: call `LogStart` at method start and `LogEnd` on completion (successful or error) so structured traces contain `correlation_id`, `operation`, `elapsed_ms`.

## Integration points & dependencies

- MCP SDK: `ModelContextProtocol` (tools are discovered via `.WithToolsFromAssembly()` in `Program.cs`).
- SQL client: `Microsoft.Data.SqlClient` for DB connectivity.
- Logging: Serilog with custom sink that writes JSON to stderr — keep stdout untouched.

## Examples for an agent (do this, not generic advice)

- Add a new tool that lists views:
  - Create a static method in `SqlServerTools`.
  - Decorate with `[McpServerTool, Description("List views")]`.
  - Use `_currentConnectionString` and `CommandTimeout`, return `JsonSerializer.Serialize(payload)`.

- When changing SQL handling, reuse `IsReadOnlyQuery` and `ApplyTopLimit` to preserve read-only enforcement and row-limiting.

## Files to inspect when making changes

- `SqlServerMcpServer/SqlServerMcpServer.cs` — primary place for tool logic and SQL handling.
- `SqlServerMcpServer/Program.cs` — host and registration; change here if you adjust transports or logging.
- `SqlServerMcpServer/StderrJsonSink.cs` — adjust only if you need different stderr logging semantics.

## Quick checklist for PRs

1. Ensure new tools are annotated with `[McpServerTool]` and include parameter `[Description]` attributes.
2. Preserve read-only behavior: validate queries with `IsReadOnlyQuery` before executing SQL.
3. Use `CommandTimeout` and respect the `SQLSERVER_COMMAND_TIMEOUT` env var or config.
4. Emit structured logs using `LogStart`/`LogEnd` and avoid writing to stdout.
5. Update `README.md` with any new public tool names and examples.

## Questions I couldn't infer (please confirm)

- Are there any CI commands or pipelines to run the MCP server in integration tests? (none present in repo)
- Preferred code style rules or analyzers to obey when adding public APIs? (none found)

If any of the above assumptions are incorrect or you'd like the file split into sections, tell me what to change and I will update it.
