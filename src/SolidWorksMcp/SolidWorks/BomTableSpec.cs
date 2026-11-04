using ModelContextProtocol;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

/// <summary>Drawing BOM table options and grid-to-rows conversion. Pure logic, testable without SolidWorks.</summary>
public static class BomTableSpec
{
    public const string TemplateExtension = ".sldbomtbt";

    public static swBomType_e ParseType(string? type) => (type ?? "top_level").Trim().ToLowerInvariant().Replace('-', '_') switch
    {
        "top_level" or "top_level_only" => swBomType_e.swBomType_TopLevelOnly,
        "parts_only" => swBomType_e.swBomType_PartsOnly,
        "indented" => swBomType_e.swBomType_Indented,
        var t => throw new McpException($"Unknown BOM type '{t}'. Use top_level, parts_only or indented."),
    };

    public static string TypeName(int type) => type switch
    {
        (int)swBomType_e.swBomType_TopLevelOnly => "top_level",
        (int)swBomType_e.swBomType_PartsOnly => "parts_only",
        (int)swBomType_e.swBomType_Indented => "indented",
        _ => type.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>"point" = place the table's top-left corner at x/y; the others attach that corner of the table to the sheet's BOM anchor.</summary>
    public static (bool UseAnchor, swBOMConfigurationAnchorType_e Corner) ParseAnchor(string? anchor) => (anchor ?? "top_right").Trim().ToLowerInvariant().Replace('-', '_') switch
    {
        "top_left" => (true, swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft),
        "top_right" => (true, swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopRight),
        "bottom_left" => (true, swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_BottomLeft),
        "bottom_right" => (true, swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_BottomRight),
        "point" => (false, swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft),
        var a => throw new McpException($"Unknown anchor '{a}'. Use top_left, top_right, bottom_left, bottom_right or point."),
    };

    public static swNumberingType_e ParseIndentedNumbering(string? numbering) => (numbering ?? "detailed").Trim().ToLowerInvariant() switch
    {
        "detailed" => swNumberingType_e.swNumberingType_Detailed,
        "flat" => swNumberingType_e.swNumberingType_Flat,
        "none" => swNumberingType_e.swNumberingType_None,
        var n => throw new McpException($"Unknown numbering '{n}'. Use detailed, flat or none."),
    };

    /// <summary>Prefer SolidWorks' standard template, then any template, from the BOM template folder(s).</summary>
    public static string? PickTemplate(IEnumerable<string> templateFiles)
    {
        var files = templateFiles.Where(f => f.EndsWith(TemplateExtension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        return files.FirstOrDefault(f => BomBuilder.FileName(f).Equals("bom-standard" + TemplateExtension, StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault();
    }

    public static string ParseSeparator(string? format, string path) => (format ?? Path.GetExtension(path).TrimStart('.')).Trim().ToLowerInvariant() switch
    {
        "csv" => ",",
        "tsv" or "txt" or "tab" => "\t",
        var f => throw new McpException($"Unsupported export format '{f}'. Use csv or tsv (a .csv opens directly in Excel)."),
    };

    /// <summary>
    /// Turn a table grid into records keyed by column title. Header rows are skipped wherever SolidWorks put them
    /// (top or bottom). Empty titles become "Column N" and duplicate titles get a numeric suffix so no value is lost.
    /// </summary>
    public static List<Dictionary<string, string>> ToRows(IReadOnlyList<string?> titles, int rowCount, Func<int, int, string?> cell,
        int headerCount, swTableHeaderPosition_e headerPosition)
    {
        var keys = new List<string>();
        for (int c = 0; c < titles.Count; c++)
        {
            string key = string.IsNullOrWhiteSpace(titles[c]) ? $"Column {c + 1}" : titles[c]!.Trim();
            string unique = key;
            for (int n = 2; keys.Contains(unique, StringComparer.OrdinalIgnoreCase); n++)
            {
                unique = $"{key}_{n}";
            }
            keys.Add(unique);
        }

        int first = headerPosition == swTableHeaderPosition_e.swTableHeader_Top ? headerCount : 0;
        int last = headerPosition == swTableHeaderPosition_e.swTableHeader_Bottom ? rowCount - headerCount : rowCount;

        var rows = new List<Dictionary<string, string>>();
        for (int r = first; r < last; r++)
        {
            var row = new Dictionary<string, string>();
            bool any = false;
            for (int c = 0; c < keys.Count; c++)
            {
                string value = (cell(r, c) ?? string.Empty).Trim();
                row[keys[c]] = value;
                any |= value.Length > 0;
            }
            if (any)
            {
                rows.Add(row);
            }
        }
        return rows;
    }

    /// <summary>Index of the column whose title matches (case- and whitespace-insensitive).</summary>
    public static int ColumnIndex(IReadOnlyList<string> titles, string column)
    {
        static string Norm(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        string want = Norm(column);
        for (int i = 0; i < titles.Count; i++)
        {
            if (Norm(titles[i]) == want) return i;
        }
        throw new McpException($"No BOM column '{column}'. Columns: {string.Join(", ", titles.Where(t => t.Length > 0))}.");
    }
}
