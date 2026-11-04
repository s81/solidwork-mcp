using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class DrawingTools(SolidWorksSession sw, PathGuard paths)
{
    [McpServerTool(Name = "sw_create_drawing", Title = "Create drawing from model", OpenWorld = false)]
    [Description("Create a new drawing of a part/assembly with the three standard orthographic views, optionally an isometric view and imported model dimensions. Optionally save it.")]
    public Task<object> CreateDrawing(
        [Description("Full path of the .sldprt/.sldasm.")] string modelPath,
        [Description("first (ISO/Europe/Middle East) or third (ANSI/US) angle projection.")] string projection = "first",
        [Description("Add an isometric view at the top-right.")] bool includeIsometric = true,
        [Description("Import dimensions marked for drawing from the model.")] bool importDimensions = false,
        [Description("Drawing template (.drwdot). Default: user default drawing template.")] string? templatePath = null,
        [Description("Sheet size if the template has none: A4, A3, A2, A1, A0, A..E.")] string? sheetSize = "A3",
        [Description("If set, save the drawing to this .slddrw path.")] string? savePath = null,
        CancellationToken ct = default)
    {
        string model = paths.Check(modelPath, mustExist: true, nameof(modelPath));
        string? template = templatePath is null ? null : paths.Check(templatePath, mustExist: true, nameof(templatePath));
        string? save = savePath is null ? null : paths.Check(savePath, argumentName: nameof(savePath));
        bool first = projection.Trim().ToLowerInvariant() switch
        {
            "first" or "1st" or "iso" => true,
            "third" or "3rd" or "ansi" => false,
            _ => throw new McpException("projection must be 'first' or 'third'."),
        };

        return sw.RunAsync<object>(app =>
        {
            var (modelDoc, _) = Documents.OpenSilently(app, model, readOnly: false, visible: true);
            if (modelDoc.GetType() == (int)swDocumentTypes_e.swDocDRAWING)
            {
                throw new McpException("modelPath must be a part or assembly.");
            }

            var drwModel = DocumentTools.CreateNew(app, "drawing", template, sheetSize);
            var drw = (DrawingDoc)drwModel;
            bool ok = first ? drw.Create1stAngleViews2(model) : drw.Create3rdAngleViews2(model);
            if (!ok)
            {
                throw new McpException("SolidWorks could not create the standard views.");
            }

            if (includeIsometric)
            {
                var (w, h) = SheetSize(drw);
                drw.CreateDrawViewFromModelView3(model, "*Isometric", w * 0.78, h * 0.72, 0);
            }

            if (importDimensions)
            {
                drw.InsertModelAnnotations3(
                    (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                    (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing,
                    true, false, false, false);
            }

            drwModel.ViewZoomtofit2();
            if (save is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(save)!);
                Exporter.SaveAs(drwModel, save, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null);
            }
            return new { drawing = drwModel.GetTitle(), path = drwModel.GetPathName(), views = ListViews(drw) };
        }, ct);
    }

    private static (double Width, double Height) SheetSize(DrawingDoc drw)
    {
        // ISheet::GetProperties2: [0]=paper size, [1]=template, [2]=scale1, [3]=scale2, [4]=first angle, [5]=width (m), [6]=height (m) ...
        if (drw.GetCurrentSheet() is Sheet sheet && sheet.GetProperties2() is double[] p && p.Length > 6)
        {
            return (p[5], p[6]);
        }
        return (0.42, 0.297); // A3 landscape fallback
    }

    public sealed record ViewInfo(string Sheet, string View, string Type, string? Model, string? Configuration, double Scale);

    private static List<ViewInfo> ListViews(DrawingDoc drw)
    {
        var result = new List<ViewInfo>();
        if (drw.GetViews() is not object[] sheets)
        {
            return result;
        }
        foreach (object[] sheetViews in sheets.OfType<object[]>())
        {
            if (sheetViews.Length == 0 || sheetViews[0] is not View sheetView)
            {
                continue;
            }
            string sheetName = sheetView.GetName2();
            foreach (View v in sheetViews.Skip(1).OfType<View>())
            {
                result.Add(new ViewInfo(sheetName, v.GetName2(), ((swDrawingViewTypes_e)v.Type).ToString().Replace("swDrawing", string.Empty),
                    v.GetReferencedModelName(), v.ReferencedConfiguration, Units.Round(v.ScaleDecimal)));
            }
        }
        return result;
    }

    [McpServerTool(Name = "sw_list_drawing_sheets", Title = "List drawing sheets and views", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the sheets of a drawing (size, scale) and the views on each with the referenced model.")]
    public Task<object> ListDrawingSheets(
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Listing sheets");
            var drw = (DrawingDoc)doc;
            string active = (drw.GetCurrentSheet() as Sheet)?.GetName() ?? string.Empty;
            var names = drw.GetSheetNames() as string[] ?? Array.Empty<string>();
            var sheets = names.Select(n =>
            {
                var s = drw.Sheet[n] as Sheet;
                var p = s?.GetProperties2() as double[];
                return new
                {
                    name = n,
                    active = n == active,
                    widthMm = p is { Length: > 6 } ? Units.Round(Units.MToMm(p[5]), 1) : (double?)null,
                    heightMm = p is { Length: > 6 } ? Units.Round(Units.MToMm(p[6]), 1) : (double?)null,
                    scale = p is { Length: > 3 } ? $"{p[2]}:{p[3]}" : null,
                    firstAngle = p is { Length: > 4 } ? p[4] != 0 : (bool?)null,
                };
            }).ToList();
            return new { sheets, views = ListViews(drw) };
        }, ct);

    [McpServerTool(Name = "sw_activate_sheet", Title = "Activate sheet", Idempotent = true, OpenWorld = false)]
    [Description("Make a drawing sheet active.")]
    public Task<string> ActivateSheet(
        [Description("Sheet name.")] string sheet,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Activating a sheet");
            return ((DrawingDoc)doc).ActivateSheet(sheet)
                ? $"Active sheet: {sheet}"
                : throw new McpException($"Sheet '{sheet}' not found.");
        }, ct);
}
