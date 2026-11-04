using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class BomBuilderTests
{
    private static ComponentNode Part(string name, string path, string config = "Default", bool suppressed = false, bool exclude = false,
        Dictionary<string, string>? props = null)
        => new(name, path, config, suppressed, exclude, false, Array.Empty<ComponentNode>(), props);

    private static ComponentNode Asm(string name, string path, params ComponentNode[] children)
        => new(name, path, "Default", false, false, false, children);

    // Desk: 4 legs, 1 top, 1 frame sub-assembly (2 rails + 8 bolts), 1 suppressed drawer, 1 excluded fixture.
    private static IReadOnlyList<ComponentNode> Desk() => new[]
    {
        Part("Leg-1", @"C:\p\leg.sldprt"), Part("Leg-2", @"C:\p\leg.sldprt"), Part("Leg-3", @"C:\p\LEG.SLDPRT"), Part("Leg-4", @"C:\p\leg.sldprt"),
        Part("Top-1", @"C:\p\top.sldprt", "1200x600", props: new() { ["PartNo"] = "TP-1200" }),
        Asm("Frame-1", @"C:\p\frame.sldasm",
            Part("Rail-1", @"C:\p\rail.sldprt"), Part("Rail-2", @"C:\p\rail.sldprt"),
            Part("Bolt-1", @"C:\p\bolt.sldprt"), Part("Bolt-2", @"C:\p\bolt.sldprt"), Part("Bolt-3", @"C:\p\bolt.sldprt"), Part("Bolt-4", @"C:\p\bolt.sldprt"),
            Part("Bolt-5", @"C:\p\bolt.sldprt"), Part("Bolt-6", @"C:\p\bolt.sldprt"), Part("Bolt-7", @"C:\p\bolt.sldprt"), Part("Bolt-8", @"C:\p\bolt.sldprt")),
        Part("Drawer-1", @"C:\p\drawer.sldprt", suppressed: true),
        Part("Jig-1", @"C:\p\jig.sldprt", exclude: true),
    };

    [Fact]
    public void PartsOnlyFlattensAndCounts()
    {
        var bom = BomBuilder.Build(Desk(), "parts_only");
        Assert.Equal(new[] { ("leg.sldprt", 4), ("top.sldprt", 1), ("rail.sldprt", 2), ("bolt.sldprt", 8) },
            bom.Select(r => (r.FileName, r.Quantity)).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, bom.Select(r => r.Item));
        Assert.All(bom, r => Assert.False(r.IsAssembly));
    }

    [Fact]
    public void TopLevelKeepsSubAssembliesAsRows()
    {
        var bom = BomBuilder.Build(Desk(), "top_level");
        Assert.Equal(new[] { ("leg.sldprt", 4), ("top.sldprt", 1), ("frame.sldasm", 1) },
            bom.Select(r => (r.FileName, r.Quantity)).ToArray());
        Assert.True(bom[2].IsAssembly);
    }

    [Fact]
    public void ConfigurationsAreSeparateRows()
    {
        var bom = BomBuilder.Build(new[] { Part("A-1", "a.sldprt", "Short"), Part("A-2", "a.sldprt", "Long"), Part("A-3", "a.sldprt", "Short") }, "parts_only");
        Assert.Equal(new[] { ("Short", 2), ("Long", 1) }, bom.Select(r => (r.Configuration, r.Quantity)).ToArray());
    }

    [Fact]
    public void PropertiesArePassedThrough()
        => Assert.Equal("TP-1200", BomBuilder.Build(Desk(), "parts_only").Single(r => r.FileName == "top.sldprt").Properties["PartNo"]);

    [Fact]
    public void VirtualComponentsGroupByName()
    {
        var v = new ComponentNode("Spacer^Asm", string.Empty, "Default", false, false, true, Array.Empty<ComponentNode>());
        var bom = BomBuilder.Build(new[] { v, v }, "parts_only");
        Assert.Single(bom);
        Assert.Equal(("Spacer^Asm", 2), (bom[0].FileName, bom[0].Quantity));
    }

    [Fact]
    public void EmptySubAssemblyContributesNothingToFlatBom()
        => Assert.Empty(BomBuilder.Build(new[] { Asm("Empty-1", "empty.sldasm") }, "parts_only"));

    [Fact]
    public void UnknownModeThrows() => Assert.Throws<ArgumentException>(() => BomBuilder.Build(Desk(), "indented"));
}
