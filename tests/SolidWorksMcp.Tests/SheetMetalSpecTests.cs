using ModelContextProtocol;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class SheetMetalSpecTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0.5)]
    [InlineData(3.0)]
    [InlineData(50.0)]
    public void AcceptsValidThickness(double? t) => SheetMetalSpec.ValidateThickness(t);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(51.0)]
    public void RejectsBadThickness(double t) => Assert.Throws<McpException>(() => SheetMetalSpec.ValidateThickness(t));

    [Fact]
    public void ZeroBendRadiusIsAllowed() => SheetMetalSpec.ValidateBendRadius(0);

    [Fact]
    public void NegativeBendRadiusRejected() => Assert.Throws<McpException>(() => SheetMetalSpec.ValidateBendRadius(-0.1));

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.2)]
    public void KFactorMustBeStrictlyBetweenZeroAndOne(double k)
        => Assert.Contains("kFactor", Assert.Throws<McpException>(() => SheetMetalSpec.ValidateKFactor(k)).Message);

    [Fact]
    public void TypicalKFactorAccepted() => SheetMetalSpec.ValidateKFactor(0.44);

    [Theory]
    [InlineData(null, swFlangePositionTypes_e.swFlangePositionTypeMaterialInside)]
    [InlineData("material-outside", swFlangePositionTypes_e.swFlangePositionTypeMaterialOutside)]
    [InlineData("Bend_Outside", swFlangePositionTypes_e.swFlangePositionTypeBendOutside)]
    [InlineData("bend_centerline", swFlangePositionTypes_e.swFlangePositionTypeBendCenterLine)]
    [InlineData("bend_tangent", swFlangePositionTypes_e.swFlangePositionTypeBendTangent)]
    public void ParsesPosition(string? input, swFlangePositionTypes_e expected) => Assert.Equal(expected, SheetMetalSpec.ParsePosition(input));

    [Fact]
    public void UnknownPositionThrows() => Assert.Throws<McpException>(() => SheetMetalSpec.ParsePosition("middle"));

    [Theory]
    [InlineData(null, swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp)]
    [InlineData("inner", swFlangeDimTypes_e.swFlangeDimTypeInnerVirtualSharp)]
    [InlineData("tangent", swFlangeDimTypes_e.swFlangeDimTypeBendTangentArc)]
    public void ParsesLengthReference(string? input, swFlangeDimTypes_e expected) => Assert.Equal(expected, SheetMetalSpec.ParseLengthReference(input));

    [Theory]
    [InlineData(0, 90, "lengthMm")]
    [InlineData(20, 0, "angleDegrees")]
    [InlineData(20, 181, "angleDegrees")]
    public void EdgeFlangeValidation(double length, double angle, string expected)
        => Assert.Contains(expected, Assert.Throws<McpException>(() => SheetMetalSpec.ValidateEdgeFlange(length, angle)).Message);

    [Fact]
    public void EdgeFlangeAcceptsHem() => SheetMetalSpec.ValidateEdgeFlange(10, 180);

    [Theory]
    [InlineData((int)swBendAllowanceTypes_e.swBendAllowanceKFactor, "k_factor")]
    [InlineData((int)swBendAllowanceTypes_e.swBendAllowanceBendTable, "bend_table")]
    [InlineData((int)swBendAllowanceTypes_e.swBendAllowanceDeduction, "bend_deduction")]
    [InlineData(42, "42")]
    public void NamesBendAllowance(int type, string expected) => Assert.Equal(expected, SheetMetalSpec.BendAllowanceName(type));

    [Theory]
    [InlineData("250", 250)]
    [InlineData("250.75", 250.75)]
    [InlineData("1,250.5", 1250.5)]
    [InlineData(" 3mm", 3)]
    [InlineData("12 kg", 12)]
    [InlineData("-4.5", -4.5)]
    public void ParsesCutListNumbers(string input, double expected) => Assert.Equal(expected, SheetMetalSpec.ParseCutListNumber(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Yes")]
    [InlineData("\"SW-Mass@@Default@Part1.SLDPRT\"")]
    [InlineData("250 pcs")]
    public void NonNumericCutListValuesAreNull(string? input) => Assert.Null(SheetMetalSpec.ParseCutListNumber(input));
}
