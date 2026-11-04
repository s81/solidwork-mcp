using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class ExportTools(SolidWorksSession sw, PathGuard paths)
{
    [McpServerTool(Name = "sw_export", Title = "Export document", Destructive = true, OpenWorld = false)]
    [Description("Export an open document to STEP, IGES, Parasolid, STL, 3MF, PDF, DXF/DWG (drawings), eDrawings or image. Format is chosen by the output file extension.")]
    public Task<string> Export(
        [Description("Output file path; extension selects the format, e.g. C:\\Exports\\bracket.step")] string outputPath,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("STEP application protocol: 203, 214 or 242. Omit to use the SolidWorks setting.")] int? stepAp = null,
        [Description("Drawings to PDF: export all sheets (true) or only the active sheet (false).")] bool allSheets = true,
        [Description("Overwrite the file if it exists.")] bool overwrite = true,
        CancellationToken ct = default)
    {
        string full = paths.Check(outputPath, argumentName: nameof(outputPath));
        if (File.Exists(full) && !overwrite)
        {
            throw new McpException($"'{full}' exists and overwrite=false.");
        }
        return sw.RunOnDocumentAsync(document, (app, doc) =>
            $"Exported {doc.GetTitle()} to {Exporter.Export(app, doc, full, stepAp, allSheets)}", ct);
    }

    [McpServerTool(Name = "sw_export_flat_pattern_dxf", Title = "Export sheet-metal flat pattern", Destructive = true, OpenWorld = false)]
    [Description("Export the flat pattern of a sheet-metal part to DXF/DWG for laser/plasma/punch cutting.")]
    public Task<string> ExportFlatPatternDxf(
        [Description("Output .dxf or .dwg path.")] string outputPath,
        [Description("Path, file name or title of a sheet-metal part. Omit for the active document.")] string? document = null,
        [Description("Include bend lines.")] bool includeBendLines = true,
        [Description("Include sketches.")] bool includeSketches = false,
        [Description("Merge coplanar faces.")] bool mergeCoplanarFaces = true,
        CancellationToken ct = default)
    {
        string full = paths.Check(outputPath, argumentName: nameof(outputPath));
        string ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext is not (".dxf" or ".dwg"))
        {
            throw new McpException("outputPath must end in .dxf or .dwg.");
        }
        return sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Exporter.ExportFlatPattern(doc, full, includeBendLines, includeSketches, mergeCoplanarFaces);
            return $"Exported flat pattern of {doc.GetTitle()} to {full}";
        }, ct);
    }

    public sealed record BatchItem(string Source, string? Output, bool Success, string? Error);

    [McpServerTool(Name = "sw_batch_export", Title = "Batch export folder", Destructive = true, OpenWorld = false)]
    [Description("Open every SolidWorks file in a folder (read-only, hidden), export each to a format, and close it again. Typical use: STEP for suppliers, PDF of all drawings, flat-pattern DXF of sheet-metal parts.")]
    public Task<IReadOnlyList<BatchItem>> BatchExport(
        [Description("Folder containing SolidWorks files.")] string sourceFolder,
        [Description("Output folder (created if missing).")] string outputFolder,
        [Description("Target extension, e.g. step, pdf, stl, dxf. 'flat-dxf' exports sheet-metal flat patterns.")] string format,
        [Description("File pattern, e.g. *.sldprt, *.slddrw, *.sld*.")] string pattern = "*.sldprt",
        [Description("Include sub-folders.")] bool recursive = false,
        [Description("Safety limit on the number of files.")] int maxFiles = 200,
        [Description("STEP application protocol (203/214/242).")] int? stepAp = null,
        CancellationToken ct = default)
    {
        string src = paths.Check(sourceFolder, mustExist: true, nameof(sourceFolder));
        string dst = paths.Check(outputFolder, argumentName: nameof(outputFolder));
        bool flat = format.Equals("flat-dxf", StringComparison.OrdinalIgnoreCase);
        string ext = flat ? ".dxf" : "." + format.TrimStart('.').ToLowerInvariant();
        if (!flat && !Exporter.Formats.ContainsKey(ext))
        {
            throw new McpException($"Unsupported format '{format}'.");
        }

        var files = Directory.EnumerateFiles(src, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => Documents.TypeFromExtension(f) != swDocumentTypes_e.swDocNONE && !Path.GetFileName(f).StartsWith("~$"))
            .Take(maxFiles + 1)
            .ToList();
        if (files.Count > maxFiles)
        {
            throw new McpException($"More than {maxFiles} files match. Narrow the pattern or raise maxFiles.");
        }

        return sw.RunAsync<IReadOnlyList<BatchItem>>(app =>
        {
            var results = new List<BatchItem>();
            foreach (string file in files)
            {
                if (ct.IsCancellationRequested)
                {
                    results.Add(new BatchItem(file, null, false, "cancelled"));
                    continue;
                }
                string relative = Path.GetRelativePath(src, file);
                string output = Path.Combine(dst, Path.ChangeExtension(relative, ext));
                ModelDoc2? doc = null;
                bool openedNow = false;
                try
                {
                    (doc, openedNow) = Documents.OpenSilently(app, file, readOnly: true, visible: false);
                    if (flat)
                    {
                        Exporter.ExportFlatPattern(doc, output);
                    }
                    else
                    {
                        Exporter.Export(app, doc, output, stepAp);
                    }
                    results.Add(new BatchItem(file, output, true, null));
                }
                catch (Exception ex) when (ex is McpException or System.Runtime.InteropServices.COMException)
                {
                    results.Add(new BatchItem(file, null, false, ex.Message));
                }
                finally
                {
                    if (doc is not null && openedNow)
                    {
                        Documents.Close(app, doc);
                    }
                }
            }
            return results;
        }, CancellationToken.None);
    }
}
