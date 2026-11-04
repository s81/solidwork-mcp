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
        [Description("Also insert a magnetic line so the balloons stay aligned.")] bool insertMagneticLine = false,
        [Description("Item numbering when the drawing has no BOM table: keep | assembly_order | sequential. With a BOM table the balloons always show its numbers; use sw_renumber_bom_items to change them.")] string itemOrder = "keep",
        [Description("First item number (no BOM table).")] int itemNumberStart = 1,
        [Description("Item number step (no BOM table).")] int itemNumberIncrement = 1,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        var order = BalloonSpec.ParseItemOrder(itemOrder);
        BalloonSpec.RequireItemNumbering(itemNumberStart, itemNumberIncrement);
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
            opt.InsertMagneticLine = insertMagneticLine;
            opt.ItemOrder = (int)order;
            opt.ItemNumberStart = itemNumberStart;
            opt.ItemNumberIncrement = itemNumberIncrement;
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

    public sealed record BalloonInfo(string Sheet, string View, int Index, string UpperText, string LowerText, string Style, bool Stacked, int StackCount, double[]? PositionMm);

    private static double[]? PositionMm(Note n)
        => (n.GetAnnotation() as Annotation)?.GetPosition() is double[] { Length: >= 2 } p
            ? new[] { Units.Round(Units.MToMm(p[0]), 2), Units.Round(Units.MToMm(p[1]), 2) } : null;

    private static int StackCount(Note n)
    {
        try { return n.IsStackedBalloon() && n.GetBalloonStack() is BalloonStack s ? s.Count : 0; }
        catch (System.Runtime.InteropServices.COMException) { return 0; }
    }

    /// <summary>The view's balloons picked by item number, index (from sw_list_balloons) or all.</summary>
    private static List<Note> Pick(View v, string[]? itemNumbers, int[]? indices, bool all)
    {
        var balloons = Balloons(v);
        if (balloons.Count == 0)
        {
            throw new McpException($"View '{v.GetName2()}' has no BOM balloons.");
        }
        return BalloonSpec.Pick(balloons.Select(n => n.GetBomBalloonText(true) ?? string.Empty).ToList(), itemNumbers, indices, all)
            .Select(i => balloons[i]).ToList();
    }

    [McpServerTool(Name = "sw_list_balloons", Title = "List balloons", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the BOM balloons in a drawing per sheet and view: index (for sw_edit_balloons / sw_delete_balloons / sw_add_magnetic_line), upper/lower text (e.g. item numbers), style, stack size and position (sheet mm). Compare with sw_read_bom_tables to find components without a balloon.")]
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
                var balloons = Balloons(v.View);
                for (int i = 0; i < balloons.Count; i++)
                {
                    Note n = balloons[i];
                    int stack = StackCount(n);
                    result.Add(new BalloonInfo(v.Sheet, v.View.GetName2(), i,
                        n.GetBomBalloonText(true) ?? string.Empty, n.GetBomBalloonText(false) ?? string.Empty,
                        BalloonSpec.StyleName(n.GetBalloonStyle()), stack > 0, stack, PositionMm(n)));
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

    [McpServerTool(Name = "sw_add_stacked_balloon", Title = "Add stacked balloon", OpenWorld = false)]
    [Description("Add a stacked balloon: one leader with a stack of BOM balloons, e.g. for a bolt, washer and nut. Pick one edge per component at sheet positions (mm); the first point gets the leader. Returns how many balloons the stack holds so you can check every component was included.")]
    public Task<object> AddStackedBalloon(
        [Description("[[x, y], ...] in sheet mm, one point on a visible edge of each component. The leader attaches at the first point.")] double[][] points,
        [Description("[x, y] in sheet mm where the stack goes.")] double[] balloonPosition,
        [Description("Stack direction: up | down | left | right.")] string direction = "right",
        [Description("Balloons per row before the stack wraps.")] int balloonsPerLine = 5,
        [Description("Balloon shape (see sw_auto_balloon).")] string style = "circular",
        [Description("Balloon size: tightest or 1_char..5_chars.")] string size = "2_chars",
        [Description("Upper text: item_number | quantity | part_number | file_name | component_reference | custom_property | custom.")] string upperText = "item_number",
        [Description("Custom text, or property name for custom_property.")] string? upperValue = null,
        [Description("View containing the points. Omit to use the view under the first point.")] string? view = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        if (points is not { Length: >= 2 })
        {
            throw new McpException("points needs at least 2 component points for a stack; use sw_add_balloon for one.");
        }
        for (int i = 0; i < points.Length; i++)
        {
            DrawingDimensionSpec.RequireSheetPoint(points[i], $"points[{i}]");
        }
        DrawingDimensionSpec.RequireSheetPoint(balloonPosition, nameof(balloonPosition));
        if (balloonsPerLine < 1)
        {
            throw new McpException("balloonsPerLine must be 1 or more.");
        }
        var dir = BalloonSpec.ParseStackDirection(direction);
        var st = BalloonSpec.ParseStyle(style);
        var sz = BalloonSpec.ParseSize(size);
        var upper = BalloonSpec.ParseText(upperText, upperValue, nameof(upperText));

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Balloons");
            var drw = (DrawingDoc)doc;
            var target = DrawingViews.AtPoint(drw, DrawingViews.All(drw), view, points[0], "points[0]");
            drw.ActivateSheet(target.Sheet);
            drw.ActivateView(target.View.GetName2());

            // InsertStackedBalloon2 balloons the selected items: the first selection gets the leader.
            doc.ClearSelection2(true);
            for (int i = 0; i < points.Length; i++)
            {
                if (!doc.Extension.SelectByID2(string.Empty, "EDGE", Units.MmToM(points[i][0]), Units.MmToM(points[i][1]), 0, i > 0, 0, null, 0))
                {
                    doc.ClearSelection2(true);
                    throw new McpException($"No edge found at points[{i}] [{string.Join(", ", points[i])}] mm in view '{target.View.GetName2()}'.");
                }
            }
            StackedBalloonOptions opt = doc.Extension.CreateStackedBalloonOptions();
            opt.Style = (int)st;
            opt.Size = (int)sz;
            opt.UpperTextContent = (int)upper.Content;
            opt.UpperText = upper.Text;
            opt.StackDirection = (int)dir;
            opt.BalloonsPerLine = balloonsPerLine;
            Note? master = doc.Extension.InsertStackedBalloon2(opt);
            doc.ClearSelection2(true);
            if (master is null)
            {
                throw new McpException("SolidWorks did not create a stacked balloon. Each point must be on an edge of a component in an assembly view.");
            }
            (master.GetAnnotation() as Annotation)?.SetPosition2(Units.MmToM(balloonPosition[0]), Units.MmToM(balloonPosition[1]), 0);
            doc.GraphicsRedraw2();

            var texts = new List<string>();
            if (master.GetBalloonStack() is BalloonStack stack && stack.Stack is object[] notes)
            {
                texts.AddRange(notes.OfType<Note>().Select(n => n.GetBomBalloonText(true) ?? string.Empty));
            }
            if (texts.Count == 0)
            {
                texts.Add(master.GetBomBalloonText(true) ?? string.Empty);
            }
            return new
            {
                view = target.View.GetName2(),
                balloons = texts,
                requested = points.Length,
                warning = texts.Count < points.Length
                    ? $"The stack has {texts.Count} balloon(s) for {points.Length} points: points on the same component share one balloon, or a point missed its component. Check with sw_list_balloons."
                    : null,
            };
        }, ct);
    }

    [McpServerTool(Name = "sw_edit_balloons", Title = "Edit balloons", Idempotent = true, OpenWorld = false)]
    [Description("Change existing BOM balloons in one view: style, size, upper/lower text and (for a single balloon) position. Pick balloons by item number (upper text), index from sw_list_balloons, or all. Only the options you pass change.")]
    public Task<object> EditBalloons(
        [Description("View holding the balloons (from sw_list_balloons).")] string view,
        [Description("Balloons whose upper text is one of these, e.g. [\"3\", \"5\"].")] string[]? itemNumbers = null,
        [Description("Balloon indices in the view (from sw_list_balloons).")] int[]? indices = null,
        [Description("Edit every balloon in the view.")] bool all = false,
        [Description("New shape (see sw_auto_balloon).")] string? style = null,
        [Description("New size: tightest or 1_char..5_chars.")] string? size = null,
        [Description("New upper text: item_number | quantity | part_number | file_name | component_reference | custom_property | custom.")] string? upperText = null,
        [Description("Custom text, or property name for custom_property (upper).")] string? upperValue = null,
        [Description("New lower text (split styles only), same choices.")] string? lowerText = null,
        [Description("Custom text or property name (lower).")] string? lowerValue = null,
        [Description("[x, y] in sheet mm to move the balloon to (one balloon only).")] double[]? position = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        swBalloonStyle_e? st = style is null ? null : BalloonSpec.ParseStyle(style);
        swBalloonFit_e? sz = size is null ? null : BalloonSpec.ParseSize(size);
        var upper = upperText is null ? ((swBalloonTextContent_e, string)?)null : BalloonSpec.ParseText(upperText, upperValue, nameof(upperText));
        var lower = lowerText is null ? ((swBalloonTextContent_e, string)?)null : BalloonSpec.ParseText(lowerText, lowerValue, nameof(lowerText));
        if (position is not null)
        {
            DrawingDimensionSpec.RequireSheetPoint(position, nameof(position));
        }
        if (st is null && sz is null && upper is null && lower is null && position is null)
        {
            throw new McpException("Nothing to change: pass style, size, upperText, lowerText or position.");
        }

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Balloons");
            var drw = (DrawingDoc)doc;
            var target = DrawingViews.Find(DrawingViews.All(drw), view);
            var picked = Pick(target.View, itemNumbers, indices, all);
            if (position is not null && picked.Count != 1)
            {
                throw new McpException($"position moves one balloon, but {picked.Count} are picked.");
            }
            var newStyle = st ?? (swBalloonStyle_e)picked[0].GetBalloonStyle();
            if (lower is not null && !BalloonSpec.IsSplit(newStyle) && picked.Any(n => !BalloonSpec.IsSplit(st ?? (swBalloonStyle_e)n.GetBalloonStyle())))
            {
                throw new McpException("lowerText only shows on split styles; also pass style split_circle or split_square.");
            }
            drw.ActivateSheet(target.Sheet);

            int failed = 0;
            foreach (Note n in picked)
            {
                bool ok = true;
                if (st is not null || sz is not null)
                {
                    ok &= n.SetBalloon((int)(st ?? (swBalloonStyle_e)n.GetBalloonStyle()), (int)(sz ?? (swBalloonFit_e)n.GetBalloonSize()));
                }
                if (upper is not null || lower is not null)
                {
                    var u = upper ?? ((swBalloonTextContent_e)n.GetBomBalloonTextStyle(true), n.GetBomBalloonText(true) ?? string.Empty);
                    var l = lower ?? ((swBalloonTextContent_e)n.GetBomBalloonTextStyle(false), n.GetBomBalloonText(false) ?? string.Empty);
                    ok &= n.SetBomBalloonText((int)u.Item1, u.Item2, (int)l.Item1, l.Item2);
                }
                if (position is not null)
                {
                    ok &= (n.GetAnnotation() as Annotation)?.SetPosition2(Units.MmToM(position[0]), Units.MmToM(position[1]), 0) == true;
                }
                if (!ok) failed++;
            }
            doc.GraphicsRedraw2();
            var balloons = picked.Select(n => new { upperText = n.GetBomBalloonText(true), lowerText = n.GetBomBalloonText(false), style = BalloonSpec.StyleName(n.GetBalloonStyle()), positionMm = PositionMm(n) }).ToList();
            if (failed == picked.Count)
            {
                throw new McpException("SolidWorks rejected every change. Check the style/text combination.");
            }
            return new { view = target.View.GetName2(), edited = picked.Count - failed, failed, balloons };
        }, ct);
    }

    [McpServerTool(Name = "sw_delete_balloons", Title = "Delete balloons", Destructive = true, OpenWorld = false)]
    [Description("Delete BOM balloons from one view, picked by item number (upper text), index from sw_list_balloons, or all. Deleting a stacked balloon removes its whole stack. The BOM table is not changed.")]
    public Task<object> DeleteBalloons(
        [Description("View holding the balloons (from sw_list_balloons).")] string view,
        [Description("Balloons whose upper text is one of these.")] string[]? itemNumbers = null,
        [Description("Balloon indices in the view (from sw_list_balloons).")] int[]? indices = null,
        [Description("Delete every balloon in the view.")] bool all = false,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Balloons");
            var drw = (DrawingDoc)doc;
            var target = DrawingViews.Find(DrawingViews.All(drw), view);
            var picked = Pick(target.View, itemNumbers, indices, all);
            int before = Balloons(target.View).Count;
            drw.ActivateSheet(target.Sheet);
            doc.ClearSelection2(true);
            int selected = picked.Count(n => (n.GetAnnotation() as Annotation)?.Select3(true, null) == true);
            if (selected == 0)
            {
                throw new McpException("Could not select the balloons.");
            }
            doc.Extension.DeleteSelection2(0);
            doc.ClearSelection2(true);
            doc.GraphicsRedraw2();
            int after = Balloons(target.View).Count;
            return new { view = target.View.GetName2(), deleted = before - after, remaining = after };
        }, ct);

    [McpServerTool(Name = "sw_add_magnetic_line", Title = "Add magnetic line", OpenWorld = false)]
    [Description("Insert a magnetic line on a sheet (start/end in sheet mm) and snap balloons to it so they stay aligned, evenly spaced by default. Pick balloons from one view by item number, index or all; omit them to insert an empty line.")]
    public Task<object> AddMagneticLine(
        [Description("[x, y] in sheet mm.")] double[] start,
        [Description("[x, y] in sheet mm.")] double[] end,
        [Description("View whose balloons attach to the line (required when picking balloons).")] string? view = null,
        [Description("Balloons whose upper text is one of these.")] string[]? itemNumbers = null,
        [Description("Balloon indices in the view (from sw_list_balloons).")] int[]? indices = null,
        [Description("Attach every balloon in the view.")] bool all = false,
        [Description("Space balloons evenly along the line.")] bool equalSpacing = true,
        [Description("Sheet name. Omit for the view's sheet, or the active sheet.")] string? sheet = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        DrawingDimensionSpec.RequireSheetPoint(start, nameof(start));
        DrawingDimensionSpec.RequireSheetPoint(end, nameof(end));
        if (Math.Abs(start[0] - end[0]) < 1e-6 && Math.Abs(start[1] - end[1]) < 1e-6)
        {
            throw new McpException("start and end must be different points.");
        }
        bool attach = all || itemNumbers is { Length: > 0 } || indices is { Length: > 0 };
        if (attach && string.IsNullOrWhiteSpace(view))
        {
            throw new McpException("Pass view to pick balloons for the magnetic line.");
        }

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Magnetic lines");
            var drw = (DrawingDoc)doc;
            ViewRef? target = view is null ? null : DrawingViews.Find(DrawingViews.All(drw), view);
            var picked = attach ? Pick(target!.View, itemNumbers, indices, all) : new List<Note>();
            string sheetName = sheet?.Trim() ?? target?.Sheet ?? (drw.GetCurrentSheet() as Sheet)?.GetName() ?? string.Empty;
            if (target is not null && !string.Equals(sheetName, target.Sheet, StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException($"View '{target.View.GetName2()}' is on sheet '{target.Sheet}', not '{sheetName}'.");
            }
            if (!drw.ActivateSheet(sheetName) || drw.GetCurrentSheet() is not Sheet sh)
            {
                throw new McpException($"Sheet '{sheetName}' not found.");
            }

            var math = (MathUtility)app.GetMathUtility();
            var p0 = (MathPoint)math.CreatePoint(new[] { Units.MmToM(start[0]), Units.MmToM(start[1]), 0.0 });
            var p1 = (MathPoint)math.CreatePoint(new[] { Units.MmToM(end[0]), Units.MmToM(end[1]), 0.0 });
            MagneticLine? line = sh.InsertMagneticLine(p0, p1);
            if (line is null)
            {
                throw new McpException("SolidWorks did not insert the magnetic line.");
            }
            line.EqualSpacing = equalSpacing;
            var pos = BalloonSpec.LinePositions(picked.Count);
            int attached = 0;
            for (int i = 0; i < picked.Count; i++)
            {
                if (line.AddNote(picked[i], pos[i])) attached++;
            }
            doc.GraphicsRedraw2();
            return new
            {
                sheet = sh.GetName(),
                lengthMm = Units.Round(Units.MToMm(line.Length), 2),
                attached,
                requested = picked.Count,
                balloons = picked.Select(n => new { upperText = n.GetBomBalloonText(true), positionMm = PositionMm(n) }).ToList(),
            };
        }, ct);
    }
}
