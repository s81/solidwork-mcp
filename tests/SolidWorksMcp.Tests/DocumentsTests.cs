using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;
using SolidWorksMcp.Tools;

namespace SolidWorksMcp.Tests;

public class DocumentsTests
{
    [Theory]
    [InlineData(@"C:\a\Bracket.SLDPRT", swDocumentTypes_e.swDocPART)]
    [InlineData("frame.sldasm", swDocumentTypes_e.swDocASSEMBLY)]
    [InlineData("frame.SLDDRW", swDocumentTypes_e.swDocDRAWING)]
    [InlineData("frame.step", swDocumentTypes_e.swDocNONE)]
    public void TypeFromExtension(string path, swDocumentTypes_e expected) => Assert.Equal(expected, Documents.TypeFromExtension(path));

    [Theory]
    [InlineData((int)swDocumentTypes_e.swDocPART, "part")]
    [InlineData((int)swDocumentTypes_e.swDocASSEMBLY, "assembly")]
    [InlineData((int)swDocumentTypes_e.swDocDRAWING, "drawing")]
    [InlineData(99, "unknown")]
    public void TypeName(int type, string expected) => Assert.Equal(expected, Documents.TypeName(type));

    [Theory]
    [InlineData(@"D:\Vault\Bracket.SLDPRT", @"D:\Vault\Bracket.SLDPRT", "Bracket.SLDPRT", true)]
    [InlineData("bracket", @"D:\Vault\Bracket.SLDPRT", "Bracket.SLDPRT", true)]
    [InlineData("Bracket.sldprt", "", "Bracket.SLDPRT", true)]
    [InlineData("Part1", "", "Part1", true)]
    [InlineData("Drawing1", "", "Drawing1 - Sheet1", true)]
    [InlineData("Bracket2", @"D:\Vault\Bracket.SLDPRT", "Bracket.SLDPRT", false)]
    public void Matches(string wanted, string path, string title, bool expected) => Assert.Equal(expected, Documents.Matches(wanted, path, title));

    [Theory]
    [InlineData("A4", swDwgPaperSizes_e.swDwgPaperA4size)]
    [InlineData("a3", swDwgPaperSizes_e.swDwgPaperA3size)]
    [InlineData(null, swDwgPaperSizes_e.swDwgPaperA3size)]
    [InlineData("E", swDwgPaperSizes_e.swDwgPaperEsize)]
    public void PaperSize(string? input, swDwgPaperSizes_e expected) => Assert.Equal(expected, DocumentTools.PaperSize(input));

    [Fact]
    public void UnknownPaperSizeThrows() => Assert.Throws<ModelContextProtocol.McpException>(() => DocumentTools.PaperSize("A9"));

    [Theory]
    [InlineData("blind", swEndConditions_e.swEndCondBlind)]
    [InlineData("MidPlane", swEndConditions_e.swEndCondMidPlane)]
    [InlineData("through_all", swEndConditions_e.swEndCondThroughAll)]
    public void EndConditions(string input, swEndConditions_e expected) => Assert.Equal(expected, ModelingTools.EndCondition(input, false));

    [Fact]
    public void ThroughAllBothOnlyForCuts()
    {
        Assert.Equal(swEndConditions_e.swEndCondThroughAllBoth, ModelingTools.EndCondition("through_all_both", true));
        Assert.Throws<ModelContextProtocol.McpException>(() => ModelingTools.EndCondition("through_all_both", false));
    }

    [Fact]
    public void ExportFormatValidation()
    {
        Exporter.ValidateFormat("a.step", "part");
        Exporter.ValidateFormat("a.PDF", "drawing");
        var dxfPart = Assert.Throws<ModelContextProtocol.McpException>(() => Exporter.ValidateFormat("a.dxf", "part"));
        Assert.Contains("sw_export_flat_pattern_dxf", dxfPart.Message);
        Assert.Throws<ModelContextProtocol.McpException>(() => Exporter.ValidateFormat("a.docx", "part"));
    }
}
