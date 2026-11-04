using ModelContextProtocol;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

/// <summary>Balloon options. Pure logic, testable without SolidWorks.</summary>
public static class BalloonSpec
{
    public static swBalloonLayoutType_e ParseLayout(string? layout) => (layout ?? "square").Trim().ToLowerInvariant() switch
    {
        "square" => swBalloonLayoutType_e.swDetailingBalloonLayout_Square,
        "circle" or "circular" => swBalloonLayoutType_e.swDetailingBalloonLayout_Circle,
        "top" => swBalloonLayoutType_e.swDetailingBalloonLayout_Top,
        "bottom" => swBalloonLayoutType_e.swDetailingBalloonLayout_Bottom,
        "right" => swBalloonLayoutType_e.swDetailingBalloonLayout_Right,
        "left" => swBalloonLayoutType_e.swDetailingBalloonLayout_Left,
        var l => throw new McpException($"Unknown layout '{l}'. Use square, circle, top, bottom, right or left."),
    };

    private static readonly Dictionary<string, swBalloonStyle_e> Styles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["circular"] = swBalloonStyle_e.swBS_Circular,
        ["split_circle"] = swBalloonStyle_e.swBS_SplitCirc,
        ["triangle"] = swBalloonStyle_e.swBS_Triangle,
        ["hexagon"] = swBalloonStyle_e.swBS_Hexagon,
        ["box"] = swBalloonStyle_e.swBS_Box,
        ["diamond"] = swBalloonStyle_e.swBS_Diamond,
        ["pentagon"] = swBalloonStyle_e.swBS_Pentagon,
        ["square"] = swBalloonStyle_e.swBS_Square,
        ["split_square"] = swBalloonStyle_e.swBS_SplitSquare,
        ["underline"] = swBalloonStyle_e.swBS_Underline,
        ["none"] = swBalloonStyle_e.swBS_None,
    };

    public static swBalloonStyle_e ParseStyle(string? style)
        => Styles.TryGetValue((style ?? "circular").Trim(), out var s) ? s
           : throw new McpException($"Unknown balloon style '{style}'. Use: {string.Join(", ", Styles.Keys)}.");

    public static string StyleName(int style)
        => Styles.FirstOrDefault(kv => (int)kv.Value == style).Key ?? style.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static swBalloonFit_e ParseSize(string? size) => (size ?? "2_chars").Trim().ToLowerInvariant().Replace(' ', '_') switch
    {
        "tightest" or "tight" => swBalloonFit_e.swBF_Tightest,
        "1" or "1_char" => swBalloonFit_e.swBF_1Char,
        "2" or "2_chars" => swBalloonFit_e.swBF_2Chars,
        "3" or "3_chars" => swBalloonFit_e.swBF_3Chars,
        "4" or "4_chars" => swBalloonFit_e.swBF_4Chars,
        "5" or "5_chars" => swBalloonFit_e.swBF_5Chars,
        var s => throw new McpException($"Unknown balloon size '{s}'. Use tightest or 1_char..5_chars."),
    };

    private static readonly Dictionary<string, swBalloonTextContent_e> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["item_number"] = swBalloonTextContent_e.swBalloonTextItemNumber,
        ["quantity"] = swBalloonTextContent_e.swBalloonTextQuantity,
        ["part_number"] = swBalloonTextContent_e.swBalloonTextPartNumberBOM,
        ["file_name"] = swBalloonTextContent_e.swBalloonTextFileName,
        ["component_reference"] = swBalloonTextContent_e.swBalloonTextComponentReference,
        ["custom_property"] = swBalloonTextContent_e.swBalloonTextCustomProperties,
        ["custom"] = swBalloonTextContent_e.swBalloonTextCustom,
    };

    /// <summary>
    /// Text content for the upper or lower half of a balloon. "custom" uses <paramref name="text"/> literally;
    /// "custom_property" uses it as the property name. Lower text only shows on split styles.
    /// </summary>
    public static (swBalloonTextContent_e Content, string Text) ParseText(string? content, string? text, string argument)
    {
        string key = (content ?? "item_number").Trim();
        if (!Texts.TryGetValue(key, out var c))
        {
            throw new McpException($"{argument}: unknown balloon text '{content}'. Use: {string.Join(", ", Texts.Keys)}.");
        }
        if (c is swBalloonTextContent_e.swBalloonTextCustom or swBalloonTextContent_e.swBalloonTextCustomProperties && string.IsNullOrWhiteSpace(text))
        {
            throw new McpException($"{argument}: '{key}' needs the text (or property name) in the matching *Text parameter.");
        }
        return (c, text ?? string.Empty);
    }

    public static bool IsSplit(swBalloonStyle_e style) => style is swBalloonStyle_e.swBS_SplitCirc or swBalloonStyle_e.swBS_SplitSquare;

    public static swStackedBalloonDirection_e ParseStackDirection(string? direction) => (direction ?? "right").Trim().ToLowerInvariant() switch
    {
        "up" => swStackedBalloonDirection_e.swStackedBalloonDir_Up,
        "down" => swStackedBalloonDirection_e.swStackedBalloonDir_Down,
        "left" => swStackedBalloonDirection_e.swStackedBalloonDir_Left,
        "right" => swStackedBalloonDirection_e.swStackedBalloonDir_Right,
        var d => throw new McpException($"Unknown stack direction '{d}'. Use up, down, left or right."),
    };

    /// <summary>How Auto Balloon numbers items when the drawing has no BOM table yet.</summary>
    public static swBalloonItemNumbersOrder_e ParseItemOrder(string? order) => (order ?? "keep").Trim().ToLowerInvariant() switch
    {
        "keep" or "do_not_change" => swBalloonItemNumbersOrder_e.swBalloonItemNumbers_DoNotChangeItemNumbers,
        "assembly_order" or "follow_assembly_order" => swBalloonItemNumbersOrder_e.swBalloonItemNumbers_FollowAssemblyOrder,
        "sequential" or "order_sequentially" => swBalloonItemNumbersOrder_e.swBalloonItemNumbers_OrderSequentially,
        var o => throw new McpException($"Unknown item order '{o}'. Use keep, assembly_order or sequential."),
    };

    public static void RequireItemNumbering(int start, int increment)
    {
        if (start < 1) throw new McpException("itemNumberStart must be 1 or more.");
        if (increment < 1) throw new McpException("itemNumberIncrement must be 1 or more.");
    }

    /// <summary>
    /// Picks balloons (by list position) from one view's balloon upper texts. Exactly one of
    /// <paramref name="itemNumbers"/> (matched against the upper text), <paramref name="indices"/> (from sw_list_balloons)
    /// or <paramref name="all"/> must be used. Every requested item must exist.
    /// </summary>
    public static List<int> Pick(IReadOnlyList<string> upperTexts, string[]? itemNumbers, int[]? indices, bool all)
    {
        int modes = (itemNumbers is { Length: > 0 } ? 1 : 0) + (indices is { Length: > 0 } ? 1 : 0) + (all ? 1 : 0);
        if (modes != 1)
        {
            throw new McpException("Give exactly one of itemNumbers, indices or all=true.");
        }
        if (all)
        {
            return Enumerable.Range(0, upperTexts.Count).ToList();
        }
        var picked = new List<int>();
        if (indices is { Length: > 0 })
        {
            foreach (int i in indices.Distinct())
            {
                if (i < 0 || i >= upperTexts.Count)
                {
                    throw new McpException($"Balloon index {i} is out of range; the view has {upperTexts.Count} balloon(s) (0..{upperTexts.Count - 1}).");
                }
                picked.Add(i);
            }
            return picked;
        }
        foreach (string item in itemNumbers!.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var hits = Enumerable.Range(0, upperTexts.Count).Where(i => string.Equals(upperTexts[i].Trim(), item, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0)
            {
                throw new McpException($"No balloon with text '{item}' in this view. Balloons: {string.Join(", ", upperTexts.Distinct())}.");
            }
            picked.AddRange(hits);
        }
        return picked.Distinct().ToList();
    }

    /// <summary>Evenly spaced attachment positions 0..1 along a magnetic line (a single note goes in the middle).</summary>
    public static double[] LinePositions(int count)
        => count <= 0 ? Array.Empty<double>()
           : count == 1 ? new[] { 0.5 }
           : Enumerable.Range(0, count).Select(i => (double)i / (count - 1)).ToArray();
}
