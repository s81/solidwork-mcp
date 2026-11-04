using ModelContextProtocol;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Tools;

namespace SolidWorksMcp.Tests;

public class ChamferTests
{
    [Fact]
    public void EqualDistanceUsesSameValueOnBothSides()
    {
        var (type, width, angle, other) = FeatureTools.ChamferParameters("equal_distance", 1.5, null, 45);
        Assert.Equal(swChamferType_e.swChamferDistanceDistance, type);
        Assert.Equal(0.0015, width, 12);
        Assert.Equal(0.0015, other, 12);
        Assert.Equal(0, angle);
    }

    [Fact]
    public void DistanceDistanceConvertsBothToMetres()
    {
        var (type, width, _, other) = FeatureTools.ChamferParameters("distance-distance", 2, 3, 45);
        Assert.Equal(swChamferType_e.swChamferDistanceDistance, type);
        Assert.Equal(0.002, width, 12);
        Assert.Equal(0.003, other, 12);
    }

    [Fact]
    public void AngleDistanceConvertsAngleToRadians()
    {
        var (type, width, angle, other) = FeatureTools.ChamferParameters("Angle_Distance", 2, null, 30);
        Assert.Equal(swChamferType_e.swChamferAngleDistance, type);
        Assert.Equal(0.002, width, 12);
        Assert.Equal(Math.PI / 6, angle, 12);
        Assert.Equal(0, other);
    }

    [Theory]
    [InlineData("equal_distance", 0, null, 45, "distance must be > 0")]
    [InlineData("distance_distance", 2, null, 45, "requires distance2")]
    [InlineData("distance_distance", 2, -1.0, 45, "requires distance2")]
    [InlineData("angle_distance", 2, null, 90, "between 0 and 90")]
    [InlineData("angle_distance", 2, null, 0, "between 0 and 90")]
    [InlineData("vertex", 2, null, 45, "Unknown chamfer type")]
    public void RejectsInvalidParameters(string type, double distance, double? distance2, double angle, string expected)
        => Assert.Contains(expected, Assert.Throws<McpException>(() => FeatureTools.ChamferParameters(type, distance, distance2, angle)).Message);

    [Theory]
    [InlineData("Feature", null, "requires 'feature'")]
    [InlineData("points", null, "at least one point")]
    [InlineData("faces", null, "must be feature, all or points")]
    public void SelectionValidation(string selection, string? feature, string expected)
        => Assert.Contains(expected, Assert.Throws<McpException>(() => FeatureTools.ValidateSelection(selection, feature, null)).Message);

    [Fact]
    public void SelectionIsNormalized() => Assert.Equal("all", FeatureTools.ValidateSelection(" ALL ", null, null));
}
