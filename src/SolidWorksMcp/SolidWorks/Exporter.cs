using ModelContextProtocol;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

/// <summary>Save-as / export helpers. Call only from the STA thread.</summary>
public static class Exporter
{
    /// <summary>Extensions SolidWorks can write, keyed by the document types that support them.</summary>
    public static readonly IReadOnlyDictionary<string, string> Formats = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".step"] = "part,assembly", [".stp"] = "part,assembly",
        [".igs"] = "part,assembly", [".iges"] = "part,assembly",
        [".x_t"] = "part,assembly", [".x_b"] = "part,assembly",
        [".sat"] = "part,assembly", [".stl"] = "part,assembly", [".3mf"] = "part,assembly",
        [".wrl"] = "part,assembly", [".ifc"] = "part,assembly",
        [".eprt"] = "part", [".easm"] = "assembly", [".edrw"] = "drawing",
        [".pdf"] = "drawing,part,assembly",
        [".dxf"] = "drawing", [".dwg"] = "drawing",
        [".jpg"] = "drawing,part,assembly", [".tif"] = "drawing,part,assembly", [".png"] = "drawing,part,assembly",
    };

    public static void ValidateFormat(string path, string docType)
    {
        string ext = Path.GetExtension(path);
        if (!Formats.TryGetValue(ext, out string? types))
        {
            throw new McpException($"Unsupported export extension '{ext}'. Supported: {string.Join(", ", Formats.Keys)}.");
        }
        if (!types.Split(',').Contains(docType))
        {
            throw new McpException($"'{ext}' export is not available for a {docType}. " +
                (docType == "part" && ext.Equals(".dxf", StringComparison.OrdinalIgnoreCase)
                    ? "For sheet-metal parts use sw_export_flat_pattern_dxf."
                    : $"Allowed for: {types}."));
        }
    }

    public static void SaveAs(ModelDoc2 doc, string fullPath, int options, object? exportData)
    {
        int err = 0, warn = 0;
        bool ok = doc.Extension.SaveAs3(fullPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, options, exportData, null, ref err, ref warn);
        if (!ok || err != 0)
        {
            throw new McpException($"Saving '{fullPath}' failed: {SwFlags.Describe<swFileSaveError_e>(err)}" +
                (warn != 0 ? $" (warnings: {SwFlags.Describe<swFileSaveWarning_e>(warn)})" : string.Empty));
        }
    }

    /// <summary>Export a document to a neutral/derived format. The open document keeps its own path.</summary>
    public static string Export(SldWorks app, ModelDoc2 doc, string fullPath, int? stepAp = null, bool allSheets = true)
    {
        string docType = Documents.TypeName(doc.GetType());
        ValidateFormat(fullPath, docType);
        string ext = Path.GetExtension(fullPath).ToLowerInvariant();

        object? exportData = null;
        if (ext == ".pdf" && docType == "drawing")
        {
            var pdf = (ExportPdfData)app.GetExportFileData((int)swExportDataFileType_e.swExportPdfData);
            pdf.SetSheets(allSheets
                ? (int)swExportDataSheetsToExport_e.swExportData_ExportAllSheets
                : (int)swExportDataSheetsToExport_e.swExportData_ExportCurrentSheet, null);
            exportData = pdf;
        }

        int? previousAp = null;
        if ((ext is ".step" or ".stp") && stepAp is int ap)
        {
            if (ap is not (203 or 214 or 242))
            {
                throw new McpException("stepAp must be 203, 214 or 242.");
            }
            previousAp = app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP);
            app.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, ap);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            SaveAs(doc, fullPath, (int)(swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_e.swSaveAsOptions_Copy), exportData);
        }
        finally
        {
            if (previousAp is int prev)
            {
                app.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, prev);
            }
        }

        return fullPath;
    }

    /// <summary>
    /// Export a sheet-metal part's flat pattern via IPartDoc::ExportToDWG2.
    /// SheetMetalOptions bit mask (API help): 1 = flat-pattern geometry, 4 = bend lines, 8 = sketches, 16 = merge coplanar faces.
    /// </summary>
    public static void ExportFlatPattern(ModelDoc2 doc, string fullPath, bool bendLines = true, bool sketches = false, bool mergeCoplanar = true)
    {
        Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Flat-pattern export");
        if (!FeatureWalker.Walk(doc, includeSubFeatures: false).Any(x => x.Feature.GetTypeName2() == "SheetMetal"))
        {
            throw new McpException($"'{doc.GetTitle()}' is not a sheet-metal part.");
        }
        string modelPath = doc.GetPathName();
        if (string.IsNullOrEmpty(modelPath))
        {
            throw new McpException("Save the part before exporting its flat pattern.");
        }

        int options = 1;
        if (bendLines) options |= 4;
        if (sketches) options |= 8;
        if (mergeCoplanar) options |= 16;

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        bool ok = ((PartDoc)doc).ExportToDWG2(fullPath, modelPath, (int)swExportToDWG_e.swExportToDWG_ExportSheetMetal,
            true, null, false, false, options, null);
        if (!ok || !File.Exists(fullPath))
        {
            throw new McpException("SolidWorks failed to export the flat pattern.");
        }
    }
}
