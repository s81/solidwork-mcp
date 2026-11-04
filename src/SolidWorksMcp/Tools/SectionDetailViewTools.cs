using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

/// <summary>
/// Section and detail views. Both are made the way SolidWorks does it interactively: activate the parent view,
/// sketch a cut line (section) or circle (detail) in it, keep that sketch entity selected, then create the view.
/// With a view active, SketchManager takes the referenced model's coordinates, so sheet points are mapped back
/// through the inverse of View.ModelToViewTransform.
/// </summary>
[McpServerToolType]
public sealed class SectionDetailViewTools(SolidWorksSession sw)
{
    public sealed record SectionInfo(string Label, double DepthMm, bool Partial, bool Reversed, bool SurfaceOnly, bool ScaleWithModel);

    public sealed record DetailInfo(string Label, string Style, bool FullOutline, bool JaggedOutline, bool NoOutline);

    public sealed record SectionDetailViewInfo(string Sheet, string View, string Type, string? ParentView, string Scale,
        double[] PositionMm, double[] OutlineMm, SectionInfo? Section, DetailInfo? Detail);

    [McpServerTool(Name = "sw_add_section_view", Title = "Add section view", OpenWorld = false)]
    [Description("Add a section view to a drawing by cutting a parent view with a straight line (horizontal, vertical or angled) through a point. The cut line is sketched across the whole parent view; the section view is aligned with the parent by default and placed beside it (horizontal cut: below, vertical cut: right) unless positionMm is given. All positions are sheet mm from the lower-left corner; get view outlines from sw_list_section_detail_views or sw_list_drawing_dimensions.")]
    public Task<object> AddSectionView(
        [Description("Parent view name, e.g. 'Drawing View1' (from sw_list_drawing_sheets).")] string view,
        [Description("[x, y] sheet mm of a point the cut line passes through; must be inside the parent view.")] double[] throughMm,
        [Description("horizontal | vertical | angled.")] string orientation = "horizontal",
        [Description("orientation 'angled' only: line angle in degrees from the sheet X axis (counter-clockwise).")] double? angleDeg = null,
        [Description("[x, y] sheet mm for the section view centre. Omit to place it beside the parent.")] double[]? positionMm = null,
        [Description("Section label (A, B, …). Omit for the next unused letter (I, O and Q are skipped).")] string? label = null,
        [Description("Reverse the viewing direction (flip the arrows).")] bool flipDirection = false,
        [Description("Keep the view aligned with the parent (it can only move along the cut normal).")] bool aligned = true,
        [Description("Partial section: cut only up to the ends of the line instead of through the whole model.")] bool partial = false,
        [Description("Show only the cut faces (no geometry behind the cut).")] bool surfaceOnly = false,
        [Description("Limit how far behind the cut the view shows, in model mm. Omit for unlimited.")] double? depthMm = null,
        [Description("Assemblies: do not cut Toolbox fasteners.")] bool excludeFasteners = true,
        [Description("Keep the cut line's position relative to the model when the model changes size.")] bool scaleWithModel = false,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        double angle = SectionDetailSpec.CutAngle(orientation, angleDeg);
        SectionDetailSpec.RequirePoint(throughMm, nameof(throughMm));
        if (positionMm is not null) SectionDetailSpec.RequirePoint(positionMm, nameof(positionMm));
        if (depthMm is not null && !(depthMm > 0)) throw new McpException("depthMm must be > 0 (omit it for unlimited depth).");
        int options = SectionDetailSpec.SectionOptions(aligned, flipDirection, scaleWithModel, partial, surfaceOnly, excludeFasteners);

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Section views");
            var drw = (DrawingDoc)doc;
            var all = DrawingViews.All(drw);
            ViewRef parent = DrawingViews.Find(all, view);
            double[] outline = SectionDetailSpec.RequireOutline(OutlineMm(parent.View), parent.View.GetName2());
            var (start, end) = SectionDetailSpec.CutLine(outline, throughMm, angle);
            double[] at = positionMm ?? SectionDetailSpec.SectionPlacement(outline, angle);
            string useLabel = string.IsNullOrWhiteSpace(label) ? SectionDetailSpec.NextLabel(UsedLabels(all)) : label.Trim();

            SketchSegment line = Sketch(app, doc, drw, parent, sm =>
            {
                var a = ToModel(app, parent.View, start);
                var b = ToModel(app, parent.View, end);
                return sm.CreateLine(a[0], a[1], a[2], b[0], b[1], b[2]);
            }, "cut line");

            View? created = drw.CreateSectionViewAt5(Units.MmToM(at[0]), Units.MmToM(at[1]), 0, useLabel, options, null,
                depthMm is null ? 0 : Units.MmToM(depthMm.Value));
            if (created is null)
            {
                DeleteSelected(doc, line);
                throw new McpException("SolidWorks did not create the section view. Check that the cut line crosses model geometry in the parent view.");
            }
            doc.ClearSelection2(true);
            doc.GraphicsRedraw2();
            return new
            {
                cutLineMm = new { start, end, angleDeg = Units.Round(angle, 6) },
                view = Describe(parent.Sheet, created),
            };
        }, ct);
    }

    [McpServerTool(Name = "sw_add_detail_view", Title = "Add detail view", OpenWorld = false)]
    [Description("Add a detail view to a drawing: a circle drawn on a parent view at centerMm/radiusMm (sheet mm) is shown enlarged, by default at twice the parent view's scale, to the right of the parent unless positionMm is given.")]
    public Task<object> AddDetailView(
        [Description("Parent view name, e.g. 'Drawing View1' (from sw_list_drawing_sheets).")] string view,
        [Description("[x, y] sheet mm of the detail circle centre; must be inside the parent view.")] double[] centerMm,
        [Description("Detail circle radius in sheet (paper) mm.")] double radiusMm,
        [Description("Detail view scale, e.g. '2:1', '5:1' or '4'. Omit for twice the parent view's scale.")] string? scale = null,
        [Description("[x, y] sheet mm for the detail view centre. Omit to place it right of the parent.")] double[]? positionMm = null,
        [Description("Detail label (A, B, …). Omit for the next unused letter (I, O and Q are skipped).")] string? label = null,
        [Description("standard | broken | leader | no_leader | connected (how the circle and label are drawn on the parent).")] string style = "standard",
        [Description("circle | profile | hidden (how the detail boundary is drawn on the parent).")] string circleDisplay = "circle",
        [Description("Draw the full outline around the detail view.")] bool fullOutline = false,
        [Description("Use a jagged outline on the detail view.")] bool jaggedOutline = false,
        [Description("jaggedOutline only: shape intensity 1 (smooth) to 5 (rough).")] int shapeIntensity = 3,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        SectionDetailSpec.RequirePoint(centerMm, nameof(centerMm));
        if (positionMm is not null) SectionDetailSpec.RequirePoint(positionMm, nameof(positionMm));
        var detailStyle = SectionDetailSpec.ParseDetailStyle(style);
        var display = SectionDetailSpec.ParseCircleDisplay(circleDisplay);
        (double Num, double Den)? fixedScale = scale is null ? null : SectionDetailSpec.ParseScale(scale);
        if (shapeIntensity is < 1 or > 5) throw new McpException("shapeIntensity must be between 1 and 5.");

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Detail views");
            var drw = (DrawingDoc)doc;
            var all = DrawingViews.All(drw);
            ViewRef parent = DrawingViews.Find(all, view);
            double[] outline = SectionDetailSpec.RequireOutline(OutlineMm(parent.View), parent.View.GetName2());
            SectionDetailSpec.RequireRadius(radiusMm, outline, centerMm);

            double parentScale = parent.View.ScaleDecimal;
            var (num, den) = fixedScale ?? ParentScaleTimesTwo(parent.View);
            double[] at = positionMm ?? SectionDetailSpec.DetailPlacement(outline, centerMm, radiusMm, parentScale, num / den);
            string useLabel = string.IsNullOrWhiteSpace(label) ? SectionDetailSpec.NextLabel(UsedLabels(all)) : label.Trim();

            SketchSegment circle = Sketch(app, doc, drw, parent, sm =>
            {
                var c = ToModel(app, parent.View, centerMm);
                var r = ToModel(app, parent.View, new[] { centerMm[0] + radiusMm, centerMm[1] });
                return sm.CreateCircle(c[0], c[1], c[2], r[0], r[1], r[2]);
            }, "detail circle");

            var created = drw.CreateDetailViewAt4(Units.MmToM(at[0]), Units.MmToM(at[1]), 0, (int)detailStyle, num, den, useLabel,
                (int)display, fullOutline, jaggedOutline, false, shapeIntensity) as View;
            if (created is null)
            {
                DeleteSelected(doc, circle);
                throw new McpException("SolidWorks did not create the detail view. Check that the circle encloses model geometry in the parent view.");
            }
            doc.ClearSelection2(true);
            doc.GraphicsRedraw2();
            return new { circleMm = new { center = centerMm, radius = radiusMm }, view = Describe(parent.Sheet, created) };
        }, ct);
    }

    [McpServerTool(Name = "sw_list_section_detail_views", Title = "List section and detail views", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the section and detail views of a drawing: sheet, view name, parent view, label, scale, position and outline (sheet mm), plus section settings (depth, partial, reversed, surface only) or detail settings (style, outline).")]
    public Task<object> ListSectionDetailViews(
        [Description("Only this sheet. Omit for all sheets.")] string? sheet = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Listing section and detail views");
            var all = DrawingViews.All((DrawingDoc)doc);
            if (sheet is not null && !all.Any(v => v.IsSheet && string.Equals(v.Sheet, sheet.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new McpException($"Sheet '{sheet}' not found. Sheets: {string.Join(", ", all.Where(v => v.IsSheet).Select(v => v.Sheet))}.");
            }
            var views = all
                .Where(v => !v.IsSheet && (sheet is null || string.Equals(v.Sheet, sheet.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Where(v => v.View.Type is (int)swDrawingViewTypes_e.swDrawingSectionView or (int)swDrawingViewTypes_e.swDrawingDetailView)
                .Select(v => Describe(v.Sheet, v.View))
                .ToList();
            return new { count = views.Count, views };
        }, ct);

    private static double[]? OutlineMm(View v) => (v.GetOutline() as double[])?.Select(Units.MToMm).ToArray();

    private static (double Num, double Den) ParentScaleTimesTwo(View v)
        => v.ScaleRatio is double[] { Length: 2 } r && r[0] > 0 && r[1] > 0 ? (r[0] * 2, r[1]) : (v.ScaleDecimal * 2, 1);

    /// <summary>Sheet mm → the parent view's model coordinates (m), which SketchManager uses while that view is active.</summary>
    private static double[] ToModel(SldWorks app, View v, double[] sheetMm)
    {
        var math = (MathUtility)app.GetMathUtility();
        var toModel = (MathTransform)v.ModelToViewTransform.Inverse();
        var p = (MathPoint)math.CreatePoint(new[] { Units.MmToM(sheetMm[0]), Units.MmToM(sheetMm[1]), 0.0 });
        return (double[])((MathPoint)p.MultiplyTransform(toModel)).ArrayData;
    }

    /// <summary>Sketch one entity in the parent view and leave it as the only selection.</summary>
    private static SketchSegment Sketch(SldWorks app, ModelDoc2 doc, DrawingDoc drw, ViewRef parent, Func<SketchManager, SketchSegment?> create, string what)
    {
        drw.ActivateSheet(parent.Sheet);
        if (!drw.ActivateView(parent.View.GetName2()))
        {
            throw new McpException($"Could not activate view '{parent.View.GetName2()}'.");
        }
        doc.ClearSelection2(true);
        SketchManager sm = doc.SketchManager;
        bool addToDb = sm.AddToDB;
        sm.AddToDB = true; // no snapping to nearby geometry
        SketchSegment? seg;
        try
        {
            seg = create(sm);
        }
        finally
        {
            sm.AddToDB = addToDb;
        }
        if (seg is null)
        {
            throw new McpException($"SolidWorks did not sketch the {what} in view '{parent.View.GetName2()}'.");
        }
        doc.ClearSelection2(true);
        if (!seg.Select4(false, null))
        {
            DeleteSelected(doc, seg);
            throw new McpException($"Could not select the {what} sketched in view '{parent.View.GetName2()}'.");
        }
        return seg;
    }

    /// <summary>Remove the helper sketch entity after a failed create so the parent view is left as it was.</summary>
    private static void DeleteSelected(ModelDoc2 doc, SketchSegment seg)
    {
        doc.ClearSelection2(true);
        if (seg.Select4(false, null))
        {
            doc.EditDelete();
        }
        doc.ClearSelection2(true);
    }

    private static IEnumerable<string?> UsedLabels(List<ViewRef> all)
    {
        foreach (var v in all.Where(v => !v.IsSheet))
        {
            if (v.View.Type == (int)swDrawingViewTypes_e.swDrawingSectionView && v.View.GetSection() is DrSection s)
            {
                yield return s.GetLabel();
            }
            else if (v.View.Type == (int)swDrawingViewTypes_e.swDrawingDetailView && v.View.GetDetail() is DetailCircle d)
            {
                yield return d.GetLabel();
            }
        }
    }

    private static SectionDetailViewInfo Describe(string sheet, View v)
    {
        double[] ratio = v.ScaleRatio as double[] ?? new[] { v.ScaleDecimal, 1.0 };
        double[] position = (v.Position as double[] ?? Array.Empty<double>()).Take(2).Select(m => Units.Round(Units.MToMm(m), 3)).ToArray();
        double[] outline = (OutlineMm(v) ?? Array.Empty<double>()).Select(mm => Units.Round(mm, 3)).ToArray();
        SectionInfo? section = null;
        DetailInfo? detail = null;
        string type;
        if (v.Type == (int)swDrawingViewTypes_e.swDrawingSectionView)
        {
            type = "section";
            if (v.GetSection() is DrSection s)
            {
                section = new SectionInfo(s.GetLabel() ?? string.Empty, Units.Round(Units.MToMm(s.SectionDepth), 3), s.GetPartialSection(),
                    s.GetReversedCutDirection(), s.GetDisplayOnlySurfaceCut(), s.GetScaleWithModelChanges());
            }
        }
        else if (v.Type == (int)swDrawingViewTypes_e.swDrawingDetailView)
        {
            type = "detail";
            if (v.GetDetail() is DetailCircle d)
            {
                detail = new DetailInfo(d.GetLabel() ?? string.Empty, SectionDetailSpec.DetailStyleName(d.GetStyle()), d.HasFullOutline(), d.JaggedOutline, d.NoOutline);
            }
        }
        else
        {
            type = ((swDrawingViewTypes_e)v.Type).ToString().Replace("swDrawing", string.Empty);
        }
        string? parentName = (v.GetBaseView() as View)?.GetName2();
        return new SectionDetailViewInfo(sheet, v.GetName2(), type, parentName,
            $"{Units.Round(ratio[0], 6)}:{Units.Round(ratio.Length > 1 ? ratio[1] : 1, 6)}", position, outline, section, detail);
    }
}
