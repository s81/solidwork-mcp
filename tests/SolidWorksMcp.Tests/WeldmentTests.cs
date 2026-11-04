using ModelContextProtocol;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class WeldmentProfileLibraryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("swmcp_profiles").FullName;
    private readonly string _second = Directory.CreateTempSubdirectory("swmcp_profiles2").FullName;

    public WeldmentProfileLibraryTests()
    {
        Touch(_root, "iso", "square tube", "40 x 40 x 4");
        Touch(_root, "iso", "square tube", "50 x 50 x 5");
        Touch(_root, "iso", "angle iron", "50 x 50 x 5");
        Touch(_root, "ansi inch", "c channel", "3 x 5");
        Touch(_second, "company", "rhs", "100x50x4");
        // Noise that must be ignored: a file at the wrong depth and a non-profile file.
        File.WriteAllText(Path.Combine(_root, "iso", "readme.sldlfp"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "iso", "square tube", "notes.txt"), string.Empty);
    }

    private static void Touch(string root, string standard, string type, string size)
    {
        string dir = Path.Combine(root, standard, type);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, size + ".sldlfp"), string.Empty);
    }

    [Fact]
    public void ScansStandardTypeSize()
    {
        var all = WeldmentProfiles.Scan(new[] { _root });
        Assert.Equal(4, all.Count);
        Assert.Contains(all, p => p is { Standard: "iso", Type: "square tube", Size: "40 x 40 x 4" });
        Assert.All(all, p => Assert.EndsWith(".sldlfp", p.Path));
    }

    [Fact]
    public void ScansSeveralRootsAndSkipsMissingOnes()
    {
        var all = WeldmentProfiles.Scan(new[] { _root, Path.Combine(_root, "does-not-exist"), _second });
        Assert.Equal(5, all.Count);
        Assert.Contains(all, p => p.Standard == "company");
    }

    [Fact]
    public void SplitsFolderSetting()
        => Assert.Equal(new[] { "C:\\a", "D:\\b" }, WeldmentProfiles.SplitRoots(" C:\\a ; D:\\b ;C:\\A;"));

    [Fact]
    public void EmptySettingGivesNoRoots() => Assert.Empty(WeldmentProfiles.SplitRoots(null));

    [Theory]
    [InlineData("ISO", "Square Tube", "40 x 40 x 4")]
    [InlineData("iso", "square tube", "40x40x4")]
    public void ResolvesIgnoringCaseAndSpacing(string standard, string type, string size)
        => Assert.EndsWith(Path.Combine("square tube", "40 x 40 x 4.sldlfp"), WeldmentProfiles.Resolve(WeldmentProfiles.Scan(new[] { _root }), standard, type, size).Path);

    [Theory]
    [InlineData("din", "square tube", "40 x 40 x 4", "Standard 'din' not found")]
    [InlineData("iso", "pipe", "40 x 40 x 4", "Type 'pipe' not found")]
    [InlineData("iso", "square tube", "60 x 60 x 6", "50 x 50 x 5")]
    public void ResolveErrorsListAlternatives(string standard, string type, string size, string expected)
        => Assert.Contains(expected, Assert.Throws<McpException>(() => WeldmentProfiles.Resolve(WeldmentProfiles.Scan(new[] { _root }), standard, type, size)).Message);

    [Fact]
    public void EmptyLibraryExplainsWhereToLook()
        => Assert.Contains("File Locations", Assert.Throws<McpException>(() => WeldmentProfiles.Resolve(Array.Empty<WeldmentProfile>(), "iso", "x", "y")).Message);

    public void Dispose()
    {
        Directory.Delete(_root, true);
        Directory.Delete(_second, true);
    }
}

public class WeldmentSpecTests
{
    [Theory]
    [InlineData(null, swSolidworksWeldmentEndCondOptions_e.swEndConditionMiter)]
    [InlineData("mitre", swSolidworksWeldmentEndCondOptions_e.swEndConditionMiter)]
    [InlineData("butt1", swSolidworksWeldmentEndCondOptions_e.swEndConditionButt1)]
    [InlineData("end-butt2", swSolidworksWeldmentEndCondOptions_e.swEndConditionButt2)]
    [InlineData("none", swSolidworksWeldmentEndCondOptions_e.swEndConditionNone)]
    public void ParsesCornerTreatment(string? input, swSolidworksWeldmentEndCondOptions_e expected)
        => Assert.Equal(expected, WeldmentProfiles.ParseCornerTreatment(input));

    [Fact]
    public void CornerTreatmentRejectsTrim() => Assert.Throws<McpException>(() => WeldmentProfiles.ParseCornerTreatment("end_trim"));

    [Theory]
    [InlineData(null, swSolidworksWeldmentEndCondOptions_e.swEndConditionTrim)]
    [InlineData("end_miter", swSolidworksWeldmentEndCondOptions_e.swEndConditionMiter)]
    [InlineData("butt2", swSolidworksWeldmentEndCondOptions_e.swEndConditionButt2)]
    public void ParsesTrimType(string? input, swSolidworksWeldmentEndCondOptions_e expected)
        => Assert.Equal(expected, WeldmentProfiles.ParseTrimType(input));

    [Theory]
    [InlineData(null, swConnectedSegmentsOption_e.swConnectedSegments_SimpleCut)]
    [InlineData("coped", swConnectedSegmentsOption_e.swConnectedSegments_CopedCut)]
    public void ParsesConnectedSegments(string? input, swConnectedSegmentsOption_e expected)
        => Assert.Equal(expected, WeldmentProfiles.ParseConnectedSegments(input));

    [Theory]
    [InlineData("start", swGussetProfileLocationType_e.swGussetProfileLocationStart)]
    [InlineData(null, swGussetProfileLocationType_e.swGussetProfileLocationCenter)]
    [InlineData("END", swGussetProfileLocationType_e.swGussetProfileLocationEnd)]
    public void ParsesGussetLocation(string? input, swGussetProfileLocationType_e expected)
        => Assert.Equal(expected, WeldmentProfiles.ParseGussetLocation(input));

    [Theory]
    [InlineData("inner", swGussetThicknessType_e.swGussetThicknessInner)]
    [InlineData(null, swGussetThicknessType_e.swGussetThicknessBothSides)]
    [InlineData("outside", swGussetThicknessType_e.swGussetThicknessOuter)]
    public void ParsesGussetThickness(string? input, swGussetThicknessType_e expected)
        => Assert.Equal(expected, WeldmentProfiles.ParseGussetThickness(input));

    [Fact]
    public void MatchBodiesReportsAllMissingNames()
    {
        var ex = Assert.Throws<McpException>(() => WeldmentProfiles.MatchBodies(
            new[] { "Structural Member1[1]", "Structural Member1[2]" }, new[] { "structural member1[2]", "X", "Y" }, "bodiesToTrim"));
        Assert.Contains("X, Y", ex.Message);
        Assert.Contains("Structural Member1[1]", ex.Message);
    }

    [Fact]
    public void MatchBodiesIsCaseInsensitiveAndDeduplicates()
        => Assert.Equal(new[] { 1 }, WeldmentProfiles.MatchBodies(new[] { "A[1]", "A[2]" }, new[] { "a[2]", "A[2]" }, "x"));

    [Fact]
    public void SegmentKindAppearsInMessage()
        => Assert.StartsWith("Segment name(s)", Assert.Throws<McpException>(() => WeldmentProfiles.MatchBodies(new[] { "Line1" }, new[] { "Line9" }, "segments", "Segment", "hint")).Message);

    [Fact]
    public void EmptyWantedListRejected() => Assert.Throws<McpException>(() => WeldmentProfiles.MatchBodies(new[] { "A" }, Array.Empty<string>(), "x"));

    [Fact]
    public void PointCountRules()
    {
        WeldmentProfiles.RequirePoints(new[] { new[] { 0d, 0, 0 }, new[] { 1d, 0, 0 } }, 2, "points");
        WeldmentProfiles.RequirePoints(new[] { new[] { 0d, 0, 0 } }, 0, "points");
        Assert.Contains("exactly 2", Assert.Throws<McpException>(() => WeldmentProfiles.RequirePoints(new[] { new[] { 0d, 0, 0 } }, 2, "points")).Message);
        Assert.Throws<McpException>(() => WeldmentProfiles.RequirePoints(Array.Empty<double[]>(), 0, "points"));
        Assert.Throws<McpException>(() => WeldmentProfiles.RequirePoints(null, 0, "points"));
        Assert.Contains("[x, y, z]", Assert.Throws<McpException>(() => WeldmentProfiles.RequirePoints(new[] { new[] { 0d, 0 } }, 0, "points")).Message);
    }

    [Theory]
    [InlineData("45.00", 45)]
    [InlineData("1,250", 1250)]
    [InlineData("90°", 90)]
    [InlineData("12.5 deg", 12.5)]
    public void CutListParsesWeldmentValues(string input, double expected) => Assert.Equal(expected, CutList.ParseNumber(input));

    [Fact]
    public void CutListLeavesTextAlone() => Assert.Null(CutList.ParseNumber("SQUARE TUBE 40 X 40 X 4"));
}
