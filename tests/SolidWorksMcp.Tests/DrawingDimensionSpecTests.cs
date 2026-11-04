using ModelContextProtocol;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class DrawingDimensionSpecTests
{
    [Fact]
    public void DefaultItemTypeIsMarkedDimensions()
        => Assert.Equal((int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing, DrawingDimensionSpec.ParseItemTypes(null));

    [Fact]
    public void ItemTypesCombineAsBitMask()
    {
        int mask = DrawingDimensionSpec.ParseItemTypes(new[] { "marked_dimensions", "Hole_Callouts", "hole_wizard_locations", "marked_dimensions" });
        Assert.Equal((int)(swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing | swInsertAnnotation_e.swInsertholeCallout | swInsertAnnotation_e.swInsertHoleWizardLocationDimensions), mask);
    }

    [Fact]
    public void UnknownItemTypeListsOptions()
        => Assert.Contains("hole_callouts", Assert.Throws<McpException>(() => DrawingDimensionSpec.ParseItemTypes(new[] { "balloons" })).Message);

    [Fact]
    public void EveryItemTypeIsASingleBit()
        => Assert.All(DrawingDimensionSpec.ItemTypes.Values, v => Assert.Equal(0, (int)v & ((int)v - 1)));

    [Theory]
    [InlineData(null, swAutodimScheme_e.swAutodimSchemeBaseline)]
    [InlineData("Ordinate", swAutodimScheme_e.swAutodimSchemeOrdinate)]
    [InlineData("chain", swAutodimScheme_e.swAutodimSchemeChain)]
    [InlineData("centerline", swAutodimScheme_e.swAutodimSchemeCenterline)]
    public void ParsesScheme(string? input, swAutodimScheme_e expected) => Assert.Equal(expected, DrawingDimensionSpec.ParseScheme(input, "x"));

    [Fact]
    public void SchemeErrorNamesTheArgument()
        => Assert.StartsWith("verticalScheme", Assert.Throws<McpException>(() => DrawingDimensionSpec.ParseScheme("zigzag", "verticalScheme")).Message);

    [Fact]
    public void ParsesPlacements()
    {
        Assert.Equal(swAutodimHorizontalPlacement_e.swAutodimHorizontalPlacementBelow, DrawingDimensionSpec.ParseHorizontalPlacement(null));
        Assert.Equal(swAutodimHorizontalPlacement_e.swAutodimHorizontalPlacementAbove, DrawingDimensionSpec.ParseHorizontalPlacement("ABOVE"));
        Assert.Equal(swAutodimVerticalPlacement_e.swAutodimVerticalPlacementLeft, DrawingDimensionSpec.ParseVerticalPlacement(null));
        Assert.Equal(swAutodimVerticalPlacement_e.swAutodimVerticalPlacementRight, DrawingDimensionSpec.ParseVerticalPlacement("right"));
        Assert.Throws<McpException>(() => DrawingDimensionSpec.ParseHorizontalPlacement("left"));
        Assert.Throws<McpException>(() => DrawingDimensionSpec.ParseVerticalPlacement("above"));
    }

    [Theory]
    [InlineData(null, DrawingDimensionSpec.Orientation.Smart)]
    [InlineData("aligned", DrawingDimensionSpec.Orientation.Smart)]
    [InlineData("Horizontal", DrawingDimensionSpec.Orientation.Horizontal)]
    [InlineData("vertical", DrawingDimensionSpec.Orientation.Vertical)]
    public void ParsesOrientation(string? input, DrawingDimensionSpec.Orientation expected) => Assert.Equal(expected, DrawingDimensionSpec.ParseOrientation(input));

    [Theory]
    [InlineData(null, "EDGE")]
    [InlineData("vertex", "VERTEX")]
    [InlineData("sketch_segment", "SKETCHSEGMENT")]
    [InlineData("sketchpoint", "SKETCHPOINT")]
    public void PickTypes(string? input, string expected) => Assert.Equal(expected, DrawingDimensionSpec.PickType(input));

    [Fact]
    public void FacePickIsRejected() => Assert.Throws<McpException>(() => DrawingDimensionSpec.PickType("face"));

    [Fact]
    public void SheetPointValidation()
    {
        DrawingDimensionSpec.RequireSheetPoint(new[] { 120.5, 80 }, "p");
        Assert.Contains("[x, y]", Assert.Throws<McpException>(() => DrawingDimensionSpec.RequireSheetPoint(new[] { 1d, 2, 3 }, "p")).Message);
        Assert.Throws<McpException>(() => DrawingDimensionSpec.RequireSheetPoint(null, "p"));
        Assert.Contains("plausible", Assert.Throws<McpException>(() => DrawingDimensionSpec.RequireSheetPoint(new[] { 1e6, 0 }, "p")).Message);
        Assert.Throws<McpException>(() => DrawingDimensionSpec.RequireSheetPoint(new[] { double.NaN, 0 }, "p"));
    }

    [Theory]
    [InlineData((int)swDimensionType_e.swLinearDimension, "linear")]
    [InlineData((int)swDimensionType_e.swDiameterDimension, "diameter")]
    [InlineData((int)swDimensionType_e.swRadialDimension, "radial")]
    [InlineData((int)swDimensionType_e.swAngularDimension, "angular")]
    [InlineData((int)swDimensionType_e.swDimensionTypeUnknown, "unknown")]
    [InlineData(999, "unknown")]
    public void NamesDimensionTypes(int type, string expected) => Assert.Equal(expected, DrawingDimensionSpec.DimensionTypeName(type));

    [Theory]
    [InlineData((int)swTolType_e.swTolNONE, "none")]
    [InlineData((int)swTolType_e.swTolBILAT, "bilat")]
    [InlineData((int)swTolType_e.swTolSYMMETRIC, "symmetric")]
    public void NamesTolerances(int type, string expected) => Assert.Equal(expected, DrawingDimensionSpec.ToleranceName(type));

    [Fact]
    public void NamesDrivenState()
    {
        Assert.Equal("driving", DrawingDimensionSpec.DrivenStateName((int)swDimensionDrivenState_e.swDimensionDriving));
        Assert.Equal("driven", DrawingDimensionSpec.DrivenStateName((int)swDimensionDrivenState_e.swDimensionDriven));
        Assert.Equal("unknown", DrawingDimensionSpec.DrivenStateName(0));
    }

    // Sheet with a front view, a right view and a detail view drawn on top of the front view.
    private static readonly double[][] Outlines =
    {
        new[] { 20d, 20, 180, 140 },   // front
        new[] { 200d, 20, 300, 140 },  // right
        new[] { 60d, 60, 100, 100 },   // detail over the front view
    };

    [Theory]
    [InlineData(30, 30, 0)]
    [InlineData(250, 100, 1)]
    [InlineData(80, 80, 2)]   // inside both front and detail: the smaller (detail) wins
    [InlineData(190, 80, -1)] // gap between views
    [InlineData(20, 20, 0)]   // on the corner counts as inside
    public void FindsViewContainingPoint(double x, double y, int expected)
        => Assert.Equal(expected, DrawingDimensionSpec.ViewAt(Outlines, new[] { x, y }));

    [Fact]
    public void MalformedOutlinesAreSkipped()
        => Assert.Equal(1, DrawingDimensionSpec.ViewAt(new[] { new[] { 0d, 0 }, new[] { 0d, 0, 10, 10 } }, new[] { 5d, 5 }));
}
