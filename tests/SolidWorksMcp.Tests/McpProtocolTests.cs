using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SolidWorksMcp.Tests;

/// <summary>
/// Starts the real server executable over stdio and talks MCP to it. On non-Windows hosts the SolidWorks
/// calls must fail cleanly with a tool error (not crash the server or corrupt stdout).
/// </summary>
public class McpProtocolTests
{
    private static string ServerDll => Path.Combine(AppContext.BaseDirectory, "SolidWorksMcp.dll");

    private static Task<McpClient> StartAsync(Dictionary<string, string?>? env = null) => McpClient.CreateAsync(
        new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "solidworks-mcp",
            Command = "dotnet",
            Arguments = new[] { ServerDll },
            EnvironmentVariables = env ?? new Dictionary<string, string?>(),
        }));

    [Fact]
    public async Task ListsToolsWithSchemasAndInstructions()
    {
        await using var client = await StartAsync();
        Assert.Equal("solidworks-mcp", client.ServerInfo?.Name);
        Assert.Contains("millimetres", client.ServerInstructions ?? string.Empty);

        var tools = await client.ListToolsAsync();
        Assert.True(tools.Count >= 35, $"only {tools.Count} tools");
        var sketch = tools.Single(t => t.Name == "sw_create_sketch");
        string schema = sketch.JsonSchema.GetRawText();
        Assert.Contains("\"entities\"", schema);
        Assert.Contains("\"radius\"", schema, StringComparison.OrdinalIgnoreCase);
        Assert.True(tools.Single(t => t.Name == "sw_list_open_documents").ProtocolTool.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public async Task MacroToolIsDisabledByDefault()
    {
        await using var client = await StartAsync();
        var result = await client.CallToolAsync("sw_run_macro", new Dictionary<string, object?> { ["macroPath"] = "C:\\x.swp" });
        Assert.True(result.IsError);
        Assert.Contains("SWMCP_ALLOW_MACROS", Text(result));
    }

    [Fact]
    public async Task AllowedRootsAreEnforcedBeforeTouchingSolidWorks()
    {
        string root = Directory.CreateTempSubdirectory("swmcp_proto").FullName;
        try
        {
            await using var client = await StartAsync(new() { ["SWMCP_ALLOWED_ROOTS"] = root });
            var result = await client.CallToolAsync("sw_export", new Dictionary<string, object?>
            {
                ["outputPath"] = Path.Combine(Path.GetTempPath(), "outside", "x.step"),
            });
            Assert.True(result.IsError);
            Assert.Contains("denied", Text(result));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SolidWorksCallsFailCleanlyWithoutSolidWorks()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // on a Windows box SolidWorks may legitimately be installed
        }
        await using var client = await StartAsync();
        var result = await client.CallToolAsync("sw_connect", new Dictionary<string, object?>());
        Assert.True(result.IsError);
        Assert.Contains("Windows", Text(result));

        // server is still healthy afterwards
        Assert.NotEmpty(await client.ListToolsAsync());
    }

    private static string Text(CallToolResult r) => string.Join("\n", r.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
