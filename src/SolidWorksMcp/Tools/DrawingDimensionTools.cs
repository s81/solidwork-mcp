using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class DrawingDimensionTools(SolidWorksSession sw)
{
    private sealed record ViewRef(string Sheet, View View, bool IsSheet);

    private static List<ViewRef> AllViews(DrawingDoc drw)
    {
        var list = new List<ViewRef>();
        foreach (object[] sheetViews in (drw.GetViews() as object[] ?? Array.Empty<object>()).OfType<object[]>())
        {
            if (sheetViews.Length == 0 || sheetViews[0] is not View sheetView)
            {
                continue;
            }
            string sheet = sheetView.GetName2();
            list.Add(new ViewRef(sheet, sheetView, true));
            list.AddRange(sheetViews.Skip(1).OfType<View>().Select(v => new ViewRef(sheet, v, false)));
        }
        return list;
    }

    private static ViewRef FindView(List<ViewRef> views, string name)
        => views.FirstOrDefault(v => !v.IsSheet && string.Equals(v.View.GetName2(), name.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? throw new McpException($"View '{name}' not found. Views: {string.Join(", ", views.Where(v => !v.IsSheet).Select(v => v.View.GetName2()))}.");

    private static void SelectView(ModelDoc2 doc, DrawingDoc drw, ViewRef v, bool append)
    {
        drw.ActivateSheet(v.Sheet);
        if (!doc.Extension.SelectByID2(v.View.GetName2(), "DRAWINGVIEW", 0, 0, 0, append, 0, null, 0))
        {
            throw new McpException($"Could not select view '{v.View.GetName2()}'.");
        }
    }

    [McpServerTool(Name = "sw_insert_model_dimensions", Title = "Insert model items into drawing views", OpenWorld = false)]
    [Description("Insert Model Items into existing drawing views: dimensions marked for drawing (default), unmarked dimensions, Hole Wizard dimensions, hole callouts, notes, datums, GD&T and more. Applies to all views or only the named ones. Reports how many dimensions each view gained.")]
    public Task<object> InsertModelDimensions(
        [Description("View names (from sw_list_drawing_sheets). Omit for all views.")] string[]? views = null,
        [Description("Item types: marked_dimensions, unmarked_dimensions, hole_wizard_locations, hole_wizard_profiles, hole_callouts, toleranced_dimensions, instance_counts, cosmetic_threads, notes, datums, gtols, surface_finish. Default: marked_dimensions.")] string[]? types = null,
        [Description("Also insert dimensions already shown in another view.")] bool allowDuplicates = false,
        [Description("Place dimensions where they were placed in the model sketches.")] bool usePlacementInSketch = false,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        int mask = DrawingDimensionSpec.ParseItemTypes(types);
        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Inserting model items");
            var drw = (DrawingDoc)doc;
            var all = AllViews(drw);
            var targets = views is { Length: > 0 } ? views.Select(n => FindView(all, n)).ToList() : all.Where(v => !v.IsSheet).ToList();
            if (targets.Count == 0)
            {
                throw new McpException("The drawing has no model views.");
            }
            var before = targets.ToDictionary(v => v.View.GetName2(), v => v.View.GetDisplayDimensionCount());

            doc.ClearSelection2(true);
            bool allViews = views is not { Length: > 0 };
            if (!allViews)
            {
                // With AllViews = false, Insert Model Items acts on the selected views.
                for (int i = 0; i < targets.Count; i++)
                {
                    SelectView(doc, drw, targets[i], append: i > 0);
                }
            }
            // InsertModelAnnotations3(Option, Types, AllViews, DuplicateDims, HiddenFeatureDims, UsePlacementInSketch)
            drw.InsertModelAnnotations3((int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel, mask, allViews,
                allowDuplicates, false, usePlacementInSketch);
            doc.ClearSelection2(true);
            doc.GraphicsRedraw2();

            var perView = targets.Select(v => new
            {
                sheet = v.Sheet,
                view = v.View.GetName2(),
                added = v.View.GetDisplayDimensionCount() - before[v.View.GetName2()],
                total = v.View.GetDisplayDimensionCount(),
            }).ToList();
            return new { types = types is { Length: > 0 } ? types : new[] { "marked_dimensions" }, views = perView, added = perView.Sum(x => x.added) };
        }, ct);
    }

    public sealed record DrawingDimensionInfo(string Sheet, string View, string Name, string Type, double Value, string Unit,
        string State, bool IsReference, string Tolerance, double? ToleranceMinus, double? TolerancePlus, double[]? PositionMm);

    [McpServerTool(Name = "sw_list_drawing_dimensions", Title = "List drawing dimensions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List every dimension shown on a drawing, per sheet and view: name, type (linear, diameter, radial, angular...), value (mm or degrees), driving/driven, reference, tolerance type and values, and position on the sheet (mm). Use it to check a drawing before release.")]
    public Task<IReadOnlyList<DrawingDimensionInfo>> ListDrawingDimensions(
        [Description("Only this sheet (optional).")] string? sheet = null,
        [Description("Only this view (optional).")] string? view = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<DrawingDimensionInfo>>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Listing drawing dimensions");
            var result = new List<DrawingDimensionInfo>();
            foreach (var v in AllViews((DrawingDoc)doc))
            {
                if (sheet is not null && !string.Equals(v.Sheet, sheet.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (view is not null && !string.Equals(v.View.GetName2(), view.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

                foreach (DisplayDimension dd in (v.View.GetDisplayDimensions() as object[] ?? Array.Empty<object>()).OfType<DisplayDimension>())
                {
                    if (dd.GetDimension2(0) is not Dimension dim)
                    {
                        continue;
                    }
                    var (value, unit) = ModelingTools.ToUser(dim);
                    double Convert(double si) => unit switch
                    {
                        "mm" => Units.Round(Units.MToMm(si)),
                        "deg" => Units.Round(Units.RadToDeg(si)),
                        _ => si,
                    };
                    DimensionTolerance? tol = dim.Tolerance;
                    int tolType = tol?.Type ?? (int)swTolType_e.swTolNONE;
                    bool hasValues = tolType is (int)swTolType_e.swTolBILAT or (int)swTolType_e.swTolLIMIT or (int)swTolType_e.swTolSYMMETRIC;
                    double[]? pos = (dd.GetAnnotation() as Annotation)?.GetPosition() as double[];
                    result.Add(new DrawingDimensionInfo(
                        v.Sheet, v.View.GetName2(), dim.FullName,
                        DrawingDimensionSpec.DimensionTypeName(dd.Type2),
                        value, unit,
                        DrawingDimensionSpec.DrivenStateName(dim.DrivenState),
                        dim.IsReference(),
                        DrawingDimensionSpec.ToleranceName(tolType),
                        hasValues ? Convert(tol!.GetMinValue()) : null,
                        hasValues ? Convert(tol!.GetMaxValue()) : null,
                        pos is { Length: >= 2 } ? new[] { Units.Round(Units.MToMm(pos[0]), 2), Units.Round(Units.MToMm(pos[1]), 2) } : null));
                }
            }
            return result;
        }, ct);

    [McpServerTool(Name = "sw_auto_dimension_view", Title = "Auto-dimension a drawing view", OpenWorld = false)]
    [Description("Let SolidWorks place dimensions automatically on a drawing view (Auto Dimension): baseline, ordinate, chain or centerline schemes horizontally and vertically. Results depend on the geometry; review with sw_list_drawing_dimensions or sw_screenshot.")]
    public Task<object> AutoDimensionView(
        [Description("View name (from sw_list_drawing_sheets).")] string view,
        [Description("Horizontal dimensions scheme: baseline | ordinate | chain | centerline.")] string horizontalScheme = "baseline",
        [Description("Vertical dimensions scheme: baseline | ordinate | chain | centerline.")] string verticalScheme = "baseline",
        [Description("Place horizontal dimensions above or below the view.")] string horizontalPlacement = "below",
        [Description("Place vertical dimensions left or right of the view.")] string verticalPlacement = "left",
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        var h = DrawingDimensionSpec.ParseScheme(horizontalScheme, nameof(horizontalScheme));
        var vs = DrawingDimensionSpec.ParseScheme(verticalScheme, nameof(verticalScheme));
        var hp = DrawingDimensionSpec.ParseHorizontalPlacement(horizontalPlacement);
        var vp = DrawingDimensionSpec.ParseVerticalPlacement(verticalPlacement);

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Auto Dimension");
            var drw = (DrawingDoc)doc;
            var target = FindView(AllViews(drw), view);
            int before = target.View.GetDisplayDimensionCount();

            doc.ClearSelection2(true);
            SelectView(doc, drw, target, append: false);
            drw.ActivateView(target.View.GetName2());
            int status = drw.AutoDimension((int)swAutodimEntities_e.swAutodimEntitiesAll, (int)h, (int)hp, (int)vs, (int)vp);
            doc.ClearSelection2(true);
            if (status != (int)swAutodimStatus_e.swAutodimStatusSuccess)
            {
                string reason = Enum.IsDefined(typeof(swAutodimStatus_e), status) ? ((swAutodimStatus_e)status).ToString().Replace("swAutodimStatus", string.Empty) : status.ToString();
                throw new McpException($"Auto Dimension failed: {reason}. Try another scheme, or insert model dimensions with sw_insert_model_dimensions.");
            }
            doc.GraphicsRedraw2();
            int after = target.View.GetDisplayDimensionCount();
            return new { view = target.View.GetName2(), added = after - before, total = after };
        }, ct);
    }

    [McpServerTool(Name = "sw_add_drawing_dimension", Title = "Add drawing dimension", OpenWorld = false)]
    [Description("Add a dimension on a drawing between one or two entities picked at sheet positions (mm from the sheet's lower-left corner): one edge gives its length, diameter or radius; two give the distance or angle between them. Orientation smart, horizontal or vertical. Points must lie on visible edges/vertices of the view; get view positions from sw_list_drawing_dimensions or sw_screenshot.")]
    public Task<object> AddDrawingDimension(
        [Description("First pick [x, y] in sheet mm.")] double[] point1,
        [Description("Text position [x, y] in sheet mm.")] double[] textPosition,
        [Description("Optional second pick [x, y] in sheet mm.")] double[]? point2 = null,
        [Description("What to pick at point1: edge | vertex | sketch_segment | sketch_point.")] string entity1 = "edge",
        [Description("What to pick at point2.")] string entity2 = "edge",
        [Description("smart (SolidWorks decides) | horizontal | vertical.")] string orientation = "smart",
        [Description("View the points belong to. Omit to use the view whose outline contains point1.")] string? view = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        DrawingDimensionSpec.RequireSheetPoint(point1, nameof(point1));
        DrawingDimensionSpec.RequireSheetPoint(textPosition, nameof(textPosition));
        if (point2 is not null) DrawingDimensionSpec.RequireSheetPoint(point2, nameof(point2));
        string type1 = DrawingDimensionSpec.PickType(entity1);
        string type2 = DrawingDimensionSpec.PickType(entity2);
        var orient = DrawingDimensionSpec.ParseOrientation(orientation);

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Adding dimensions");
            var drw = (DrawingDoc)doc;
            var all = AllViews(drw);
            ViewRef target;
            if (view is not null)
            {
                target = FindView(all, view);
            }
            else
            {
                string active = (drw.GetCurrentSheet() as Sheet)?.GetName() ?? string.Empty;
                var candidates = all.Where(v => !v.IsSheet && v.Sheet == active).ToList();
                var outlines = candidates.Select(v => (v.View.GetOutline() as double[] ?? Array.Empty<double>()).Select(Units.MToMm).ToArray()).ToList();
                int idx = DrawingDimensionSpec.ViewAt(outlines, point1);
                target = idx >= 0 ? candidates[idx]
                    : throw new McpException($"No view on the active sheet '{active}' contains point1 [{string.Join(", ", point1)}] mm. Pass 'view' or check the coordinates.");
            }

            drw.ActivateSheet(target.Sheet);
            drw.ActivateView(target.View.GetName2());
            doc.ClearSelection2(true);
            bool Pick(double[] p, string type, bool append)
                => doc.Extension.SelectByID2(string.Empty, type, Units.MmToM(p[0]), Units.MmToM(p[1]), 0, append, 0, null, 0);
            if (!Pick(point1, type1, false))
            {
                throw new McpException($"No {entity1} found at point1 [{string.Join(", ", point1)}] mm in view '{target.View.GetName2()}'.");
            }
            if (point2 is not null && !Pick(point2, type2, true))
            {
                doc.ClearSelection2(true);
                throw new McpException($"No {entity2} found at point2 [{string.Join(", ", point2)}] mm in view '{target.View.GetName2()}'.");
            }

            double tx = Units.MmToM(textPosition[0]), ty = Units.MmToM(textPosition[1]);
            var dd = orient switch
            {
                DrawingDimensionSpec.Orientation.Horizontal => doc.AddHorizontalDimension2(tx, ty, 0),
                DrawingDimensionSpec.Orientation.Vertical => doc.AddVerticalDimension2(tx, ty, 0),
                _ => doc.AddDimension2(tx, ty, 0),
            } as DisplayDimension;
            doc.ClearSelection2(true);
            if (dd?.GetDimension2(0) is not Dimension dim)
            {
                throw new McpException("SolidWorks did not create a dimension for these picks (e.g. two parallel picks with orientation vertical/horizontal that cannot be measured that way).");
            }
            var (value, unit) = ModelingTools.ToUser(dim);
            return new { view = target.View.GetName2(), name = dim.FullName, type = DrawingDimensionSpec.DimensionTypeName(dd.Type2), value, unit };
        }, ct);
    }
}
