using ModelContextProtocol;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class EdgeSelectorTests
{
    private static EdgeGeometry Line(double[] a, double[] b)
    {
        double[] mid = a.Zip(b, (x, y) => (x + y) / 2).ToArray();
        return new EdgeGeometry("line", a, b, mid, EdgeGeometry.Distance(a, b), null);
    }

    [Fact]
    public void KeyIsIndependentOfEdgeDirection()
        => Assert.Equal(Line(new[] { 0d, 0, 0 }, new[] { 100d, 0, 0 }).Key, Line(new[] { 100d, 0, 0 }, new[] { 0d, 0, 0 }).Key);

    [Fact]
    public void KeyIgnoresSubMicronNoise()
        => Assert.Equal(Line(new[] { 0d, 0, 0 }, new[] { 100d, 0, 0 }).Key, Line(new[] { 0.0000001, 0, 0 }, new[] { 100d, 0, 0 }).Key);

    [Fact]
    public void ClosedEdgesWithSameSeamDifferByMidpoint()
    {
        // Two full circles sharing a seam point (e.g. concentric holes): start == end, different midpoint/length.
        var small = new EdgeGeometry("circle", new[] { 10d, 0, 0 }, new[] { 10d, 0, 0 }, new[] { 0d, 0, 0 }, 31.416, 5);
        var big = new EdgeGeometry("circle", new[] { 10d, 0, 0 }, new[] { 10d, 0, 0 }, new[] { -10d, 0, 0 }, 62.832, 10);
        Assert.NotEqual(small.Key, big.Key);
    }

    // Box 100 x 50 x 10 mm, top face edges.
    private static readonly List<EdgeGeometry> TopEdges = new()
    {
        Line(new[] { -50d, -25, 10 }, new[] { 50d, -25, 10 }),
        Line(new[] { 50d, -25, 10 }, new[] { 50d, 25, 10 }),
        Line(new[] { 50d, 25, 10 }, new[] { -50d, 25, 10 }),
        Line(new[] { -50d, 25, 10 }, new[] { -50d, -25, 10 }),
    };

    private static double MidDistance(EdgeGeometry e, double[] p) => EdgeGeometry.Distance(e.MidMm, p);

    [Fact]
    public void NearestPicksEdgeClosestToEachPoint()
    {
        var picked = EdgeSelector.Nearest(TopEdges, new[] { new[] { 0d, 25, 10 }, new[] { 50d, 0, 10.2 } }, 0.5, MidDistance);
        Assert.Equal(new[] { TopEdges[2].Key, TopEdges[1].Key }, picked.Select(e => e.Key));
    }

    [Fact]
    public void NearestDeduplicatesRepeatedPicks()
        => Assert.Single(EdgeSelector.Nearest(TopEdges, new[] { new[] { 0d, 25, 10 }, new[] { 0.1, 25, 10 } }, 0.5, MidDistance));

    [Fact]
    public void NearestRejectsPointOutsideTolerance()
    {
        var ex = Assert.Throws<McpException>(() => EdgeSelector.Nearest(TopEdges, new[] { new[] { 0d, 0, 10 } }, 0.5, MidDistance));
        Assert.Contains("25 mm away", ex.Message);
    }

    [Fact]
    public void NearestRejectsMalformedPoint()
        => Assert.Throws<McpException>(() => EdgeSelector.Nearest(TopEdges, new[] { new[] { 0d, 25 } }, 0.5, MidDistance));

    [Theory]
    [InlineData(null, "any")]
    [InlineData("Linear", "line")]
    [InlineData("arc", "circle")]
    [InlineData("spline", "other")]
    public void NormalizesKind(string? input, string expected) => Assert.Equal(expected, EdgeSelector.NormalizeKind(input));

    [Fact]
    public void UnknownKindThrows() => Assert.Throws<McpException>(() => EdgeSelector.NormalizeKind("helix"));
}
