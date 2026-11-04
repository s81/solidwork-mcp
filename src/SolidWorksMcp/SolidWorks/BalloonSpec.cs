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
}
