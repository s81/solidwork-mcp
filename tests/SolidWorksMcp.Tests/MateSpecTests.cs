using ModelContextProtocol;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class MateSpecTests
{
    [Theory]
    [InlineData("coincident", swMateType_e.swMateCOINCIDENT)]
    [InlineData("Concentric", swMateType_e.swMateCONCENTRIC)]
    [InlineData("parallel", swMateType_e.swMatePARALLEL)]
    [InlineData("perpendicular", swMateType_e.swMatePERPENDICULAR)]
    [InlineData("tangent", swMateType_e.swMateTANGENT)]
    [InlineData("distance", swMateType_e.swMateDISTANCE)]
    [InlineData("angle", swMateType_e.swMateANGLE)]
    [InlineData(" LOCK ", swMateType_e.swMateLOCK)]
    public void ParsesTypes(string input, swMateType_e expected) => Assert.Equal(expected, MateSpec.ParseType(input));

    [Fact]
    public void UnsupportedTypeThrows() => Assert.Throws<McpException>(() => MateSpec.ParseType("gear"));

    [Theory]
    [InlineData(null, swMateAlign_e.swMateAlignCLOSEST)]
    [InlineData("aligned", swMateAlign_e.swMateAlignALIGNED)]
    [InlineData("anti-aligned", swMateAlign_e.swMateAlignANTI_ALIGNED)]
    [InlineData("Anti_Aligned", swMateAlign_e.swMateAlignANTI_ALIGNED)]
    public void ParsesAlignment(string? input, swMateAlign_e expected) => Assert.Equal(expected, MateSpec.ParseAlignment(input));

    [Fact]
    public void NamesRoundTrip()
    {
        Assert.Equal("coincident", MateSpec.TypeName((int)swMateType_e.swMateCOINCIDENT));
        Assert.Equal("anti_aligned", MateSpec.AlignmentName((int)swMateAlign_e.swMateAlignANTI_ALIGNED));
    }

    [Fact]
    public void FeatureReference() => Assert.Equal(MateTargetKind.Feature,
        MateSpec.Classify(new MateTarget { Component = "Leg-1", Feature = "top" }, 0, swMateType_e.swMateCOINCIDENT));

    [Fact]
    public void AssemblyPlaneReference() => Assert.Equal(MateTargetKind.Feature,
        MateSpec.Classify(new MateTarget { Feature = "front" }, 0, swMateType_e.swMateCOINCIDENT));

    [Fact]
    public void EntityReference() => Assert.Equal(MateTargetKind.Entity,
        MateSpec.Classify(new MateTarget { Component = "Top-1", Entity = "Face", Point = new[] { 0d, 0, 740 } }, 1, swMateType_e.swMateDISTANCE));

    [Fact]
    public void WholeComponentOnlyForLock()
    {
        var r = new MateTarget { Component = "Bracket-1" };
        Assert.Equal(MateTargetKind.WholeComponent, MateSpec.Classify(r, 0, swMateType_e.swMateLOCK));
        Assert.Contains("lock mate", Assert.Throws<McpException>(() => MateSpec.Classify(r, 0, swMateType_e.swMateCOINCIDENT)).Message);
    }

    [Theory]
    [InlineData("top", "face", "not both")]
    [InlineData(null, "solid", "unknown entity")]
    [InlineData(null, "face", "needs 'point'")]
    public void RejectsBadReferences(string? feature, string entity, string expected)
    {
        var r = new MateTarget { Component = "A-1", Feature = feature, Entity = entity };
        Assert.Contains(expected, Assert.Throws<McpException>(() => MateSpec.Classify(r, 0, swMateType_e.swMateCOINCIDENT)).Message);
    }

    [Fact]
    public void EmptyReferenceRejected()
        => Assert.Throws<McpException>(() => MateSpec.Classify(new MateTarget(), 0, swMateType_e.swMateLOCK));

    [Fact]
    public void EntityTypeStrings()
    {
        Assert.Equal("FACE", MateSpec.EntityType("face"));
        Assert.Equal("EDGE", MateSpec.EntityType("Edge"));
        Assert.Equal("VERTEX", MateSpec.EntityType("vertex"));
    }

    private static readonly string[] Names = { "Leg-1", "Leg-2", "Frame-1", "Frame-1/Rail-1", "Frame-1/Rail-2", "Drawer-1/Rail-1" };

    [Theory]
    [InlineData("leg-2", 1)]
    [InlineData("Frame-1/Rail-2", 4)]
    [InlineData("Rail-2", 4)]
    [InlineData("drawer-1/rail-1", 5)]
    public void MatchesComponents(string wanted, int expected) => Assert.Equal(expected, MateSpec.MatchComponent(Names, wanted));

    [Fact]
    public void AmbiguousShortNameListsCandidates()
    {
        var ex = Assert.Throws<McpException>(() => MateSpec.MatchComponent(Names, "Rail-1"));
        Assert.Contains("Frame-1/Rail-1", ex.Message);
        Assert.Contains("Drawer-1/Rail-1", ex.Message);
    }

    [Fact]
    public void MissingComponentThrows() => Assert.Throws<McpException>(() => MateSpec.MatchComponent(Names, "Top-1"));
}
