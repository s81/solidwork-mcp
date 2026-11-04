using ModelContextProtocol;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class SectionDetailSpecTests
{
    private static readonly double[] Outline = { 100, 50, 200, 110 }; // 100 wide, 60 high, centre (150, 80)

    [Theory]
    [InlineData("horizontal", null, 0)]
    [InlineData("H", null, 0)]
    [InlineData("vertical", null, 90)]
    [InlineData("angled", 30.0, 30)]
    [InlineData("angled", -30.0, 150)]
    [InlineData("angled", 210.0, 30)]
    public void CutAngleNormalisesToHalfTurn(string orientation, double? angle, double expected)
        => Assert.Equal(expected, SectionDetailSpec.CutAngle(orientation, angle), 9);

    [Theory]
    [InlineData("angled", null, "requires angleDeg")]
    [InlineData("horizontal", 10.0, "only used with orientation 'angled'")]
    [InlineData("diagonal", null, "Unknown orientation")]
    public void CutAngleRejectsBadInput(string orientation, double? angle, string expected)
        => Assert.Contains(expected, Assert.Throws<McpException>(() => SectionDetailSpec.CutAngle(orientation, angle)).Message);

    [Fact]
    public void HorizontalCutSpansTheViewPlusMargin()
    {
        var (s, e) = SectionDetailSpec.CutLine(Outline, new[] { 150.0, 70 }, 0, 5);
        Assert.Equal(new[] { 95.0, 70 }, s);
        Assert.Equal(new[] { 205.0, 70 }, e);
    }

    [Fact]
    public void VerticalCutSpansTheViewPlusMargin()
    {
        var (s, e) = SectionDetailSpec.CutLine(Outline, new[] { 120.0, 80 }, 90, 5);
        Assert.Equal(new[] { 120.0, 45 }, s);
        Assert.Equal(new[] { 120.0, 115 }, e);
    }

    [Fact]
    public void AngledCutIsClippedToTheGrownOutline()
    {
        var (s, e) = SectionDetailSpec.CutLine(Outline, new[] { 150.0, 80 }, 45, 0);
        // From the centre at 45°, the 60 mm height limits first: ±30 mm in y.
        Assert.Equal(new[] { 120.0, 50 }, s);
        Assert.Equal(new[] { 180.0, 110 }, e);
    }

    [Fact]
    public void CutThroughAPointOutsideTheViewIsRejected()
        => Assert.Contains("outside the parent view", Assert.Throws<McpException>(() => SectionDetailSpec.CutLine(Outline, new[] { 90.0, 80 }, 0)).Message);

    [Fact]
    public void HorizontalSectionGoesBelowAndVerticalGoesRight()
    {
        Assert.Equal(new[] { 150.0, 0 }, SectionDetailSpec.SectionPlacement(Outline, 0, 20));   // 80 - (60 + 20)
        Assert.Equal(new[] { 270.0, 80 }, SectionDetailSpec.SectionPlacement(Outline, 90, 20)); // 150 + (100 + 20)
    }

    [Fact]
    public void SectionOptionsBuildTheApiBitMask()
    {
        Assert.Equal(0, SectionDetailSpec.SectionOptions(true, false, false, false, false, false));
        int all = SectionDetailSpec.SectionOptions(false, true, true, true, true, true);
        Assert.Equal(1 | 4 | 8 | 16 | 32 | 64, all);
        Assert.Equal((int)swCreateSectionViewAtOptions_e.swCreateSectionView_Partial, SectionDetailSpec.SectionOptions(true, false, false, true, false, false));
    }

    [Theory]
    [InlineData("2:1", 2, 1)]
    [InlineData("1:5", 1, 5)]
    [InlineData("3/2", 3, 2)]
    [InlineData(" 4 ", 4, 1)]
    [InlineData("0.5", 0.5, 1)]
    public void ParsesScales(string text, double num, double den)
        => Assert.Equal((num, den), SectionDetailSpec.ParseScale(text));

    [Theory]
    [InlineData("0:1")]
    [InlineData("2:0")]
    [InlineData("-2")]
    [InlineData("big")]
    [InlineData("1:2:3")]
    public void RejectsBadScales(string text)
        => Assert.Throws<McpException>(() => SectionDetailSpec.ParseScale(text));

    [Theory]
    [InlineData("standard", swDetViewStyle_e.swDetViewSTANDARD)]
    [InlineData("No Leader", swDetViewStyle_e.swDetViewNOLEADER)]
    [InlineData("connected", swDetViewStyle_e.swDetViewCONNECTED)]
    public void ParsesDetailStylesAndNamesThemBack(string text, swDetViewStyle_e expected)
    {
        var style = SectionDetailSpec.ParseDetailStyle(text);
        Assert.Equal(expected, style);
        Assert.Equal(SectionDetailSpec.ParseDetailStyle(SectionDetailSpec.DetailStyleName((int)style)), style);
    }

    [Fact]
    public void ParsesCircleDisplay()
    {
        Assert.Equal(swDetCircleShowType_e.swDetCircleCIRCLE, SectionDetailSpec.ParseCircleDisplay("circle"));
        Assert.Equal(swDetCircleShowType_e.swDetCircleDONTSHOW, SectionDetailSpec.ParseCircleDisplay("hidden"));
        Assert.Throws<McpException>(() => SectionDetailSpec.ParseCircleDisplay("square"));
    }

    [Fact]
    public void DetailRadiusAndCentreAreChecked()
    {
        Assert.Contains("radiusMm must be > 0", Assert.Throws<McpException>(() => SectionDetailSpec.RequireRadius(0, Outline, new[] { 150.0, 80 })).Message);
        Assert.Contains("outside the parent view", Assert.Throws<McpException>(() => SectionDetailSpec.RequireRadius(5, Outline, new[] { 250.0, 80 })).Message);
        SectionDetailSpec.RequireRadius(5, Outline, new[] { 150.0, 80 });
    }

    [Fact]
    public void DetailGoesRightOfTheParentAccountingForMagnification()
        // parent 1:2 (0.5), detail 2:1 (2) → ×4; radius 10 → half-width 40; 200 + 20 + 40.
        => Assert.Equal(new[] { 260.0, 70 }, SectionDetailSpec.DetailPlacement(Outline, new[] { 150.0, 70 }, 10, 0.5, 2, 20));

    [Fact]
    public void RequiresTwoFinitePointCoordinates()
    {
        Assert.Throws<McpException>(() => SectionDetailSpec.RequirePoint(new[] { 1.0 }, "positionMm"));
        Assert.Throws<McpException>(() => SectionDetailSpec.RequirePoint(new[] { 1.0, double.NaN }, "positionMm"));
        Assert.Throws<McpException>(() => SectionDetailSpec.RequireOutline(new[] { 10.0, 10, 5, 20 }, "Drawing View1"));
    }

    [Fact]
    public void NextLabelSkipsUsedAndConfusableLetters()
    {
        Assert.Equal("A", SectionDetailSpec.NextLabel(Array.Empty<string>()));
        Assert.Equal("C", SectionDetailSpec.NextLabel(new[] { "a", "B", null, " " }));
        Assert.Equal("J", SectionDetailSpec.NextLabel(new[] { "A", "B", "C", "D", "E", "F", "G", "H" }));
        var single = "ABCDEFGHJKLMNPRSTUVWXYZ".Select(c => c.ToString()).ToList();
        Assert.Equal("AA", SectionDetailSpec.NextLabel(single));
        Assert.Equal("AB", SectionDetailSpec.NextLabel(single.Append("AA")));
    }
}
