using ModelContextProtocol;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class RevisionSpecTests
{
    private const swRevisionTableTagStyle_e Alpha = swRevisionTableTagStyle_e.swRevisionTable_AlphabeticTags;
    private const swRevisionTableTagStyle_e Numeric = swRevisionTableTagStyle_e.swRevisionTable_NumericTags;

    [Theory]
    [InlineData(null, "A")]
    [InlineData("", "A")]
    [InlineData("A", "B")]
    [InlineData("b", "C")]
    [InlineData("Z", "AA")]
    [InlineData("AZ", "BA")]
    [InlineData("ZZ", "AAA")]
    public void NextAlphabetic(string? current, string expected) => Assert.Equal(expected, RevisionSpec.Next(current, Alpha));

    [Theory]
    [InlineData(null, "1")]
    [InlineData("1", "2")]
    [InlineData("9", "10")]
    public void NextNumeric(string? current, string expected) => Assert.Equal(expected, RevisionSpec.Next(current, Numeric));

    [Theory]
    [InlineData("H", "J")]
    [InlineData("N", "P")]
    [InlineData("Y", "AA")]
    [InlineData(null, "A")]
    public void NextSkipsLetters(string? current, string expected) => Assert.Equal(expected, RevisionSpec.Next(current, Alpha, "IOQSXZ"));

    [Fact]
    public void SkippedFirstLetterStartsAtNextAllowed() => Assert.Equal("B", RevisionSpec.Next(null, Alpha, "a"));

    [Theory]
    [InlineData("A1", Alpha)]
    [InlineData("I", Alpha)]
    [InlineData("B", Numeric)]
    public void NextRejectsRevisionsOutsideTheScheme(string current, swRevisionTableTagStyle_e scheme)
        => Assert.Contains("pass revision explicitly", Assert.Throws<McpException>(() => RevisionSpec.Next(current, scheme, "I")).Message);

    [Theory]
    [InlineData("keep", null)]
    [InlineData("Alphabetic", Alpha)]
    [InlineData("numeric", Numeric)]
    public void ParsesScheme(string input, swRevisionTableTagStyle_e? expected) => Assert.Equal(expected, RevisionSpec.ParseScheme(input));

    [Fact]
    public void UnknownSchemeThrows() => Assert.Throws<McpException>(() => RevisionSpec.ParseScheme("roman"));

    [Theory]
    [InlineData("triangle", swRevisionTableSymbolShape_e.swRevisionTable_TriangleSymbol)]
    [InlineData("Hexagon", swRevisionTableSymbolShape_e.swRevisionTable_HexagonSymbol)]
    public void ParsesSymbolShape(string input, swRevisionTableSymbolShape_e expected) => Assert.Equal(expected, RevisionSpec.ParseSymbolShape(input));

    [Fact]
    public void UnknownSymbolShapeThrows() => Assert.Throws<McpException>(() => RevisionSpec.ParseSymbolShape("star"));

    [Fact]
    public void PicksStandardRevisionBlock()
    {
        var files = new[] { @"C:\t\a-custom.sldrevtbt", @"C:\t\standard revision block.sldrevtbt", @"C:\t\notes.txt" };
        Assert.Equal(@"C:\t\standard revision block.sldrevtbt", RevisionSpec.PickTemplate(files));
        Assert.Equal(@"C:\t\a-custom.sldrevtbt", RevisionSpec.PickTemplate(files.Where(f => !f.Contains("standard"))));
        Assert.Null(RevisionSpec.PickTemplate(new[] { @"C:\t\notes.txt" }));
    }

    private static readonly string[] Titles = { "ZONE", "REV.", "DESCRIPTION", "DATE", "APPROVED", "ECO" };

    [Fact]
    public void MapsFieldsToColumns()
    {
        var writes = RevisionSpec.CellWrites(Titles, "B3", "Leg 720", "2026-11-04", "SA", new Dictionary<string, string> { ["eco"] = "1234" });
        Assert.Equal(new[] { (0, "B3"), (2, "Leg 720"), (3, "2026-11-04"), (4, "SA"), (5, "1234") }, writes);
    }

    [Fact]
    public void OnlyPassedFieldsAreWritten() => Assert.Equal(new[] { (2, "x") }, RevisionSpec.CellWrites(Titles, null, "x", null, null, null));

    [Fact]
    public void AcceptsDescAndApprTitles()
        => Assert.Equal(new[] { (1, "d"), (2, "a") }, RevisionSpec.CellWrites(new[] { "REV", "DESC.", "APPR." }, null, "d", null, "a", null));

    [Fact]
    public void MissingColumnListsColumns()
        => Assert.Contains("REV, DESCRIPTION", Assert.Throws<McpException>(() => RevisionSpec.CellWrites(new[] { "REV", "DESCRIPTION" }, "A1", null, null, null, null)).Message);

    [Fact]
    public void UnknownValueColumnThrows()
        => Assert.Throws<McpException>(() => RevisionSpec.CellWrites(Titles, null, null, null, null, new Dictionary<string, string> { ["MASS"] = "1" }));

    [Fact]
    public void RevColumnCannotBeWrittenDirectly()
        => Assert.Contains("revision argument", Assert.Throws<McpException>(() => RevisionSpec.CellWrites(Titles, null, null, null, null, new Dictionary<string, string> { ["REV."] = "C" })).Message);

    [Fact]
    public void DataRowsSkipHeaders()
    {
        Assert.Equal(new[] { 2, 3, 4 }, RevisionSpec.DataRows(5, 2, swTableHeaderPosition_e.swTableHeader_Top));
        Assert.Equal(new[] { 0, 1, 2 }, RevisionSpec.DataRows(5, 2, swTableHeaderPosition_e.swTableHeader_Bottom));
        Assert.Empty(RevisionSpec.DataRows(2, 2, swTableHeaderPosition_e.swTableHeader_Top));
    }
}
