using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class BomTableTools(SolidWorksSession sw, PathGuard paths)
{
    public sealed record BomTableInfo(int Index, string Sheet, string View, string Feature, string Type, string? Configuration, string? Model,
        IReadOnlyList<string> Columns, IReadOnlyList<Dictionary<string, string>> Rows);

    /// <summary>All (sheet, view, BOM table) on every sheet, in sheet order. Call only from the STA thread.</summary>
    private static List<(string Sheet, View View, BomTableAnnotation Bom, TableAnnotation Table)> FindBomTables(DrawingDoc drw)
    {
        var result = new List<(string, View, BomTableAnnotation, TableAnnotation)>();
        if (drw.GetViews() is not object[] sheets)
        {
            return result;
        }
        foreach (object[] sheetViews in sheets.OfType<object[]>())
        {
            if (sheetViews.Length == 0 || sheetViews[0] is not View sheetView)
            {
                continue;
            }
            string sheetName = sheetView.GetName2();
            foreach (View v in sheetViews.OfType<View>())
            {
                foreach (TableAnnotation t in (v.GetTableAnnotations() as object[] ?? Array.Empty<object>()).OfType<TableAnnotation>())
                {
                    if (t.Type == (int)swTableAnnotationType_e.swTableAnnotation_BillOfMaterials && t is BomTableAnnotation bom)
                    {
                        result.Add((sheetName, v, bom, t));
                    }
                }
            }
        }
        return result;
    }

    private static BomTableInfo Describe(int index, string sheet, View view, BomTableAnnotation bom, TableAnnotation t)
    {
        int cols = t.ColumnCount;
        var titles = Enumerable.Range(0, cols).Select(c => (string?)t.GetColumnTitle2(c, false)).ToList();
        var rows = BomTableSpec.ToRows(titles, t.RowCount, (r, c) => t.DisplayedText2[r, c, false],
            t.GetHeaderCount(), (swTableHeaderPosition_e)t.GetHeaderStyle());
        BomFeature? f = bom.BomFeature;
        return new BomTableInfo(index, sheet, view.GetName2(), f?.Name ?? string.Empty, f is null ? "unknown" : BomTableSpec.TypeName(f.TableType),
            f?.Configuration, f?.GetReferencedModelName(), rows.FirstOrDefault()?.Keys.ToList() ?? titles.Select(x => x ?? string.Empty).ToList(), rows);
    }

    [McpServerTool(Name = "sw_insert_bom_table", Title = "Insert drawing BOM table", OpenWorld = false)]
    [Description("Insert a Bill of Materials table into a drawing, linked to an assembly view: top_level, parts_only or indented. Columns come from the BOM template (default: bom-standard from the BOM Templates folder). Returns the table's rows so you can check them.")]
    public Task<BomTableInfo> InsertBomTable(
        [Description("Drawing view to link the BOM to (from sw_list_drawing_sheets). Omit for the first model view on the active sheet.")] string? view = null,
        [Description("top_level | parts_only | indented")] string type = "top_level",
        [Description("Assembly configuration to list. Omit for the view's configuration.")] string? configuration = null,
        [Description("BOM template (.sldbomtbt). Omit for bom-standard (or the first template) in the BOM Templates folder.")] string? templatePath = null,
        [Description("top_left | top_right | bottom_left | bottom_right (attach that table corner to the sheet's BOM anchor) | point (use x/y).")] string anchor = "top_right",
        [Description("X position of the table's top-left corner in mm from the sheet origin (anchor 'point').")] double xMm = 10,
        [Description("Y position in mm (anchor 'point').")] double yMm = 200,
        [Description("Indented BOMs only: item numbering detailed (1, 1.1, 1.2), flat or none.")] string indentedNumbering = "detailed",
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        var bomType = BomTableSpec.ParseType(type);
        var (useAnchor, corner) = BomTableSpec.ParseAnchor(anchor);
        var numbering = BomTableSpec.ParseIndentedNumbering(indentedNumbering);
        string? template = templatePath is null ? null : paths.Check(templatePath, mustExist: true, nameof(templatePath));

        return sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "BOM tables");
            var drw = (DrawingDoc)doc;
            View target = FindView(drw, view);
            if (target.GetReferencedModelName() is not string model || !model.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException($"View '{target.GetName2()}' does not show an assembly. BOM tables need an assembly view.");
            }

            template ??= BomTableSpec.PickTemplate(TemplateFiles(app));
            if (template is null)
            {
                throw new McpException("No BOM template (.sldbomtbt) found in Tools > Options > File Locations > BOM Templates. Pass templatePath.");
            }

            // InsertBomTable4(UseAnchorPoint, X, Y, AnchorType, BomType, Configuration, TableTemplate, Hidden, IndentedNumberingType, DetailedCutList)
            var bom = target.InsertBomTable4(useAnchor, Units.MmToM(xMm), Units.MmToM(yMm), (int)corner, (int)bomType,
                configuration ?? target.ReferencedConfiguration ?? string.Empty, template, false,
                (int)(bomType == swBomType_e.swBomType_Indented ? numbering : swNumberingType_e.swIndentedBOMNotSet), false);
            if (bom is null)
            {
                throw new McpException($"SolidWorks could not insert the BOM table on view '{target.GetName2()}'. Check the template path and the configuration name.");
            }
            doc.GraphicsRedraw2();

            // Report the new table as sw_read_bom_tables would, so its index can be used with sw_export_bom_table.
            string? featureName = bom.BomFeature?.Name;
            var tables = FindBomTables(drw);
            int index = tables.FindIndex(x => x.Bom.BomFeature?.Name == featureName);
            if (index < 0)
            {
                throw new McpException("The BOM table was inserted but could not be read back.");
            }
            var (sheet, v, b, t) = tables[index];
            return Describe(index, sheet, v, b, t);
        }, ct);
    }

    private static IEnumerable<string> TemplateFiles(SldWorks app)
    {
        string setting = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsBOMTemplates) ?? string.Empty;
        foreach (string dir in setting.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Directory.Exists))
        {
            foreach (string f in Directory.EnumerateFiles(dir, "*" + BomTableSpec.TemplateExtension))
            {
                yield return f;
            }
        }
    }

    private static View FindView(DrawingDoc drw, string? name)
    {
        var sheets = (drw.GetViews() as object[] ?? Array.Empty<object>()).OfType<object[]>().ToList();
        IEnumerable<View> modelViews = sheets.SelectMany(s => s.Skip(1).OfType<View>());
        if (!string.IsNullOrWhiteSpace(name))
        {
            return modelViews.FirstOrDefault(v => string.Equals(v.GetName2(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new McpException($"View '{name}' not found. Views: {string.Join(", ", modelViews.Select(v => v.GetName2()))}.");
        }
        string active = (drw.GetCurrentSheet() as Sheet)?.GetName() ?? string.Empty;
        var onActive = sheets.FirstOrDefault(s => s.Length > 0 && s[0] is View sv && sv.GetName2() == active);
        return onActive?.Skip(1).OfType<View>().FirstOrDefault()
            ?? modelViews.FirstOrDefault()
            ?? throw new McpException("The drawing has no model views. Create one with sw_create_drawing.");
    }

    [McpServerTool(Name = "sw_read_bom_tables", Title = "Read drawing BOM tables", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Read every BOM table in a drawing (all sheets) as rows keyed by column title, e.g. {\"ITEM NO.\":\"1\",\"PART NUMBER\":\"LEG-700\",\"QTY.\":\"4\"}. Values are exactly what the table displays.")]
    public Task<IReadOnlyList<BomTableInfo>> ReadBomTables(
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<BomTableInfo>>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "BOM tables");
            return FindBomTables((DrawingDoc)doc).Select((x, i) => Describe(i, x.Sheet, x.View, x.Bom, x.Table)).ToList();
        }, ct);

    [McpServerTool(Name = "sw_export_bom_table", Title = "Export drawing BOM table", Destructive = true, OpenWorld = false)]
    [Description("Save a drawing BOM table as CSV (opens in Excel) or tab-separated text for purchasing, ERP import or logistics. Exports what the table displays.")]
    public Task<string> ExportBomTable(
        [Description("Output path, e.g. C:\\Exports\\LabBench-BOM.csv")] string outputPath,
        [Description("Which BOM table (index from sw_read_bom_tables). Default: the first.")] int index = 0,
        [Description("csv or tsv. Omit to choose from the file extension.")] string? format = null,
        [Description("Include hidden rows and columns.")] bool includeHidden = false,
        [Description("Overwrite the file if it exists.")] bool overwrite = true,
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
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "BOM tables");
            var tables = FindBomTables((DrawingDoc)doc);
            if (tables.Count == 0)
            {
                throw new McpException("The drawing has no BOM table. Insert one with sw_insert_bom_table.");
            }
            if (index < 0 || index >= tables.Count)
            {
                throw new McpException($"index must be 0..{tables.Count - 1}.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var t = tables[index].Table;
            if (!t.SaveAsText2(full, separator, includeHidden) || !File.Exists(full))
            {
                throw new McpException($"SolidWorks could not save the BOM table to '{full}'.");
            }
            return $"Exported BOM table {index} ({t.RowCount} rows incl. header) from {doc.GetTitle()} to {full}";
        }, ct);
    }

    [McpServerTool(Name = "sw_renumber_bom_items", Title = "Renumber BOM items and balloons", Idempotent = true, OpenWorld = false)]
    [Description("Renumber the item numbers of a drawing BOM table; balloons linked to it update to the new numbers. Number sequentially from startNumber, optionally after sorting by a column (e.g. PART NUMBER), or follow the assembly tree order. Returns the renumbered rows.")]
    public Task<BomTableInfo> RenumberBomItems(
        [Description("Which BOM table (index from sw_read_bom_tables). Default: the first.")] int index = 0,
        [Description("First item number.")] int startNumber = 1,
        [Description("Column title to sort rows by before renumbering, e.g. PART NUMBER. Omit to keep the row order.")] string? sortColumn = null,
        [Description("Sort ascending (false = descending).")] bool ascending = true,
        [Description("Number rows in the assembly's FeatureManager order instead (ignores sortColumn).")] bool followAssemblyOrder = false,
        [Description("Path, file name or title of a drawing. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        if (startNumber < 1)
        {
            throw new McpException("startNumber must be 1 or more.");
        }
        if (followAssemblyOrder && sortColumn is not null)
        {
            throw new McpException("Use either sortColumn or followAssemblyOrder, not both.");
        }
        return sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocDRAWING, "BOM tables");
            var tables = FindBomTables((DrawingDoc)doc);
            if (tables.Count == 0)
            {
                throw new McpException("The drawing has no BOM table. Insert one with sw_insert_bom_table.");
            }
            if (index < 0 || index >= tables.Count)
            {
                throw new McpException($"index must be 0..{tables.Count - 1}.");
            }
            var (sheet, view, bom, table) = tables[index];
            BomFeature f = bom.BomFeature ?? throw new McpException("The BOM table has no BOM feature.");
            f.KeepCurrentItemNumbers = false;
            f.SequenceStartNumber = startNumber;
            f.FollowAssemblyOrder2 = followAssemblyOrder;
            if (sortColumn is not null)
            {
                var titles = Enumerable.Range(0, table.ColumnCount).Select(c => (string?)table.GetColumnTitle2(c, false) ?? string.Empty).ToList();
                int col = BomTableSpec.ColumnIndex(titles, sortColumn);
                BomTableSortData sort = bom.GetBomTableSortData() ?? throw new McpException("SolidWorks returned no sort settings for the BOM table.");
                sort.set_ColumnIndex(0, col); // primary sort key; secondary keys keep their saved settings
                sort.set_Ascending(0, ascending);
                sort.DoNotChangeItemNumber = false;
                if (!bom.Sort(sort))
                {
                    throw new McpException($"SolidWorks could not sort the BOM table by '{titles[col]}'.");
                }
            }
            doc.ForceRebuild3(false);
            doc.GraphicsRedraw2();
            var refreshed = FindBomTables((DrawingDoc)doc);
            var t = index < refreshed.Count ? refreshed[index] : tables[index];
            return Describe(index, t.Sheet, t.View, t.Bom, t.Table);
        }, ct);
    }
}
