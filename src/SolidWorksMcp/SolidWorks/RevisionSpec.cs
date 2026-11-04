using System.Globalization;
using ModelContextProtocol;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

/// <summary>Revision table options and revision sequencing. Pure logic, testable without SolidWorks.</summary>
public static class RevisionSpec
{
    public const string TemplateExtension = ".sldrevtbt";

    /// <summary>Revision scheme for the drawing (Document Properties > Tables > Revision), or null to keep it.</summary>
    public static swRevisionTableTagStyle_e? ParseScheme(string? scheme) => (scheme ?? "keep").Trim().ToLowerInvariant() switch
    {
        "keep" or "" => null,
        "alphabetic" or "alpha" or "letters" => swRevisionTableTagStyle_e.swRevisionTable_AlphabeticTags,
        "numeric" or "numbers" => swRevisionTableTagStyle_e.swRevisionTable_NumericTags,
        var s => throw new McpException($"Unknown revision scheme '{s}'. Use keep, alphabetic or numeric."),
    };

    public static swRevisionTableSymbolShape_e? ParseSymbolShape(string? shape) => (shape ?? "keep").Trim().ToLowerInvariant() switch
    {
        "keep" or "" => null,
        "circle" => swRevisionTableSymbolShape_e.swRevisionTable_CircleSymbol,
        "square" => swRevisionTableSymbolShape_e.swRevisionTable_SquareSymbol,
        "triangle" => swRevisionTableSymbolShape_e.swRevisionTable_TriangleSymbol,
        "hexagon" => swRevisionTableSymbolShape_e.swRevisionTable_HexagonSymbol,
        var s => throw new McpException($"Unknown revision symbol shape '{s}'. Use keep, circle, square, triangle or hexagon."),
    };

    /// <summary>Default template: "standard revision block" if present, otherwise the first one alphabetically.</summary>
    public static string? PickTemplate(IEnumerable<string> templateFiles)
    {
        var files = templateFiles.Where(f => f.EndsWith(TemplateExtension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        return files.FirstOrDefault(f => BomBuilder.FileName(f).Equals("standard revision block" + TemplateExtension, StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault();
    }

    /// <summary>
    /// The revision after <paramref name="current"/>: numeric 1 → 2, alphabetic A → B, Z → AA (spreadsheet-style),
    /// skipping any letters in <paramref name="skipLetters"/> (e.g. "IOQSXZ" per ASME Y14.35). With no current revision
    /// the first is "1" or "A" (the first letter not skipped).
    /// </summary>
    public static string Next(string? current, swRevisionTableTagStyle_e scheme, string? skipLetters = null)
    {
        string cur = (current ?? string.Empty).Trim();
        if (scheme == swRevisionTableTagStyle_e.swRevisionTable_NumericTags)
        {
            if (cur.Length == 0) return "1";
            return int.TryParse(cur, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                ? (n + 1).ToString(CultureInfo.InvariantCulture)
                : throw new McpException($"Current revision '{cur}' is not a number; pass revision explicitly.");
        }

        var skip = new HashSet<char>((skipLetters ?? string.Empty).ToUpperInvariant().Where(char.IsAsciiLetterUpper));
        var letters = Enumerable.Range('A', 26).Select(c => (char)c).Where(c => !skip.Contains(c)).ToArray();
        if (letters.Length == 0)
        {
            throw new McpException("skipLetters removes every letter.");
        }
        if (cur.Length == 0) return letters[0].ToString();

        // Treat the revision as a number in base letters.Length, digits = allowed letters, and add one.
        var digits = new List<int>();
        foreach (char ch in cur.ToUpperInvariant())
        {
            int d = Array.IndexOf(letters, ch);
            if (d < 0)
            {
                throw new McpException($"Current revision '{cur}' is not alphabetic in this scheme (or uses a skipped letter); pass revision explicitly.");
            }
            digits.Add(d);
        }
        int i = digits.Count - 1;
        while (i >= 0 && digits[i] == letters.Length - 1)
        {
            digits[i] = 0;
            i--;
        }
        if (i >= 0) digits[i]++;
        else digits.Insert(0, 0);
        return new string(digits.Select(d => letters[d]).ToArray());
    }

    /// <summary>Standard revision table fields mapped to the column titles SolidWorks templates use.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> FieldTitles = new Dictionary<string, string[]>
    {
        ["zone"] = new[] { "ZONE" },
        ["description"] = new[] { "DESCRIPTION", "DESC", "DESC." },
        ["date"] = new[] { "DATE" },
        ["approved"] = new[] { "APPROVED", "APPROVED BY", "APPR", "APPR." },
    };

    /// <summary>
    /// Cell writes for a revision row: named fields first, then free-form <paramref name="values"/> keyed by column title.
    /// Returns (column index, text). Throws if a value has no matching column, so nothing is silently dropped.
    /// </summary>
    public static List<(int Column, string Text)> CellWrites(IReadOnlyList<string> titles, string? zone, string? description, string? date,
        string? approved, IReadOnlyDictionary<string, string>? values)
    {
        static string Norm(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        var norm = titles.Select(Norm).ToList();
        var writes = new Dictionary<int, string>();

        void Put(string field, IEnumerable<string> candidates, string text)
        {
            int col = candidates.Select(Norm).Select(c => norm.IndexOf(c)).FirstOrDefault(i => i >= 0, -1);
            if (col < 0)
            {
                throw new McpException($"The revision table has no column for {field}. Columns: {string.Join(", ", titles.Where(t => t.Length > 0))}.");
            }
            writes[col] = text;
        }

        if (zone is not null) Put("zone", FieldTitles["zone"], zone);
        if (description is not null) Put("description", FieldTitles["description"], description);
        if (date is not null) Put("date", FieldTitles["date"], date);
        if (approved is not null) Put("approved", FieldTitles["approved"], approved);
        foreach (var (title, text) in values ?? new Dictionary<string, string>())
        {
            if (Norm(title) is "REV" or "REV." or "REVISION")
            {
                throw new McpException("Change the revision letter with the revision argument, not the REV column.");
            }
            Put($"'{title}'", new[] { title }, text);
        }
        return writes.Select(kv => (kv.Key, kv.Value)).OrderBy(w => w.Key).ToList();
    }

    /// <summary>Data rows of a table grid (header rows excluded wherever SolidWorks put them).</summary>
    public static IEnumerable<int> DataRows(int rowCount, int headerCount, swTableHeaderPosition_e headerPosition)
    {
        int first = headerPosition == swTableHeaderPosition_e.swTableHeader_Top ? headerCount : 0;
        int last = headerPosition == swTableHeaderPosition_e.swTableHeader_Bottom ? rowCount - headerCount : rowCount;
        return Enumerable.Range(first, Math.Max(0, last - first));
    }
}
