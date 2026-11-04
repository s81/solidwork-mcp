using ModelContextProtocol;
using SolidWorks.Interop.sldworks;
using SolidWorksMcp.Infrastructure;

namespace SolidWorksMcp.SolidWorks;

public sealed record ViewRef(string Sheet, View View, bool IsSheet);

/// <summary>Drawing view lookup and selection shared by drawing tools. Call only from the STA thread.</summary>
public static class DrawingViews
{
    /// <summary>Every sheet view (IsSheet = true) followed by its model views, in sheet order.</summary>
    public static List<ViewRef> All(DrawingDoc drw)
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

    public static ViewRef Find(List<ViewRef> views, string name)
        => views.FirstOrDefault(v => !v.IsSheet && string.Equals(v.View.GetName2(), name.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? throw new McpException($"View '{name}' not found. Views: {string.Join(", ", views.Where(v => !v.IsSheet).Select(v => v.View.GetName2()))}.");

    public static void Select(ModelDoc2 doc, DrawingDoc drw, ViewRef v, bool append)
    {
        drw.ActivateSheet(v.Sheet);
        if (!doc.Extension.SelectByID2(v.View.GetName2(), "DRAWINGVIEW", 0, 0, 0, append, 0, null, 0))
        {
            throw new McpException($"Could not select view '{v.View.GetName2()}'.");
        }
    }

    /// <summary>The named view, or the view on the active sheet whose outline contains the sheet point (mm).</summary>
    public static ViewRef AtPoint(DrawingDoc drw, List<ViewRef> all, string? name, double[] pointMm, string pointName)
    {
        if (name is not null)
        {
            return Find(all, name);
        }
        string active = (drw.GetCurrentSheet() as Sheet)?.GetName() ?? string.Empty;
        var candidates = all.Where(v => !v.IsSheet && v.Sheet == active).ToList();
        var outlines = candidates.Select(v => (v.View.GetOutline() as double[] ?? Array.Empty<double>()).Select(Units.MToMm).ToArray()).ToList();
        int idx = DrawingDimensionSpec.ViewAt(outlines, pointMm);
        return idx >= 0 ? candidates[idx]
            : throw new McpException($"No view on the active sheet '{active}' contains {pointName} [{string.Join(", ", pointMm)}] mm. Pass 'view' or check the coordinates.");
    }
}
