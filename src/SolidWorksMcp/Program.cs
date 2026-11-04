using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SolidWorksMcp;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP JSON-RPC stream: every log line must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddSingleton(ServerOptions.FromEnvironment());
builder.Services.AddSingleton<PathGuard>();
builder.Services.AddSingleton<StaDispatcher>();
builder.Services.AddSingleton<SolidWorksSession>();

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "solidworks-mcp", Version = ServerVersion.Current };
        o.ServerInstructions = ServerInstructions.Text;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
