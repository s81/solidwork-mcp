using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class AssemblyTools(SolidWorksSession sw, PathGuard paths)
{
    [McpServerTool(Name = "sw_get_component_tree", Title = "Assembly component tree", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Return the assembly's component hierarchy (instance names, file paths, referenced configurations, suppression state).")]
    public Task<IReadOnlyList<ComponentNode>> GetComponentTree(
        [Description("Path, file name or title of an assembly. Omit for the active document.")] string? document = null,
        [Description("Maximum depth (1 = top level only).")] int maxDepth = 10,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<ComponentNode>>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocASSEMBLY, "Component tree");
            return Snapshot((AssemblyDoc)doc, Math.Max(1, maxDepth), Array.Empty<string>(), resolveLightweight: false);
        }, ct);

    [McpServerTool(Name = "sw_get_bom", Title = "Bill of materials", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Build a bill of materials from the assembly structure with quantities and selected custom properties (e.g. PartNo, Description, Material). Independent of any drawing BOM table.")]
    public Task<IReadOnlyList<BomRow>> GetBom(
        [Description("Path, file name or title of an assembly. Omit for the active document.")] string? document = null,
        [Description("top_level (sub-assemblies as single rows) or parts_only (all parts at any depth, total quantities).")] string mode = "parts_only",
        [Description("Custom property names to include per row; configuration-specific value wins over file-level.")] string[]? properties = null,
        [Description("Resolve lightweight components so their properties can be read (slower on large assemblies).")] bool resolveLightweight = true,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<BomRow>>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocASSEMBLY, "BOM");
            var props = properties ?? new[] { "PartNo", "Description", "Material" };
            var tree = Snapshot((AssemblyDoc)doc, maxDepth: 64, props, resolveLightweight);
            try
            {
                return BomBuilder.Build(tree, mode);
            }
            catch (ArgumentException ex)
            {
                throw new McpException(ex.Message);
            }
        }, ct);

    internal static IReadOnlyList<ComponentNode> Snapshot(AssemblyDoc assy, int maxDepth, IReadOnlyCollection<string> props, bool resolveLightweight)
    {
        if (resolveLightweight)
        {
            assy.ResolveAllLightWeightComponents(false);
        }
        var cache = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var top = assy.GetComponents(true) as object[] ?? Array.Empty<object>();
        return top.OfType<Component2>().Select(c => Node(c, 1, maxDepth, props, cache)).ToList();
    }

    private static ComponentNode Node(Component2 c, int depth, int maxDepth, IReadOnlyCollection<string> props,
        Dictionary<string, IReadOnlyDictionary<string, string>> cache)
    {
        int suppression = c.GetSuppression2();
        bool suppressed = suppression == (int)swComponentSuppressionState_e.swComponentSuppressed;
        string path = c.GetPathName() ?? string.Empty;
        string config = c.ReferencedConfiguration ?? string.Empty;

        IReadOnlyDictionary<string, string>? values = null;
        if (props.Count > 0 && !suppressed)
        {
            string key = path + "|" + config;
            if (!cache.TryGetValue(key, out values))
            {
                var dict = new Dictionary<string, string>();
                if (c.GetModelDoc2() is ModelDoc2 md)
                {
                    foreach (string p in props)
                    {
                        if (PropertyTools.ReadResolved(md, config, p) is string v)
                        {
                            dict[p] = v;
                        }
                    }
                }
                cache[key] = values = dict;
            }
        }

        var children = new List<ComponentNode>();
        if (depth < maxDepth && !suppressed && c.GetChildren() is object[] kids)
        {
            children.AddRange(kids.OfType<Component2>().Select(k => Node(k, depth + 1, maxDepth, props, cache)));
        }

        return new ComponentNode(
            Name: c.Name2,
            Path: path,
            Configuration: config,
            IsSuppressed: suppressed,
            ExcludeFromBom: c.ExcludeFromBOM,
            IsVirtual: c.IsVirtual,
            Children: children,
            Properties: values);
    }

    [McpServerTool(Name = "sw_insert_component", Title = "Insert component", OpenWorld = false)]
    [Description("Insert a part or sub-assembly file into an assembly at a position (mm). The component is placed floating; add mates in SolidWorks or fix it.")]
    public Task<object> InsertComponent(
        [Description("Full path of the .sldprt/.sldasm to insert.")] string componentPath,
        [Description("X position of the component origin in mm.")] double x = 0,
        [Description("Y position in mm.")] double y = 0,
        [Description("Z position in mm.")] double z = 0,
        [Description("Configuration of the component to use. Omit for its active configuration.")] string? configuration = null,
        [Description("Path, file name or title of the target assembly. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        string full = paths.Check(componentPath, mustExist: true, nameof(componentPath));
        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocASSEMBLY, "Inserting components");
            // The component must be loaded in memory; open it hidden so the assembly stays the active window.
            Documents.OpenSilently(app, full, readOnly: true, visible: false, configuration);
            var assy = (AssemblyDoc)doc;
            var comp = assy.AddComponent5(full,
                (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                string.Empty, false, string.Empty,
                Units.MmToM(x), Units.MmToM(y), Units.MmToM(z)) as Component2
                ?? throw new McpException($"SolidWorks could not insert '{full}'.");
            if (!string.IsNullOrEmpty(configuration))
            {
                comp.ReferencedConfiguration = configuration;
            }
            doc.EditRebuild3();
            return new { component = comp.Name2, path = comp.GetPathName(), configuration = comp.ReferencedConfiguration };
        }, ct);
    }
}
