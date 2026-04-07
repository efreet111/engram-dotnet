using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Engram.Mcp;

/// <summary>
/// Configures and runs the Engram MCP server over stdio transport.
/// The 15 tools are declared in EngramTools.cs with [McpServerTool] attributes.
/// </summary>
public static class EngramMcpServer
{
    /// <summary>
    /// Creates a pre-configured HostApplicationBuilder with all MCP services registered.
    /// Callers should add their own services (IStore, StoreConfig, MCPConfig) and then Build() + Run().
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // All logs must go to stderr — stdout is reserved for the MCP JSON-RPC protocol.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(opts =>
        {
            opts.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<EngramTools>();

        return builder;
    }
}
