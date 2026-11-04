using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class BalloonTools(SolidWorksSession sw)
{
    private static List<Note> Balloons(View v)
        => (v.GetNotes() as object[] ?? Array.Empty<object>()).OfType<Note>().Where(n => n.IsBomBalloon()).ToList();

    [McpServerTool(Name = "sw_auto_balloon", Title = "Auto balloon drawing views", OpenWorld = false)]
    [Description("Add BOM balloons automatically to assembly drawing views (Auto Balloon): one balloon per component, numbered to match the BOM table. Choose layout, style, size and the text shown (item number by default; quantity, part number, file name or a custom property). Insert a BOM table first (sw_insert_bom_table) so numbers match it.")]
    public Task<object> AutoBalloon(
        [Description("View names. Omit for every model view on the active sheet.")] string[]? views = null,
        [Description("Arrangement: square | circle | top | bottom | right | left.")] string layout = "square",
        [Description("Balloon shape: circular, split_circle, triangle, hexagon, box, diamond, pentagon, square, underline, none.")] string style = "circular",
        [Description("Balloon size: tightest or 1_char..5_chars.")] string size = "2_chars",
        [Description("Upper text: item_number | quantity | part_number | file_name | component_reference | custom_property | custom.")] string upperText = "item_number",
        [Description("Custom text, or the property name for custom_property (upper).")] string? upperValue = null,
        [Description("Lower text (split styles only), same choices as upperText. Omit for none.")] string? lowerText = null,
        [Description("Custom text or property name (lower).")] string? lowerValue = null,
        [Description("One balloon per part even when it appears several times in a view.")] bool ignoreMultipleInstances = true,
        [Description("Attach leaders to faces instead of edges.")] bool attachToFaces = false,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        var lay = BalloonSpec.ParseLayout(layout);
        var st = BalloonSpec.ParseStyle(style);
        var sz = BalloonSpec.ParseSize(size);
        var upper = BalloonSpec.ParseText(upperText, upperValue, nameof(upperText));
        var lower = lowerText is null ? ((swBalloonTextContent_e?)null, string.Empty) : BalloonSpec.ParseText(lowerText, lowerValue, nameof(lowerText));
        if (lower.Item1 is not null && !BalloonSpec.IsSplit(st))
        {
            throw new McpException("lowerText only shows on split styles; use style split_circle.");
        }

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Balloons");
            var drw = (DrawingDoc)doc;
            var all = DrawingViews.All(drw);
            string active = (drw.GetCurrentSheet() as Sheet)?.GetName() ?? string.Empty;
            var targets = views is { Length: > 0 }
                ? views.Select(n => DrawingViews.Find(all, n)).ToList()
                : all.Where(v => !v.IsSheet && v.Sheet == active).ToList();
            if (targets.Count == 0)
            {
                throw new McpException($"No model views to balloon on sheet '{active}'.");
            }
            var before = targets.ToDictionary(v => v.View.GetName2(), v => Balloons(v.View).Count);

            // AutoBalloon5 acts on the selected views.
            doc.ClearSelection2(true);
            for (int i = 0; i < targets.Count; i++)
            {
                DrawingViews.Select(doc, drw, targets[i], append: i > 0);
            }
            AutoBalloonOptions opt = drw.CreateAutoBalloonOptions();
            opt.Layout = (int)lay;
            opt.Style = (int)st;
            opt.Size = (int)sz;
            opt.UpperTextContent = (int)upper.Content;
            opt.UpperText = upper.Text;
            if (lower.Item1 is swBalloonTextContent_e lc)
            {
                opt.LowerTextContent = (int)lc;
                opt.LowerText = lower.Item2;
            }
            opt.IgnoreMultiple = ignoreMultipleInstances;
            opt.LeaderAttachmentToFaces = attachToFaces;
            var created = drw.AutoBalloon5(opt) as object[];
            doc.ClearSelection2(true);
            doc.GraphicsRedraw2();

            var perView = targets.Select(v => new
            {
                view = v.View.GetName2(),
                added = Balloons(v.View).Count - before[v.View.GetName2()],
                total = Balloons(v.View).Count,
            }).ToList();
            if (created is null && perView.All(p => p.added == 0))
            {
                throw new McpException("No balloons were added. Auto Balloon needs assembly views with visible components (part views have nothing to balloon).");
            }
            return new { views = perView, added = perView.Sum(p => p.added) };
        }, ct);
    }

    public sealed record BalloonInfo(string Sheet, string View, string UpperText, string LowerText, string Style, double[]? PositionMm);

    [McpServerTool(Name = "sw_list_balloons", Title = "List balloons", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the BOM balloons in a drawing per sheet and view: upper/lower text (e.g. item numbers), style and position (sheet mm). Compare with sw_read_bom_tables to find components without a balloon.")]
    public Task<IReadOnlyList<BalloonInfo>> ListBalloons(
        [Description("Only this sheet (optional).")] string? sheet = null,
        [Description("Only this view (optional).")] string? view = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<BalloonInfo>>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Balloons");
            var result = new List<BalloonInfo>();
            foreach (var v in DrawingViews.All((DrawingDoc)doc))
            {
                if (sheet is not null && !string.Equals(v.Sheet, sheet.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (view is not null && !string.Equals(v.View.GetName2(), view.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                foreach (Note n in Balloons(v.View))
                {
                    double[]? pos = (n.GetAnnotation() as Annotation)?.GetPosition() as double[];
                    result.Add(new BalloonInfo(v.Sheet, v.View.GetName2(),
                        n.GetBomBalloonText(true) ?? string.Empty, n.GetBomBalloonText(false) ?? string.Empty,
                        BalloonSpec.StyleName(n.GetBalloonStyle()),
                        pos is { Length: >= 2 } ? new[] { Units.Round(Units.MToMm(pos[0]), 2), Units.Round(Units.MToMm(pos[1]), 2) } : null));
                }
            }
            return result;
        }, ct);

    [McpServerTool(Name = "sw_add_balloon", Title = "Add balloon", OpenWorld = false)]
    [Description("Add one BOM balloon to a component in a drawing view: pick an edge of the component at a sheet position (mm from the sheet's lower-left corner) and place the balloon at another position. Use for components Auto Balloon missed.")]
    public Task<object> AddBalloon(
        [Description("[x, y] in sheet mm on a visible edge of the component.")] double[] point,
        [Description("[x, y] in sheet mm where the balloon goes.")] double[] balloonPosition,
        [Description("Balloon shape (see sw_auto_balloon).")] string style = "circular",
        [Description("Balloon size: tightest or 1_char..5_chars.")] string size = "2_chars",
        [Description("Upper text: item_number | quantity | part_number | file_name | component_reference | custom_property | custom.")] string upperText = "item_number",
        [Description("Custom text, or property name for custom_property.")] string? upperValue = null,
        [Description("View containing the point. Omit to use the view under the point.")] string? view = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        DrawingDimensionSpec.RequireSheetPoint(point, nameof(point));
        DrawingDimensionSpec.RequireSheetPoint(balloonPosition, nameof(balloonPosition));
        var st = BalloonSpec.ParseStyle(style);
        var sz = BalloonSpec.ParseSize(size);
        var upper = BalloonSpec.ParseText(upperText, upperValue, nameof(upperText));

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Balloons");
            var drw = (DrawingDoc)doc;
            var target = DrawingViews.AtPoint(drw, DrawingViews.All(drw), view, point, nameof(point));
            drw.ActivateSheet(target.Sheet);
            drw.ActivateView(target.View.GetName2());

            doc.ClearSelection2(true);
            if (!doc.Extension.SelectByID2(string.Empty, "EDGE", Units.MmToM(point[0]), Units.MmToM(point[1]), 0, false, 0, null, 0))
            {
                throw new McpException($"No edge found at [{string.Join(", ", point)}] mm in view '{target.View.GetName2()}'.");
            }
            BalloonOptions opt = doc.Extension.CreateBalloonOptions();
            opt.Style = (int)st;
            opt.Size = (int)sz;
            opt.UpperTextContent = (int)upper.Content;
            opt.UpperText = upper.Text;
            Note? note = doc.Extension.InsertBOMBalloon2(opt);
            doc.ClearSelection2(true);
            if (note is null)
            {
                throw new McpException("SolidWorks did not create a balloon. The edge must belong to a component of an assembly view.");
            }
            (note.GetAnnotation() as Annotation)?.SetPosition2(Units.MmToM(balloonPosition[0]), Units.MmToM(balloonPosition[1]), 0);
            doc.GraphicsRedraw2();
            return new { view = target.View.GetName2(), upperText = note.GetBomBalloonText(true), style = BalloonSpec.StyleName(note.GetBalloonStyle()) };
        }, ct);
    }
}
