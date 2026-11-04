using ModelContextProtocol;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class BomTableSpecTests
{
    [Theory]
    [InlineData(null, swBomType_e.swBomType_TopLevelOnly)]
    [InlineData("parts-only", swBomType_e.swBomType_PartsOnly)]
    [InlineData("Indented", swBomType_e.swBomType_Indented)]
    public void ParsesType(string? input, swBomType_e expected) => Assert.Equal(expected, BomTableSpec.ParseType(input));

    [Fact]
    public void UnknownTypeThrows() => Assert.Throws<McpException>(() => BomTableSpec.ParseType("flat"));

    [Fact]
    public void TypeNamesRoundTrip()
    {
        foreach (var t in new[] { "top_level", "parts_only", "indented" })
        {
            Assert.Equal(t, BomTableSpec.TypeName((int)BomTableSpec.ParseType(t)));
        }
    }

    [Theory]
    [InlineData(null, true, swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopRight)]
    [InlineData("bottom-left", true, swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_BottomLeft)]
    [InlineData("point", false, swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft)]
    public void ParsesAnchor(string? input, bool useAnchor, swBOMConfigurationAnchorType_e corner)
        => Assert.Equal((useAnchor, corner), BomTableSpec.ParseAnchor(input));

    [Theory]
    [InlineData(null, swNumberingType_e.swNumberingType_Detailed)]
    [InlineData("flat", swNumberingType_e.swNumberingType_Flat)]
    [InlineData("NONE", swNumberingType_e.swNumberingType_None)]
    public void ParsesNumbering(string? input, swNumberingType_e expected) => Assert.Equal(expected, BomTableSpec.ParseIndentedNumbering(input));

    [Fact]
    public void PrefersStandardTemplate()
        => Assert.Equal(@"C:\t\bom-standard.sldbomtbt", BomTableSpec.PickTemplate(new[] { @"C:\t\bom-material.sldbomtbt", @"C:\t\bom-standard.sldbomtbt", @"C:\t\readme.txt" }));

    [Fact]
    public void StandardTemplateMatchIgnoresCase()
        => Assert.Equal(@"C:\t\BOM-Standard.SLDBOMTBT", BomTableSpec.PickTemplate(new[] { @"C:\t\a.sldbomtbt", @"C:\t\BOM-Standard.SLDBOMTBT" }));

    [Fact]
    public void FallsBackToFirstTemplateAlphabetically()
        => Assert.Equal(@"C:\t\a-company.sldbomtbt", BomTableSpec.PickTemplate(new[] { @"C:\t\z-other.sldbomtbt", @"C:\t\a-company.sldbomtbt" }));

    [Fact]
    public void NoTemplateGivesNull() => Assert.Null(BomTableSpec.PickTemplate(new[] { "x.txt" }));

    [Theory]
    [InlineData(null, "bom.csv", ",")]
    [InlineData(null, "bom.TSV", "\t")]
    [InlineData("tab", "bom.dat", "\t")]
    [InlineData("csv", "bom.txt", ",")]
    public void ChoosesSeparator(string? format, string path, string expected) => Assert.Equal(expected, BomTableSpec.ParseSeparator(format, path));

    [Fact]
    public void XlsxIsNotPretendedToBeSupported()
        => Assert.Contains("csv", Assert.Throws<McpException>(() => BomTableSpec.ParseSeparator(null, "bom.xlsx")).Message);

    // A 4-row BOM with the header on top:  ITEM NO. | PART NUMBER | DESCRIPTION | QTY.
    private static readonly string?[][] Grid =
    {
        new string?[] { "ITEM NO.", "PART NUMBER", "DESCRIPTION", "QTY." },
        new string?[] { "1", "LEG-700", "Leg 40x40", "4" },
        new string?[] { "2", "TOP-1200", " Worktop ", "1" },
        new string?[] { "", "", "", "" },
    };

    [Fact]
    public void TopHeaderRowsBecomeRecords()
    {
        var rows = BomTableSpec.ToRows(Grid[0], Grid.Length, (r, c) => Grid[r][c], 1, swTableHeaderPosition_e.swTableHeader_Top);
        Assert.Equal(2, rows.Count); // empty trailing row dropped
        Assert.Equal("LEG-700", rows[0]["PART NUMBER"]);
        Assert.Equal("4", rows[0]["QTY."]);
        Assert.Equal("Worktop", rows[1]["DESCRIPTION"]); // trimmed
    }

    [Fact]
    public void BottomHeaderIsSkippedAtTheEnd()
    {
        string?[][] g = { new string?[] { "2", "B" }, new string?[] { "1", "A" }, new string?[] { "ITEM", "PART" } };
        var rows = BomTableSpec.ToRows(g[2], g.Length, (r, c) => g[r][c], 1, swTableHeaderPosition_e.swTableHeader_Bottom);
        Assert.Equal(new[] { "B", "A" }, rows.Select(r => r["PART"]));
    }

    [Fact]
    public void EmptyAndDuplicateTitlesStayDistinct()
    {
        string?[] titles = { "QTY.", "", "qty.", null };
        string?[][] g = { new string?[] { "1", "2", "3", "4" } };
        var row = BomTableSpec.ToRows(titles, 1, (r, c) => g[r][c], 0, swTableHeaderPosition_e.swTableHeader_None).Single();
        Assert.Equal(new[] { "QTY.", "Column 2", "qty._2", "Column 4" }, row.Keys);
        Assert.Equal("3", row["qty._2"]);
    }

    [Fact]
    public void NullCellsBecomeEmptyStrings()
    {
        var row = BomTableSpec.ToRows(new string?[] { "A", "B" }, 1, (r, c) => c == 0 ? "x" : null, 0, swTableHeaderPosition_e.swTableHeader_None).Single();
        Assert.Equal(string.Empty, row["B"]);
    }
}
