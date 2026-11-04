namespace SolidWorksMcp.SolidWorks;

/// <summary>Plain snapshot of an assembly component, taken on the STA thread so the BOM logic is COM-free and testable.</summary>
public sealed record ComponentNode(
    string Name,
    string Path,
    string Configuration,
    bool IsSuppressed,
    bool ExcludeFromBom,
    bool IsVirtual,
    IReadOnlyList<ComponentNode> Children,
    IReadOnlyDictionary<string, string>? Properties = null)
{
    public bool IsAssembly => Path.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase) || Children.Count > 0;
}

public sealed record BomRow(
    int Item,
    string FileName,
    string Path,
    string Configuration,
    int Quantity,
    bool IsAssembly,
    IReadOnlyDictionary<string, string> Properties);

public static class BomBuilder
{
    /// <summary>
    /// top_level: direct children of the assembly (sub-assemblies count as one row each).
    /// parts_only: all leaf parts at any depth with total quantities (the "flat" cut list for purchasing/production).
    /// Suppressed and "exclude from BOM" components are skipped, and so are their children.
    /// Rows are grouped by file path + configuration, so the same part in two configurations makes two rows.
    /// </summary>
    public static IReadOnlyList<BomRow> Build(IEnumerable<ComponentNode> topLevel, string mode)
    {
        var rows = new Dictionary<(string, string), (ComponentNode Node, int Qty)>();
        var order = new List<(string, string)>();

        void Add(ComponentNode n)
        {
            var key = (Key(n), n.Configuration);
            if (rows.TryGetValue(key, out var existing))
            {
                rows[key] = (existing.Node, existing.Qty + 1);
            }
            else
            {
                rows[key] = (n, 1);
                order.Add(key);
            }
        }

        void Walk(ComponentNode n)
        {
            if (n.IsSuppressed || n.ExcludeFromBom)
            {
                return;
            }
            if (n.Children.Count == 0 && !n.Path.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
            {
                Add(n);
                return;
            }
            foreach (var c in n.Children)
            {
                Walk(c);
            }
        }

        bool flat = mode.Equals("parts_only", StringComparison.OrdinalIgnoreCase);
        if (!flat && !mode.Equals("top_level", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown BOM mode '{mode}'. Use top_level or parts_only.", nameof(mode));
        }

        foreach (var n in topLevel)
        {
            if (flat)
            {
                Walk(n);
            }
            else if (!n.IsSuppressed && !n.ExcludeFromBom)
            {
                Add(n);
            }
        }

        int item = 0;
        return order.Select(k =>
        {
            var (node, qty) = rows[k];
            return new BomRow(
                ++item,
                node.IsVirtual || node.Path.Length == 0 ? node.Name : FileName(node.Path),
                node.Path,
                node.Configuration,
                qty,
                node.IsAssembly,
                node.Properties ?? new Dictionary<string, string>());
        }).ToList();
    }

    // SolidWorks always reports Windows paths; split on both separators so this also works when tested on Linux.
    internal static string FileName(string path) => path[(path.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];

    // Virtual components have no file of their own; group them by name instead.
    private static string Key(ComponentNode n)
        => n.IsVirtual || n.Path.Length == 0 ? "virtual:" + n.Name : n.Path.ToLowerInvariant();
}
