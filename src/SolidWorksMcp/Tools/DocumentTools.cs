using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class DocumentTools(SolidWorksSession sw, PathGuard paths)
{
    [McpServerTool(Name = "sw_list_open_documents", Title = "List open documents", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List documents open in SolidWorks with type, path, unsaved-changes flag and active configuration.")]
    public Task<IReadOnlyList<DocumentInfo>> ListOpenDocuments(
        [Description("Also include hidden documents loaded as references of assemblies/drawings.")] bool includeHidden = false,
        CancellationToken ct = default)
        => sw.RunAsync<IReadOnlyList<DocumentInfo>>(app => Documents.Enumerate(app)
            .Where(m => includeHidden || m.Visible)
            .Select(m => Documents.Describe(app, m))
            .ToList(), ct);

    [McpServerTool(Name = "sw_get_document_info", Title = "Get document info", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Describe one open document (active document if omitted).")]
    public Task<DocumentInfo> GetDocumentInfo(
        [Description("Path, file name or title of an open document. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, Documents.Describe, ct);

    [McpServerTool(Name = "sw_open_document", Title = "Open document", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Open a part (.sldprt), assembly (.sldasm) or drawing (.slddrw) and make it the active document.")]
    public Task<DocumentInfo> OpenDocument(
        [Description("Full path of the file.")] string path,
        [Description("Open read-only (recommended for files in a PDM vault you only want to inspect/export).")] bool readOnly = false,
        [Description("Configuration to open (parts/assemblies). Omit for the last-saved configuration.")] string? configuration = null,
        CancellationToken ct = default)
    {
        string full = paths.Check(path, mustExist: true);
        return sw.RunAsync(app =>
        {
            var (doc, _) = Documents.OpenSilently(app, full, readOnly, visible: true, configuration);
            int err = 0;
            app.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swUserDecision, ref err);
            return Documents.Describe(app, doc);
        }, ct);
    }

    [McpServerTool(Name = "sw_new_document", Title = "New document", Destructive = false, OpenWorld = false)]
    [Description("Create a new part, assembly or drawing from a template (the user's default template when omitted). The document is unsaved until sw_save_as.")]
    public Task<DocumentInfo> NewDocument(
        [Description("part | assembly | drawing")] string type,
        [Description("Optional template path (.prtdot/.asmdot/.drwdot). Default: the template set in Tools > Options > Default Templates.")] string? templatePath = null,
        [Description("Drawing sheet size when the template has none: A4, A3, A2, A1, A0, A, B, C, D, E. Ignored for parts/assemblies.")] string? sheetSize = null,
        CancellationToken ct = default)
    {
        string? template = templatePath is null ? null : paths.Check(templatePath, mustExist: true, nameof(templatePath));
        return sw.RunAsync(app =>
        {
            var doc = CreateNew(app, type, template, sheetSize);
            return Documents.Describe(app, doc);
        }, ct);
    }

    internal static ModelDoc2 CreateNew(SldWorks app, string type, string? template, string? sheetSize)
    {
        var pref = type.Trim().ToLowerInvariant() switch
        {
            "part" => swUserPreferenceStringValue_e.swDefaultTemplatePart,
            "assembly" => swUserPreferenceStringValue_e.swDefaultTemplateAssembly,
            "drawing" => swUserPreferenceStringValue_e.swDefaultTemplateDrawing,
            _ => throw new McpException($"Unknown document type '{type}'. Use part, assembly or drawing."),
        };
        template ??= app.GetUserPreferenceStringValue((int)pref);
        if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
        {
            throw new McpException($"No usable {type} template ('{template}'). Pass templatePath or set Tools > Options > System Options > Default Templates.");
        }

        int paper = (int)PaperSize(sheetSize);
        var doc = app.NewDocument(template, paper, 0, 0) as ModelDoc2
            ?? throw new McpException($"SolidWorks failed to create a new {type} from '{template}'.");
        return doc;
    }

    internal static swDwgPaperSizes_e PaperSize(string? size) => (size ?? "A3").Trim().ToUpperInvariant() switch
    {
        "A4" => swDwgPaperSizes_e.swDwgPaperA4size,
        "A3" => swDwgPaperSizes_e.swDwgPaperA3size,
        "A2" => swDwgPaperSizes_e.swDwgPaperA2size,
        "A1" => swDwgPaperSizes_e.swDwgPaperA1size,
        "A0" => swDwgPaperSizes_e.swDwgPaperA0size,
        "A" => swDwgPaperSizes_e.swDwgPaperAsize,
        "B" => swDwgPaperSizes_e.swDwgPaperBsize,
        "C" => swDwgPaperSizes_e.swDwgPaperCsize,
        "D" => swDwgPaperSizes_e.swDwgPaperDsize,
        "E" => swDwgPaperSizes_e.swDwgPaperEsize,
        _ => throw new McpException($"Unknown sheet size '{size}'. Use A4, A3, A2, A1, A0, A, B, C, D or E."),
    };

    [McpServerTool(Name = "sw_activate_document", Title = "Activate document", Idempotent = true, OpenWorld = false)]
    [Description("Bring an open document to the front so later calls without 'document' act on it.")]
    public Task<DocumentInfo> ActivateDocument(
        [Description("Path, file name or title of an open document.")] string document,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            int err = 0;
            app.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swUserDecision, ref err);
            return Documents.Describe(app, doc);
        }, ct);

    [McpServerTool(Name = "sw_save_document", Title = "Save document", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Save an open document to its current path (overwrites the file on disk).")]
    public Task<string> SaveDocument(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            string path = doc.GetPathName();
            if (string.IsNullOrEmpty(path))
            {
                throw new McpException($"'{doc.GetTitle()}' has never been saved. Use sw_save_as with a path.");
            }
            paths.Check(path);
            if (doc.IsOpenedReadOnly())
            {
                throw new McpException($"'{path}' is open read-only (checked in to PDM or opened read-only). Use sw_save_as to save a copy.");
            }
            int err = 0, warn = 0;
            bool ok = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref err, ref warn);
            if (!ok)
            {
                throw new McpException($"Save failed: {SwFlags.Describe<swFileSaveError_e>(err)}");
            }
            return warn == 0 ? $"Saved {path}" : $"Saved {path} (warnings: {SwFlags.Describe<swFileSaveWarning_e>(warn)})";
        }, ct);

    [McpServerTool(Name = "sw_save_as", Title = "Save document as", Destructive = true, OpenWorld = false)]
    [Description("Save an open document under a new SolidWorks file path (.sldprt/.sldasm/.slddrw). For neutral formats (STEP, PDF, DXF...) use sw_export.")]
    public Task<string> SaveAs(
        [Description("Destination full path with SolidWorks extension.")] string path,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("Save a copy and keep the open document pointing at its original file.")] bool asCopy = false,
        [Description("Overwrite the destination if it exists.")] bool overwrite = false,
        CancellationToken ct = default)
    {
        string full = paths.Check(path);
        if (Documents.TypeFromExtension(full) == swDocumentTypes_e.swDocNONE)
        {
            throw new McpException("sw_save_as only writes native SolidWorks files. Use sw_export for STEP/IGES/STL/PDF/DXF/etc.");
        }
        if (File.Exists(full) && !overwrite)
        {
            throw new McpException($"'{full}' already exists. Pass overwrite=true to replace it.");
        }
        return sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            int options = (int)swSaveAsOptions_e.swSaveAsOptions_Silent;
            if (asCopy)
            {
                options |= (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            Exporter.SaveAs(doc, full, options, exportData: null);
            return $"Saved {doc.GetTitle()} as {full}";
        }, ct);
    }

    [McpServerTool(Name = "sw_close_document", Title = "Close document", Destructive = true, OpenWorld = false)]
    [Description("Close an open document. Unsaved changes are discarded unless save=true.")]
    public Task<string> CloseDocument(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("Save before closing.")] bool save = false,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            string title = doc.GetTitle();
            bool dirty = doc.GetSaveFlag();
            if (save && dirty)
            {
                int err = 0, warn = 0;
                if (!doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref err, ref warn))
                {
                    throw new McpException($"Save failed, document left open: {SwFlags.Describe<swFileSaveError_e>(err)}");
                }
            }
            app.CloseDoc(title);
            return dirty && !save ? $"Closed {title} (unsaved changes discarded)" : $"Closed {title}";
        }, ct);

    [McpServerTool(Name = "sw_rebuild", Title = "Rebuild", Idempotent = true, OpenWorld = false)]
    [Description("Rebuild a document and report rebuild errors/warnings on features.")]
    public Task<object> Rebuild(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("Force rebuild of all features (Ctrl+Q) instead of only changed ones.")] bool force = false,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            bool ok = force ? doc.ForceRebuild3(false) : doc.EditRebuild3();
            var problems = new List<object>();
            if (doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                foreach (var (f, _) in FeatureWalker.Walk(doc, includeSubFeatures: false))
                {
                    int code = f.GetErrorCode2(out bool isWarning);
                    if (code != 0)
                    {
                        problems.Add(new { feature = f.Name, type = f.GetTypeName2(), severity = isWarning ? "warning" : "error", code = ((swFeatureError_e)code).ToString() });
                    }
                }
            }
            return new { rebuilt = ok, problems };
        }, ct);

    [McpServerTool(Name = "sw_screenshot", Title = "Screenshot", ReadOnly = true, OpenWorld = false)]
    [Description("Capture an image of the document's graphics area so you can see the model. Optionally set a standard view and zoom to fit first.")]
    public async Task<IEnumerable<ContentBlock>> Screenshot(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("current | isometric | front | back | top | bottom | left | right | trimetric | dimetric (ignored for drawings)")] string view = "current",
        [Description("Zoom to fit before capturing.")] bool zoomToFit = true,
        CancellationToken ct = default)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"swmcp_{Guid.NewGuid():N}.jpg");
        string title = await sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            int err = 0;
            app.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref err);
            if (doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING && !string.Equals(view, "current", StringComparison.OrdinalIgnoreCase))
            {
                var (name, id) = StandardView(view);
                doc.ShowNamedView2(name, (int)id);
            }
            if (zoomToFit)
            {
                doc.ViewZoomtofit2();
            }
            doc.GraphicsRedraw2();
            Exporter.SaveAs(doc, temp, (int)(swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_e.swSaveAsOptions_Copy), null);
            return doc.GetTitle();
        }, ct);

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(temp, ct);
            return new ContentBlock[]
            {
                new TextContentBlock { Text = $"Screenshot of {title} ({bytes.Length / 1024} KB)" },
                ImageContentBlock.FromBytes(bytes, "image/jpeg"),
            };
        }
        finally
        {
            try { File.Delete(temp); } catch { /* temp cleanup */ }
        }
    }

    internal static (string Name, swStandardViews_e Id) StandardView(string view) => view.Trim().ToLowerInvariant() switch
    {
        "isometric" or "iso" => ("*Isometric", swStandardViews_e.swIsometricView),
        "front" => ("*Front", swStandardViews_e.swFrontView),
        "back" => ("*Back", swStandardViews_e.swBackView),
        "top" => ("*Top", swStandardViews_e.swTopView),
        "bottom" => ("*Bottom", swStandardViews_e.swBottomView),
        "left" => ("*Left", swStandardViews_e.swLeftView),
        "right" => ("*Right", swStandardViews_e.swRightView),
        "trimetric" => ("*Trimetric", swStandardViews_e.swTrimetricView),
        "dimetric" => ("*Dimetric", swStandardViews_e.swDimetricView),
        _ => throw new McpException($"Unknown view '{view}'."),
    };
}
