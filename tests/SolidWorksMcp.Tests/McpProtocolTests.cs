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
        Assert.True(tools.Count >= 75, $"only {tools.Count} tools");
        var sketch = tools.Single(t => t.Name == "sw_create_sketch");
        string schema = sketch.JsonSchema.GetRawText();
        Assert.Contains("\"entities\"", schema);
        Assert.Contains("\"radius\"", schema, StringComparison.OrdinalIgnoreCase);
        Assert.True(tools.Single(t => t.Name == "sw_list_open_documents").ProtocolTool.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public async Task FilletSchemaAcceptsPointArrays()
    {
        await using var client = await StartAsync();
        var fillet = (await client.ListToolsAsync()).Single(t => t.Name == "sw_fillet");
        var points = fillet.JsonSchema.GetProperty("properties").GetProperty("points");
        Assert.Contains("array", points.GetRawText());
        Assert.Contains("number", points.GetRawText());
    }

    [Theory]
    [InlineData("sw_fillet", "radius", 0.0, "radius must be > 0")]
    [InlineData("sw_revolve", "angleDegrees", 400.0, "angleDegrees must be")]
    [InlineData("sw_chamfer", "distance", 0.0, "distance must be > 0")]
    [InlineData("sw_chamfer", "angleDegrees", 95.0, "between 0 and 90")]
    public async Task FeatureToolsValidateBeforeTouchingSolidWorks(string tool, string arg, double value, string expected)
    {
        await using var client = await StartAsync();
        var args = new Dictionary<string, object?> { ["distance"] = 1.0, ["type"] = "angle_distance", [arg] = value, ["sketch"] = "Sketch1", ["selection"] = "all" };
        var result = await client.CallToolAsync(tool, args);
        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
    }

    [Theory]
    [InlineData("gear", 2, "Unsupported mate type")]
    [InlineData("coincident", 1, "exactly two")]
    [InlineData("coincident", 2, "only be used in a lock mate")]
    public async Task AddMateValidatesBeforeTouchingSolidWorks(string type, int count, string expected)
    {
        await using var client = await StartAsync();
        var refs = Enumerable.Range(1, count).Select(i => new Dictionary<string, object?> { ["component"] = $"Part-{i}" }).ToArray();
        var result = await client.CallToolAsync("sw_add_mate", new Dictionary<string, object?> { ["type"] = type, ["references"] = refs });
        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
    }

    [Theory]
    [InlineData("tapped", "6mm", "through_all", "not an ISO metric size")]
    [InlineData("counterbore", "M6", "blind", "requires depthMm")]
    [InlineData("pocket", "M6", "through_all", "Unknown hole type")]
    public async Task HoleWizardValidatesBeforeTouchingSolidWorks(string type, string size, string end, string expected)
    {
        await using var client = await StartAsync();
        var result = await client.CallToolAsync("sw_hole_wizard", new Dictionary<string, object?>
        {
            ["type"] = type, ["size"] = size, ["endCondition"] = end, ["points"] = new[] { new[] { 0.0, 0, 10 } },
        });
        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
    }

    [Theory]
    [InlineData("sw_base_flange", "kFactor", 1.5, "kFactor must be")]
    [InlineData("sw_base_flange", "thicknessMm", 0.0, "thicknessMm must be")]
    [InlineData("sw_edge_flange", "angleDegrees", 200.0, "angleDegrees must be")]
    [InlineData("sw_set_sheet_metal_parameters", "kFactor", 0.0, "kFactor must be")]
    public async Task SheetMetalToolsValidateBeforeTouchingSolidWorks(string tool, string arg, double value, string expected)
    {
        await using var client = await StartAsync();
        var args = new Dictionary<string, object?>
        {
            ["sketch"] = "Sketch1", ["thicknessMm"] = 2.0, ["bendRadiusMm"] = 1.0, ["lengthMm"] = 20.0,
            ["points"] = new[] { new[] { 0.0, 0, 0 } },
        };
        args[arg] = value;
        var result = await client.CallToolAsync(tool, args);
        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
    }

    [Theory]
    [InlineData("sw_add_structural_member", "cornerTreatment", "zigzag", "Unknown corner treatment")]
    [InlineData("sw_add_structural_member", "standard", null, "Give standard, type and size")]
    [InlineData("sw_trim_extend", "type", "weld", "Unknown trim type")]
    [InlineData("sw_add_gusset", "location", "top", "Unknown gusset location")]
    public async Task WeldmentToolsValidateBeforeTouchingSolidWorks(string tool, string arg, string? value, string expected)
    {
        await using var client = await StartAsync();
        var args = new Dictionary<string, object?>
        {
            ["sketch"] = "Sketch1", ["standard"] = "iso", ["type"] = "square tube", ["size"] = "40 x 40 x 4",
            ["bodiesToTrim"] = new[] { "A" }, ["boundaryBodies"] = new[] { "B" },
            ["points"] = new[] { new[] { 0.0, 0, 0 }, new[] { 1.0, 0, 0 } }, ["thicknessMm"] = 5.0, ["leg1Mm"] = 50.0, ["leg2Mm"] = 50.0,
        };
        args[arg] = value;
        var result = await client.CallToolAsync(tool, args);
        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
    }

    [Theory]
    [InlineData("sw_insert_bom_table", "type", "flat", "Unknown BOM type")]
    [InlineData("sw_insert_bom_table", "anchor", "middle", "Unknown anchor")]
    [InlineData("sw_export_bom_table", "outputPath", "C:\\bom.xlsx", "Unsupported export format")]
    public async Task BomTableToolsValidateBeforeTouchingSolidWorks(string tool, string arg, string value, string expected)
    {
        await using var client = await StartAsync();
        var args = new Dictionary<string, object?> { ["outputPath"] = Path.Combine(Path.GetTempPath(), "bom.csv"), [arg] = value };
        var result = await client.CallToolAsync(tool, args);
        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
    }

    [Theory]
    [InlineData("sw_insert_model_dimensions", "types", "balloons", "Unknown model item type")]
    [InlineData("sw_auto_dimension_view", "horizontalScheme", "zigzag", "unknown scheme")]
    [InlineData("sw_add_drawing_dimension", "orientation", "diagonal", "orientation must be")]
    [InlineData("sw_add_drawing_dimension", "entity1", "face", "Unknown entity")]
    public async Task DrawingDimensionToolsValidateBeforeTouchingSolidWorks(string tool, string arg, string value, string expected)
    {
        await using var client = await StartAsync();
        var args = new Dictionary<string, object?>
        {
            ["view"] = "Drawing View1", ["point1"] = new[] { 50.0, 50 }, ["textPosition"] = new[] { 50.0, 70 },
        };
        args[arg] = arg == "types" ? new[] { value } : value;
        var result = await client.CallToolAsync(tool, args);
        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
    }

    [Theory]
    [InlineData("sw_auto_balloon", "layout", "spiral", "Unknown layout")]
    [InlineData("sw_auto_balloon", "lowerText", "quantity", "split styles")]
    [InlineData("sw_add_balloon", "upperText", "custom", "needs the text")]
    [InlineData("sw_auto_balloon", "itemOrder", "random", "Unknown item order")]
    [InlineData("sw_add_stacked_balloon", "direction", "diagonal", "Unknown stack direction")]
    [InlineData("sw_edit_balloons", "style", "star", "Unknown balloon style")]
    [InlineData("sw_add_magnetic_line", "view", "", "different points")]
    public async Task BalloonToolsValidateBeforeTouchingSolidWorks(string tool, string arg, string value, string expected)
    {
        await using var client = await StartAsync();
        var args = new Dictionary<string, object?>
        {
            ["point"] = new[] { 50.0, 50 }, ["balloonPosition"] = new[] { 80.0, 90 },
            ["points"] = new[] { new[] { 50.0, 50 }, new[] { 60.0, 50 } }, ["view"] = "Drawing View1",
            ["start"] = new[] { 20.0, 20 }, ["end"] = new[] { 20.0, 20 }, [arg] = value,
        };
        var result = await client.CallToolAsync(tool, args);
        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
    }

    [Theory]
    [InlineData("sw_edit_balloons", "{\"view\":\"Drawing View1\",\"all\":true}", "Nothing to change")]
    [InlineData("sw_add_stacked_balloon", "{\"points\":[[50,50]],\"balloonPosition\":[80,90]}", "at least 2")]
    [InlineData("sw_add_magnetic_line", "{\"start\":[10,10],\"end\":[100,10],\"all\":true}", "Pass view")]
    [InlineData("sw_renumber_bom_items", "{\"startNumber\":0}", "startNumber")]
    [InlineData("sw_renumber_bom_items", "{\"sortColumn\":\"PART NUMBER\",\"followAssemblyOrder\":true}", "not both")]
    public async Task BalloonEditingValidatesBeforeTouchingSolidWorks(string tool, string json, string expected)
    {
        await using var client = await StartAsync();
        var args = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        var result = await client.CallToolAsync(tool, args);
        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
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
