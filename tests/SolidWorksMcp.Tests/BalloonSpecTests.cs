using ModelContextProtocol;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class BalloonSpecTests
{
    [Theory]
    [InlineData(null, swBalloonLayoutType_e.swDetailingBalloonLayout_Square)]
    [InlineData("Circle", swBalloonLayoutType_e.swDetailingBalloonLayout_Circle)]
    [InlineData("left", swBalloonLayoutType_e.swDetailingBalloonLayout_Left)]
    public void ParsesLayout(string? input, swBalloonLayoutType_e expected) => Assert.Equal(expected, BalloonSpec.ParseLayout(input));

    [Fact]
    public void UnknownLayoutThrows() => Assert.Throws<McpException>(() => BalloonSpec.ParseLayout("spiral"));

    [Theory]
    [InlineData(null, swBalloonStyle_e.swBS_Circular)]
    [InlineData("Split_Circle", swBalloonStyle_e.swBS_SplitCirc)]
    [InlineData("hexagon", swBalloonStyle_e.swBS_Hexagon)]
    public void ParsesStyle(string? input, swBalloonStyle_e expected) => Assert.Equal(expected, BalloonSpec.ParseStyle(input));

    [Fact]
    public void UnknownStyleListsOptions() => Assert.Contains("split_circle", Assert.Throws<McpException>(() => BalloonSpec.ParseStyle("star")).Message);

    [Fact]
    public void StyleNamesRoundTrip()
    {
        Assert.Equal("box", BalloonSpec.StyleName((int)swBalloonStyle_e.swBS_Box));
        Assert.Equal("17", BalloonSpec.StyleName((int)swBalloonStyle_e.swBS_FixedSym)); // not offered -> number
    }

    [Theory]
    [InlineData(null, swBalloonFit_e.swBF_2Chars)]
    [InlineData("tightest", swBalloonFit_e.swBF_Tightest)]
    [InlineData("1", swBalloonFit_e.swBF_1Char)]
    [InlineData("5 chars", swBalloonFit_e.swBF_5Chars)]
    public void ParsesSize(string? input, swBalloonFit_e expected) => Assert.Equal(expected, BalloonSpec.ParseSize(input));

    [Fact]
    public void UnknownSizeThrows() => Assert.Throws<McpException>(() => BalloonSpec.ParseSize("6_chars"));

    [Fact]
    public void DefaultTextIsItemNumber() => Assert.Equal((swBalloonTextContent_e.swBalloonTextItemNumber, ""), BalloonSpec.ParseText(null, null, "upperText"));

    [Fact]
    public void CustomPropertyKeepsName()
        => Assert.Equal((swBalloonTextContent_e.swBalloonTextCustomProperties, "PartNo"), BalloonSpec.ParseText("custom_property", "PartNo", "upperText"));

    [Theory]
    [InlineData("custom", null)]
    [InlineData("custom_property", " ")]
    public void CustomTextsNeedAValue(string content, string? value)
        => Assert.Contains("needs the text", Assert.Throws<McpException>(() => BalloonSpec.ParseText(content, value, "upperText")).Message);

    [Fact]
    public void UnknownTextNamesTheArgument()
        => Assert.StartsWith("lowerText", Assert.Throws<McpException>(() => BalloonSpec.ParseText("weight", null, "lowerText")).Message);

    [Fact]
    public void SplitStyles()
    {
        Assert.True(BalloonSpec.IsSplit(swBalloonStyle_e.swBS_SplitCirc));
        Assert.False(BalloonSpec.IsSplit(swBalloonStyle_e.swBS_Circular));
    }

    [Theory]
    [InlineData(null, swStackedBalloonDirection_e.swStackedBalloonDir_Right)]
    [InlineData("Up", swStackedBalloonDirection_e.swStackedBalloonDir_Up)]
    [InlineData("left", swStackedBalloonDirection_e.swStackedBalloonDir_Left)]
    public void ParsesStackDirection(string? input, swStackedBalloonDirection_e expected) => Assert.Equal(expected, BalloonSpec.ParseStackDirection(input));

    [Fact]
    public void UnknownStackDirectionThrows() => Assert.Throws<McpException>(() => BalloonSpec.ParseStackDirection("diagonal"));

    [Theory]
    [InlineData(null, swBalloonItemNumbersOrder_e.swBalloonItemNumbers_DoNotChangeItemNumbers)]
    [InlineData("assembly_order", swBalloonItemNumbersOrder_e.swBalloonItemNumbers_FollowAssemblyOrder)]
    [InlineData("Sequential", swBalloonItemNumbersOrder_e.swBalloonItemNumbers_OrderSequentially)]
    public void ParsesItemOrder(string? input, swBalloonItemNumbersOrder_e expected) => Assert.Equal(expected, BalloonSpec.ParseItemOrder(input));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void RejectsBadItemNumbering(int start, int increment) => Assert.Throws<McpException>(() => BalloonSpec.RequireItemNumbering(start, increment));

    [Fact]
    public void SplitSquareIsSplit() => Assert.True(BalloonSpec.IsSplit(BalloonSpec.ParseStyle("split_square")));

    private static readonly string[] Texts = { "1", "2", "3", "2" };

    [Fact]
    public void PicksByItemNumberIncludingRepeats() => Assert.Equal(new[] { 1, 3 }, BalloonSpec.Pick(Texts, new[] { "2" }, null, false));

    [Fact]
    public void PicksByIndexWithoutDuplicates() => Assert.Equal(new[] { 0, 2 }, BalloonSpec.Pick(Texts, null, new[] { 0, 2, 0 }, false));

    [Fact]
    public void PicksAll() => Assert.Equal(new[] { 0, 1, 2, 3 }, BalloonSpec.Pick(Texts, null, null, true));

    [Fact]
    public void MissingItemNumberListsBalloons() => Assert.Contains("1, 2, 3", Assert.Throws<McpException>(() => BalloonSpec.Pick(Texts, new[] { "9" }, null, false)).Message);

    [Fact]
    public void IndexOutOfRangeThrows() => Assert.Contains("0..3", Assert.Throws<McpException>(() => BalloonSpec.Pick(Texts, null, new[] { 4 }, false)).Message);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void PickNeedsExactlyOneMode(bool items, bool idx, bool all)
        => Assert.Contains("exactly one", Assert.Throws<McpException>(() => BalloonSpec.Pick(Texts, items ? new[] { "1" } : null, idx ? new[] { 0 } : null, all)).Message);

    [Fact]
    public void LinePositionsSpreadFromEndToEnd()
    {
        Assert.Empty(BalloonSpec.LinePositions(0));
        Assert.Equal(new[] { 0.5 }, BalloonSpec.LinePositions(1));
        Assert.Equal(new[] { 0.0, 0.5, 1.0 }, BalloonSpec.LinePositions(3));
    }
}
