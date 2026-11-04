using ModelContextProtocol;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

/// <summary>Drawing dimension options. Pure logic, testable without SolidWorks.</summary>
public static class DrawingDimensionSpec
{
    /// <summary>Model item types for Insert Model Items, by friendly name.</summary>
    public static readonly IReadOnlyDictionary<string, swInsertAnnotation_e> ItemTypes = new Dictionary<string, swInsertAnnotation_e>(StringComparer.OrdinalIgnoreCase)
    {
        ["marked_dimensions"] = swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing,
        ["unmarked_dimensions"] = swInsertAnnotation_e.swInsertDimensionsNotMarkedForDrawing,
        ["hole_wizard_locations"] = swInsertAnnotation_e.swInsertHoleWizardLocationDimensions,
        ["hole_wizard_profiles"] = swInsertAnnotation_e.swInsertHoleWizardProfileDimensions,
        ["hole_callouts"] = swInsertAnnotation_e.swInsertholeCallout,
        ["toleranced_dimensions"] = swInsertAnnotation_e.swInsertTolerancedDims,
        ["instance_counts"] = swInsertAnnotation_e.swInsertInstanceCounts,
        ["cosmetic_threads"] = swInsertAnnotation_e.swInsertCThreads,
        ["notes"] = swInsertAnnotation_e.swInsertNotes,
        ["datums"] = swInsertAnnotation_e.swInsertDatums,
        ["gtols"] = swInsertAnnotation_e.swInsertGTols,
        ["surface_finish"] = swInsertAnnotation_e.swInsertSFSymbols,
    };

    public static int ParseItemTypes(IReadOnlyList<string>? types)
    {
        var list = types is { Count: > 0 } ? types : new[] { "marked_dimensions" };
        int mask = 0;
        foreach (string t in list)
        {
            if (!ItemTypes.TryGetValue(t.Trim(), out var flag))
            {
                throw new McpException($"Unknown model item type '{t}'. Use: {string.Join(", ", ItemTypes.Keys)}.");
            }
            mask |= (int)flag;
        }
        return mask;
    }

    public static swAutodimScheme_e ParseScheme(string? scheme, string argument) => (scheme ?? "baseline").Trim().ToLowerInvariant() switch
    {
        "baseline" => swAutodimScheme_e.swAutodimSchemeBaseline,
        "ordinate" => swAutodimScheme_e.swAutodimSchemeOrdinate,
        "chain" => swAutodimScheme_e.swAutodimSchemeChain,
        "centerline" => swAutodimScheme_e.swAutodimSchemeCenterline,
        var s => throw new McpException($"{argument}: unknown scheme '{s}'. Use baseline, ordinate, chain or centerline."),
    };

    public static swAutodimHorizontalPlacement_e ParseHorizontalPlacement(string? p) => (p ?? "below").Trim().ToLowerInvariant() switch
    {
        "above" => swAutodimHorizontalPlacement_e.swAutodimHorizontalPlacementAbove,
        "below" => swAutodimHorizontalPlacement_e.swAutodimHorizontalPlacementBelow,
        var v => throw new McpException($"horizontalPlacement must be above or below, not '{v}'."),
    };

    public static swAutodimVerticalPlacement_e ParseVerticalPlacement(string? p) => (p ?? "left").Trim().ToLowerInvariant() switch
    {
        "left" => swAutodimVerticalPlacement_e.swAutodimVerticalPlacementLeft,
        "right" => swAutodimVerticalPlacement_e.swAutodimVerticalPlacementRight,
        var v => throw new McpException($"verticalPlacement must be left or right, not '{v}'."),
    };

    public enum Orientation { Smart, Horizontal, Vertical }

    public static Orientation ParseOrientation(string? o) => (o ?? "smart").Trim().ToLowerInvariant() switch
    {
        "smart" or "aligned" or "auto" => Orientation.Smart,
        "horizontal" => Orientation.Horizontal,
        "vertical" => Orientation.Vertical,
        var v => throw new McpException($"orientation must be smart, horizontal or vertical, not '{v}'."),
    };

    public static string PickType(string? entity) => (entity ?? "edge").Trim().ToLowerInvariant() switch
    {
        "edge" => "EDGE",
        "vertex" => "VERTEX",
        "sketch_segment" or "sketchsegment" => "SKETCHSEGMENT",
        "sketch_point" or "sketchpoint" => "SKETCHPOINT",
        var e => throw new McpException($"Unknown entity '{e}'. Use edge, vertex, sketch_segment or sketch_point."),
    };

    /// <summary>Sheet points are [x, y] in mm from the sheet's lower-left corner.</summary>
    public static void RequireSheetPoint(double[]? p, string argument)
    {
        if (p is not { Length: 2 })
        {
            throw new McpException($"{argument} must be [x, y] in mm on the sheet (origin at the lower-left corner).");
        }
        if (p.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < -10000 || v > 10000))
        {
            throw new McpException($"{argument} [{string.Join(", ", p)}] is not a plausible sheet position in mm.");
        }
    }

    public static string DimensionTypeName(int type) => Enum.IsDefined(typeof(swDimensionType_e), type)
        ? ((swDimensionType_e)type).ToString().Replace("sw", string.Empty).Replace("Dimension", string.Empty).ToLowerInvariant() switch
        {
            "dimensiontypeunknown" or "typeunknown" => "unknown",
            var n => n,
        }
        : "unknown";

    public static string ToleranceName(int type) => Enum.IsDefined(typeof(swTolType_e), type)
        ? ((swTolType_e)type).ToString().Replace("swTol", string.Empty).ToLowerInvariant()
        : "unknown";

    public static string DrivenStateName(int state) => state switch
    {
        (int)swDimensionDrivenState_e.swDimensionDriven => "driven",
        (int)swDimensionDrivenState_e.swDimensionDriving => "driving",
        _ => "unknown",
    };

    /// <summary>
    /// Index of the view whose outline [xmin, ymin, xmax, ymax] (mm) contains the point; the smallest one wins when views
    /// overlap (e.g. a detail view drawn over its parent). -1 when no view contains it.
    /// </summary>
    public static int ViewAt(IReadOnlyList<double[]> outlines, double[] pointMm)
    {
        int best = -1;
        double bestArea = double.MaxValue;
        for (int i = 0; i < outlines.Count; i++)
        {
            double[] o = outlines[i];
            if (o.Length < 4)
            {
                continue;
            }
            if (pointMm[0] >= o[0] && pointMm[0] <= o[2] && pointMm[1] >= o[1] && pointMm[1] <= o[3])
            {
                double area = (o[2] - o[0]) * (o[3] - o[1]);
                if (area < bestArea)
                {
                    best = i;
                    bestArea = area;
                }
            }
        }
        return best;
    }
}
