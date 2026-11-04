using ModelContextProtocol;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class HoleSpecTests
{
    [Theory]
    [InlineData("clearance", swWzdGeneralHoleTypes_e.swWzdHole, swWzdHoleStandardFastenerTypes_e.swStandardISOScrewClearances)]
    [InlineData("Tapped", swWzdGeneralHoleTypes_e.swWzdTap, swWzdHoleStandardFastenerTypes_e.swStandardISOTappedHole)]
    [InlineData("counterbore", swWzdGeneralHoleTypes_e.swWzdCounterBore, swWzdHoleStandardFastenerTypes_e.swStandardISOSocketHeadCap)]
    [InlineData("countersink", swWzdGeneralHoleTypes_e.swWzdCounterSink, swWzdHoleStandardFastenerTypes_e.swStandardISOSocketCTSKFlatHead)]
    public void MapsKindsToIsoTables(string kind, swWzdGeneralHoleTypes_e type, swWzdHoleStandardFastenerTypes_e fastener)
    {
        var s = HoleSpec.Parse(kind, "M6", "through_all", null, null);
        Assert.Equal(type, s.HoleType);
        Assert.Equal(fastener, s.FastenerType);
        Assert.Equal(swEndConditions_e.swEndCondThroughAll, s.EndCondition);
        Assert.Null(s.DepthMm);
    }

    [Theory]
    [InlineData("m6", "M6")]
    [InlineData(" M10 ", "M10")]
    [InlineData("M2.5", "M2.5")]
    [InlineData("M8x1", "M8x1.0")]
    [InlineData("m8 x 1.25", "M8x1.25")]
    [InlineData("M12×1.5", "M12x1.5")]
    public void NormalizesSizes(string input, string expected) => Assert.Equal(expected, HoleSpec.NormalizeSize(input));

    [Theory]
    [InlineData("6mm")]
    [InlineData("#10-32")]
    [InlineData("M0.5")]
    [InlineData("M120")]
    public void RejectsNonIsoSizes(string size) => Assert.Throws<McpException>(() => HoleSpec.NormalizeSize(size));

    [Fact]
    public void BlindKeepsDepth()
    {
        var s = HoleSpec.Parse("tapped", "M8", "blind", 20, 15);
        Assert.Equal(swEndConditions_e.swEndCondBlind, s.EndCondition);
        Assert.Equal(20, s.DepthMm);
        Assert.Equal(15, s.ThreadDepthMm);
    }

    [Fact]
    public void ThroughAllDropsDepth() => Assert.Null(HoleSpec.Parse("clearance", "M6", "through_all", 12, null).DepthMm);

    [Theory]
    [InlineData("slot", "M6", "through_all", null, null, "Unknown hole type")]
    [InlineData("clearance", "M6", "up_to_next", null, null, "through_all or blind")]
    [InlineData("clearance", "M6", "blind", null, null, "requires depthMm")]
    [InlineData("clearance", "M6", "blind", 0.0, null, "requires depthMm")]
    [InlineData("counterbore", "M6", "through_all", null, 10.0, "only to tapped")]
    [InlineData("tapped", "M6", "blind", 10.0, 12.0, "cannot exceed")]
    [InlineData("tapped", "M6", "through_all", null, -1.0, "must be > 0")]
    public void RejectsInvalidRequests(string kind, string size, string end, double? depth, double? thread, string expected)
        => Assert.Contains(expected, Assert.Throws<McpException>(() => HoleSpec.Parse(kind, size, end, depth, thread)).Message);

    [Fact]
    public void PointsValidation()
    {
        HoleSpec.ValidatePoints(new[] { new[] { 0d, 0, 10 }, new[] { 20d, 0, 10 } });
        Assert.Throws<McpException>(() => HoleSpec.ValidatePoints(null));
        Assert.Throws<McpException>(() => HoleSpec.ValidatePoints(Array.Empty<double[]>()));
        Assert.Contains("must be [x, y, z]", Assert.Throws<McpException>(() => HoleSpec.ValidatePoints(new[] { new[] { 0d, 0 } })).Message);
        Assert.Contains("duplicates", Assert.Throws<McpException>(() => HoleSpec.ValidatePoints(new[] { new[] { 5d, 5, 10 }, new[] { 5.001, 5, 10 } })).Message);
    }

    [Fact]
    public void PlaneDistanceUsesUnnormalizedNormal()
        => Assert.Equal(3, HoleSpec.PlaneDistanceMm(new[] { 0d, 0, 5 }, new[] { 0d, 0, 10 }, new[] { 40d, -7, 13 }), 9);

    [Fact]
    public void PlaneDistanceOnTiltedPlane()
    {
        double[] n = { 1, 1, 0 };
        Assert.Equal(0, HoleSpec.PlaneDistanceMm(n, new[] { 0d, 0, 0 }, new[] { 10d, -10, 50 }), 9);
        Assert.Equal(Math.Sqrt(2), HoleSpec.PlaneDistanceMm(n, new[] { 0d, 0, 0 }, new[] { 1d, 1, 0 }), 9);
    }

    [Fact]
    public void FirstOffPlaneFindsTheBadPoint()
    {
        double[][] pts = { new[] { 0d, 0, 10 }, new[] { 50d, 20, 10.02 }, new[] { 50d, 40, 9 } };
        Assert.Equal(2, HoleSpec.FirstOffPlane(new[] { 0d, 0, 1 }, new[] { 0d, 0, 10 }, pts, 0.05));
        Assert.Equal(-1, HoleSpec.FirstOffPlane(new[] { 0d, 0, 1 }, new[] { 0d, 0, 10 }, pts[..2], 0.05));
    }
}
