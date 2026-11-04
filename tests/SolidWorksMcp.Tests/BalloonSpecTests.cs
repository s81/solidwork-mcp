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
}
