using ModelContextProtocol;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

public sealed record WeldmentProfile(string Standard, string Type, string Size, string Path);

/// <summary>
/// Weldment profile libraries follow SolidWorks' folder convention: &lt;root&gt;\&lt;standard&gt;\&lt;type&gt;\&lt;size&gt;.sldlfp
/// (e.g. ...\weldment profiles\iso\square tube\40 x 40 x 4.sldlfp). Pure file-system logic, testable without SolidWorks.
/// </summary>
public static class WeldmentProfiles
{
    public const string Extension = ".sldlfp";

    /// <summary>The Weldment Profiles file-location setting can list several folders separated by ';'.</summary>
    public static IReadOnlyList<string> SplitRoots(string? setting)
        => (setting ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static List<WeldmentProfile> Scan(IEnumerable<string> roots)
    {
        var result = new List<WeldmentProfile>();
        foreach (string root in roots.Where(Directory.Exists))
        {
            foreach (string standardDir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                foreach (string typeDir in Directory.EnumerateDirectories(standardDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (string file in Directory.EnumerateFiles(typeDir, "*" + Extension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(new WeldmentProfile(
                            System.IO.Path.GetFileName(standardDir),
                            System.IO.Path.GetFileName(typeDir),
                            System.IO.Path.GetFileNameWithoutExtension(file),
                            file));
                    }
                }
            }
        }
        return result;
    }

    /// <summary>Find a profile by standard / type / size (case-insensitive; size spacing is ignored, so "40x40x4" matches "40 x 40 x 4").</summary>
    public static WeldmentProfile Resolve(IReadOnlyList<WeldmentProfile> all, string standard, string type, string size)
    {
        static bool Eq(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        static string Squash(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

        if (all.Count == 0)
        {
            throw new McpException("No weldment profiles found. Check Tools > Options > File Locations > Weldment Profiles in SolidWorks.");
        }
        var byStandard = all.Where(p => Eq(p.Standard, standard)).ToList();
        if (byStandard.Count == 0)
        {
            throw new McpException($"Standard '{standard}' not found. Available: {string.Join(", ", all.Select(p => p.Standard).Distinct(StringComparer.OrdinalIgnoreCase))}.");
        }
        var byType = byStandard.Where(p => Eq(p.Type, type)).ToList();
        if (byType.Count == 0)
        {
            throw new McpException($"Type '{type}' not found in {standard}. Available: {string.Join(", ", byStandard.Select(p => p.Type).Distinct(StringComparer.OrdinalIgnoreCase))}.");
        }
        var match = byType.FirstOrDefault(p => Eq(p.Size, size)) ?? byType.FirstOrDefault(p => Squash(p.Size) == Squash(size));
        return match ?? throw new McpException($"Size '{size}' not found in {standard} / {type}. Available: {string.Join(", ", byType.Select(p => p.Size))}.");
    }

    public static swSolidworksWeldmentEndCondOptions_e ParseCornerTreatment(string? value) => (value ?? "miter").Trim().ToLowerInvariant().Replace('-', '_') switch
    {
        "miter" or "mitre" => swSolidworksWeldmentEndCondOptions_e.swEndConditionMiter,
        "butt1" or "end_butt1" => swSolidworksWeldmentEndCondOptions_e.swEndConditionButt1,
        "butt2" or "end_butt2" => swSolidworksWeldmentEndCondOptions_e.swEndConditionButt2,
        "none" => swSolidworksWeldmentEndCondOptions_e.swEndConditionNone,
        var v => throw new McpException($"Unknown corner treatment '{v}'. Use miter, butt1, butt2 or none."),
    };

    public static swConnectedSegmentsOption_e ParseConnectedSegments(string? value) => (value ?? "simple").Trim().ToLowerInvariant() switch
    {
        "simple" or "simple_cut" => swConnectedSegmentsOption_e.swConnectedSegments_SimpleCut,
        "coped" or "coped_cut" => swConnectedSegmentsOption_e.swConnectedSegments_CopedCut,
        var v => throw new McpException($"Unknown connected-segments option '{v}'. Use simple or coped."),
    };

    /// <summary>Match body names case-insensitively, reporting every missing name at once.</summary>
    public static List<int> MatchBodies(IReadOnlyList<string> available, IReadOnlyList<string> wanted, string argument,
        string kind = "Body", string hint = "see sw_get_weldment_cut_list")
    {
        if (wanted.Count == 0)
        {
            throw new McpException($"'{argument}' must list at least one {kind.ToLowerInvariant()} name ({hint}).");
        }
        var indices = new List<int>();
        var missing = new List<string>();
        foreach (string w in wanted)
        {
            int i = available.ToList().FindIndex(a => string.Equals(a, w.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0) missing.Add(w); else if (!indices.Contains(i)) indices.Add(i);
        }
        if (missing.Count > 0)
        {
            throw new McpException($"{kind} name(s) not found in '{argument}': {string.Join(", ", missing)}. Available: {string.Join(", ", available)}.");
        }
        return indices;
    }

    public static swSolidworksWeldmentEndCondOptions_e ParseTrimType(string? value) => (value ?? "end_trim").Trim().ToLowerInvariant().Replace('-', '_') switch
    {
        "end_trim" or "trim" => swSolidworksWeldmentEndCondOptions_e.swEndConditionTrim,
        "miter" or "mitre" or "end_miter" => swSolidworksWeldmentEndCondOptions_e.swEndConditionMiter,
        "butt1" or "end_butt1" => swSolidworksWeldmentEndCondOptions_e.swEndConditionButt1,
        "butt2" or "end_butt2" => swSolidworksWeldmentEndCondOptions_e.swEndConditionButt2,
        var v => throw new McpException($"Unknown trim type '{v}'. Use end_trim, miter, butt1 or butt2."),
    };

    public static swGussetProfileLocationType_e ParseGussetLocation(string? value) => (value ?? "center").Trim().ToLowerInvariant() switch
    {
        "start" => swGussetProfileLocationType_e.swGussetProfileLocationStart,
        "center" or "centre" or "mid" => swGussetProfileLocationType_e.swGussetProfileLocationCenter,
        "end" => swGussetProfileLocationType_e.swGussetProfileLocationEnd,
        var v => throw new McpException($"Unknown gusset location '{v}'. Use start, center or end."),
    };

    public static swGussetThicknessType_e ParseGussetThickness(string? value) => (value ?? "both").Trim().ToLowerInvariant() switch
    {
        "inner" or "inside" => swGussetThicknessType_e.swGussetThicknessInner,
        "both" or "both_sides" or "mid" => swGussetThicknessType_e.swGussetThicknessBothSides,
        "outer" or "outside" => swGussetThicknessType_e.swGussetThicknessOuter,
        var v => throw new McpException($"Unknown gusset thickness side '{v}'. Use inner, both or outer."),
    };

    public static void RequirePositive(double value, string name)
    {
        if (!(value > 0))
        {
            throw new McpException($"{name} must be > 0.");
        }
    }

    public static void RequirePoints(double[][]? points, int count, string name)
    {
        if (points is null || points.Length != count && count > 0 || points.Length == 0)
        {
            throw new McpException(count > 0 ? $"{name} must contain exactly {count} [x, y, z] points in mm." : $"{name} must contain at least one [x, y, z] point in mm.");
        }
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] is not { Length: 3 })
            {
                throw new McpException($"{name}[{i}] must be [x, y, z] in mm.");
            }
        }
    }
}
