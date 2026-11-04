using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class RevisionTableTools(SolidWorksSession sw, PathGuard paths)
{
    public sealed record RevisionRow(string Revision, int Id, int SymbolCount, Dictionary<string, string> Cells);

    public sealed record RevisionTableInfo(string Sheet, string CurrentRevision, string Scheme, string? RevisionProperty,
        IReadOnlyList<string> Columns, IReadOnlyList<RevisionRow> Revisions);

    private const int NoOption = (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified;

    private static List<string> SheetNames(DrawingDoc drw) => (drw.GetSheetNames() as string[] ?? Array.Empty<string>()).ToList();

    private static Sheet GetSheet(DrawingDoc drw, string? name)
    {
        if (name is null)
        {
            return drw.GetCurrentSheet() as Sheet ?? throw new McpException("The drawing has no active sheet.");
        }
        var names = SheetNames(drw);
        string match = names.FirstOrDefault(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"Sheet '{name}' not found. Sheets: {string.Join(", ", names)}.");
        return drw.get_Sheet(match) as Sheet ?? throw new McpException($"Sheet '{match}' could not be read.");
    }

    private static RevisionTableAnnotation RequireTable(Sheet sheet)
        => sheet.RevisionTable as RevisionTableAnnotation
           ?? throw new McpException($"Sheet '{sheet.GetName()}' has no revision table. Insert one with sw_insert_revision_table.");

    private static List<string> Titles(TableAnnotation t)
        => Enumerable.Range(0, t.ColumnCount).Select(c => ((string?)t.GetColumnTitle2(c, false) ?? string.Empty).Trim()).ToList();

    private static swRevisionTableTagStyle_e Scheme(ModelDoc2 doc)
        => (swRevisionTableTagStyle_e)doc.Extension.GetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swRevisionTableTagStyle, NoOption);

    private static string? RevisionProperty(SldWorks app, ModelDoc2 doc)
    {
        string name = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDrawingCustomPropertyUsedAsRevision);
        return string.IsNullOrWhiteSpace(name) ? null : PropertyTools.ReadResolved(doc, string.Empty, name);
    }

    private static RevisionTableInfo Describe(SldWorks app, ModelDoc2 doc, Sheet sheet, RevisionTableAnnotation rev)
    {
        var t = (TableAnnotation)rev;
        var titles = Titles(t);
        var keys = BomTableSpec.ColumnKeys(titles);
        var rows = new List<RevisionRow>();
        foreach (int r in RevisionSpec.DataRows(t.RowCount, t.GetHeaderCount(), (swTableHeaderPosition_e)t.GetHeaderStyle()))
        {
            int id = rev.GetIdForRowNumber(r);
            string? letter = id < 0 ? null : rev.GetRevisionForId(id);
            if (string.IsNullOrWhiteSpace(letter))
            {
                continue; // title or spacer row, not a revision
            }
            var cells = new Dictionary<string, string>();
            for (int c = 0; c < keys.Count; c++)
            {
                cells[keys[c]] = ((string?)t.DisplayedText2[r, c, false] ?? string.Empty).Trim();
            }
            rows.Add(new RevisionRow(letter, id, rev.GetRevisionSymbolCount(id), cells));
        }
        return new RevisionTableInfo(sheet.GetName(), rev.CurrentRevision ?? string.Empty,
            Scheme(doc) == swRevisionTableTagStyle_e.swRevisionTable_NumericTags ? "numeric" : "alphabetic",
            RevisionProperty(app, doc), keys, rows);
    }

    private static int FindRevisionId(RevisionTableAnnotation rev, string? revision, string argument)
    {
        var t = (TableAnnotation)rev;
        var ids = RevisionSpec.DataRows(t.RowCount, t.GetHeaderCount(), (swTableHeaderPosition_e)t.GetHeaderStyle())
            .Select(rev.GetIdForRowNumber).Where(id => id >= 0 && !string.IsNullOrWhiteSpace(rev.GetRevisionForId(id))).ToList();
        if (ids.Count == 0)
        {
            throw new McpException("The revision table has no revisions yet.");
        }
        if (revision is null)
        {
            string current = rev.CurrentRevision ?? string.Empty;
            return ids.FirstOrDefault(id => string.Equals(rev.GetRevisionForId(id), current, StringComparison.OrdinalIgnoreCase), ids[^1]);
        }
        return ids.FirstOrDefault(id => string.Equals(rev.GetRevisionForId(id)?.Trim(), revision.Trim(), StringComparison.OrdinalIgnoreCase), -1) is int found and >= 0
            ? found
            : throw new McpException($"{argument}: revision '{revision}' not found. Revisions: {string.Join(", ", ids.Select(rev.GetRevisionForId))}.");
    }

    private static void WriteCells(RevisionTableAnnotation rev, int id, List<(int Column, string Text)> writes)
    {
        var t = (TableAnnotation)rev;
        int row = rev.GetRowNumberForId(id);
        foreach (var (col, text) in writes)
        {
            t.set_Text2(row, col, false, text);
        }
    }

    private static IEnumerable<string> TemplateFiles(SldWorks app)
    {
        string setting = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsRevisionTableTemplates) ?? string.Empty;
        foreach (string dir in setting.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Directory.Exists))
        {
            foreach (string f in Directory.EnumerateFiles(dir, "*" + RevisionSpec.TemplateExtension))
            {
                yield return f;
            }
        }
    }

    [McpServerTool(Name = "sw_insert_revision_table", Title = "Insert revision table", OpenWorld = false)]
    [Description("Insert a revision table on a drawing sheet (default template: 'standard revision block' from the Revision Table Templates folder). Optionally set the drawing's revision scheme (alphabetic A, B, C… or numeric 1, 2, 3…) and revision symbol shape. Add rows with sw_add_revision.")]
    public Task<RevisionTableInfo> InsertRevisionTable(
        [Description("Sheet name. Omit for the active sheet.")] string? sheet = null,
        [Description("Revision table template (.sldrevtbt). Omit for the default in the Revision Table Templates folder.")] string? templatePath = null,
        [Description("top_left | top_right | bottom_left | bottom_right (attach that corner to the sheet format's revision anchor) | point (use x/y).")] string anchor = "top_right",
        [Description("X of the table's top-left corner in mm from the sheet origin (anchor 'point').")] double xMm = 10,
        [Description("Y in mm (anchor 'point').")] double yMm = 200,
        [Description("Revision scheme: keep | alphabetic | numeric (Document Properties > Tables > Revision).")] string scheme = "keep",
        [Description("Revision symbol shape: keep | circle | square | triangle | hexagon.")] string symbolShape = "keep",
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        var (useAnchor, corner) = BomTableSpec.ParseAnchor(anchor);
        var tag = RevisionSpec.ParseScheme(scheme);
        var shape = RevisionSpec.ParseSymbolShape(symbolShape);
        string? template = templatePath is null ? null : paths.Check(templatePath, mustExist: true, nameof(templatePath));

        return sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Revision tables");
            var drw = (DrawingDoc)doc;
            Sheet sh = GetSheet(drw, sheet);
            if (sh.RevisionTable is not null)
            {
                throw new McpException($"Sheet '{sh.GetName()}' already has a revision table. Use sw_read_revision_tables.");
            }
            template ??= RevisionSpec.PickTemplate(TemplateFiles(app))
                ?? throw new McpException("No revision table template (.sldrevtbt) found in Tools > Options > File Locations > Revision Table Templates. Pass templatePath.");
            if (tag is { } tg)
            {
                doc.Extension.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swRevisionTableTagStyle, NoOption, (int)tg);
            }
            int shapeValue = (int)(shape ?? (swRevisionTableSymbolShape_e)doc.Extension.GetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swRevisionTableSymbolShape, NoOption));
            if (shape is not null)
            {
                doc.Extension.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swRevisionTableSymbolShape, NoOption, shapeValue);
            }

            drw.ActivateSheet(sh.GetName());
            RevisionTableAnnotation? rev = sh.InsertRevisionTable2(useAnchor, Units.MmToM(xMm), Units.MmToM(yMm), (int)corner, template, shapeValue, true);
            if (rev is null)
            {
                throw new McpException($"SolidWorks could not insert the revision table on sheet '{sh.GetName()}'. Check the template.");
            }
            doc.GraphicsRedraw2();
            return Describe(app, doc, sh, rev);
        }, ct);
    }

    [McpServerTool(Name = "sw_add_revision", Title = "Add revision", OpenWorld = false)]
    [Description("Add a revision row to a sheet's revision table: the next revision (B after A, 3 after 2) unless you pass one, with description, date, approved-by, zone and any other column. Optionally place revision symbols at sheet positions (mm) for the new revision. Returns the table, the current revision and the drawing's revision property.")]
    public Task<RevisionTableInfo> AddRevision(
        [Description("Description of the change, e.g. 'Leg height 700 → 720 mm'.")] string description,
        [Description("Revision letter/number. Omit for the next one in the drawing's scheme.")] string? revision = null,
        [Description("Date text as it should appear, e.g. 2026-11-04. Omit to keep what SolidWorks fills in.")] string? date = null,
        [Description("Approved by, e.g. initials.")] string? approved = null,
        [Description("Zone, e.g. B3.")] string? zone = null,
        [Description("Other columns by title, e.g. {\"ECO\":\"1234\"}.")] Dictionary<string, string>? values = null,
        [Description("[[x, y], ...] sheet mm where revision symbols for this revision go. Omit for none.")] double[][]? symbolPositions = null,
        [Description("Letters never used for alphabetic revisions, e.g. IOQSXZ (ASME Y14.35). Only applies when revision is omitted.")] string? skipLetters = null,
        [Description("Sheet name. Omit for the active sheet.")] string? sheet = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new McpException("description is required.");
        }
        if (revision is not null && string.IsNullOrWhiteSpace(revision))
        {
            throw new McpException("revision must not be empty; omit it for the next revision.");
        }
        for (int i = 0; i < (symbolPositions?.Length ?? 0); i++)
        {
            DrawingDimensionSpec.RequireSheetPoint(symbolPositions![i], $"symbolPositions[{i}]");
        }

        return sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Revision tables");
            var drw = (DrawingDoc)doc;
            Sheet sh = GetSheet(drw, sheet);
            var rev = RequireTable(sh);
            var writes = RevisionSpec.CellWrites(Titles((TableAnnotation)rev), zone, description, date, approved, values);
            string letter = revision?.Trim() ?? RevisionSpec.Next(rev.CurrentRevision, Scheme(doc), skipLetters);

            drw.ActivateSheet(sh.GetName());
            int id = rev.AddRevision(letter);
            if (id < 0)
            {
                throw new McpException($"SolidWorks did not add revision '{letter}'.");
            }
            WriteCells(rev, id, writes);

            // InsertRevisionSymbol places a symbol for the latest revision.
            int placed = 0;
            foreach (var p in symbolPositions ?? Array.Empty<double[]>())
            {
                if (drw.InsertRevisionSymbol(Units.MmToM(p[0]), Units.MmToM(p[1])) is not null) placed++;
            }
            if (placed < (symbolPositions?.Length ?? 0))
            {
                throw new McpException($"Revision '{letter}' was added, but only {placed} of {symbolPositions!.Length} revision symbols were placed.");
            }
            doc.GraphicsRedraw2();
            return Describe(app, doc, sh, rev);
        }, ct);
    }

    [McpServerTool(Name = "sw_read_revision_tables", Title = "Read revision tables", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Read the revision table of every sheet (or one sheet): each revision with its cells keyed by column title (ZONE, REV, DESCRIPTION, DATE, APPROVED…), revision symbol count, the current revision, the scheme and the drawing's revision property.")]
    public Task<IReadOnlyList<RevisionTableInfo>> ReadRevisionTables(
        [Description("Only this sheet (optional).")] string? sheet = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<RevisionTableInfo>>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Revision tables");
            var drw = (DrawingDoc)doc;
            var sheets = sheet is null ? SheetNames(drw).Select(n => GetSheet(drw, n)) : new[] { GetSheet(drw, sheet) };
            return sheets.Where(s => s.RevisionTable is not null)
                .Select(s => Describe(app, doc, s, (RevisionTableAnnotation)s.RevisionTable)).ToList();
        }, ct);

    [McpServerTool(Name = "sw_export_revision_table", Title = "Export revision table", Destructive = true, OpenWorld = false)]
    [Description("Save a sheet's revision table as CSV (opens in Excel) or tab-separated text, e.g. for a change log or document control system.")]
    public Task<string> ExportRevisionTable(
        [Description("Output path, e.g. C:\\Exports\\LabBench-revisions.csv")] string outputPath,
        [Description("csv or tsv. Omit to choose from the file extension.")] string? format = null,
        [Description("Overwrite the file if it exists.")] bool overwrite = true,
        [Description("Sheet name. Omit for the active sheet.")] string? sheet = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        string full = paths.Check(outputPath, argumentName: nameof(outputPath));
        string separator = BomTableSpec.ParseSeparator(format, full);
        if (File.Exists(full) && !overwrite)
        {
            throw new McpException($"'{full}' exists and overwrite=false.");
        }
        return sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Revision tables");
            Sheet sh = GetSheet((DrawingDoc)doc, sheet);
            var t = (TableAnnotation)RequireTable(sh);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (!t.SaveAsText2(full, separator, false) || !File.Exists(full))
            {
                throw new McpException($"SolidWorks could not save the revision table to '{full}'.");
            }
            return $"Exported the revision table of sheet '{sh.GetName()}' ({t.RowCount} rows incl. header) from {doc.GetTitle()} to {full}";
        }, ct);
    }

    [McpServerTool(Name = "sw_edit_revision", Title = "Edit revision", Idempotent = true, OpenWorld = false)]
    [Description("Change cells of one revision row: description, date, approved-by, zone or other columns by title. Only the values you pass change. The REV letter itself is not editable here (delete and re-add instead).")]
    public Task<RevisionTableInfo> EditRevision(
        [Description("Revision to edit. Omit for the current (latest) revision.")] string? revision = null,
        [Description("New description.")] string? description = null,
        [Description("New date text.")] string? date = null,
        [Description("New approved-by.")] string? approved = null,
        [Description("New zone.")] string? zone = null,
        [Description("Other columns by title, e.g. {\"ECO\":\"1234\"}.")] Dictionary<string, string>? values = null,
        [Description("Sheet name. Omit for the active sheet.")] string? sheet = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        if (description is null && date is null && approved is null && zone is null && values is not { Count: > 0 })
        {
            throw new McpException("Nothing to change: pass description, date, approved, zone or values.");
        }
        return sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Revision tables");
            Sheet sh = GetSheet((DrawingDoc)doc, sheet);
            var rev = RequireTable(sh);
            var writes = RevisionSpec.CellWrites(Titles((TableAnnotation)rev), zone, description, date, approved, values);
            int id = FindRevisionId(rev, revision, nameof(revision));
            WriteCells(rev, id, writes);
            doc.GraphicsRedraw2();
            return Describe(app, doc, sh, rev);
        }, ct);
    }

    [McpServerTool(Name = "sw_delete_revision", Title = "Delete revision", Destructive = true, OpenWorld = false)]
    [Description("Delete a revision row (default: the current, latest revision) and, by default, its revision symbols on the sheet. The current revision then falls back to the previous row.")]
    public Task<RevisionTableInfo> DeleteRevision(
        [Description("Revision to delete. Omit for the current (latest) revision.")] string? revision = null,
        [Description("Also delete the revision symbols linked to it.")] bool deleteSymbols = true,
        [Description("Sheet name. Omit for the active sheet.")] string? sheet = null,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "Revision tables");
            var drw = (DrawingDoc)doc;
            Sheet sh = GetSheet(drw, sheet);
            var rev = RequireTable(sh);
            int id = FindRevisionId(rev, revision, nameof(revision));
            string letter = rev.GetRevisionForId(id);
            drw.ActivateSheet(sh.GetName());
            if (!rev.DeleteRevision(id, deleteSymbols))
            {
                throw new McpException($"SolidWorks could not delete revision '{letter}'.");
            }
            doc.GraphicsRedraw2();
            return Describe(app, doc, sh, rev);
        }, ct);
}
