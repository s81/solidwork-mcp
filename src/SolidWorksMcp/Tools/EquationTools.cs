using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class EquationTools(SolidWorksSession sw)
{
    public sealed record EquationInfo(int Index, string Equation, double Value, bool IsGlobalVariable);

    [McpServerTool(Name = "sw_list_equations", Title = "List equations", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List equations and global variables, e.g. \"Width\" = 600 or \"D1@Sketch1\" = \"Width\" / 2. Values are in document units.")]
    public Task<IReadOnlyList<EquationInfo>> ListEquations(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<EquationInfo>>(document, (app, doc) => Read(doc.GetEquationMgr()), ct);

    private static List<EquationInfo> Read(EquationMgr mgr)
    {
        var list = new List<EquationInfo>();
        int n = mgr.GetCount();
        for (int i = 0; i < n; i++)
        {
            list.Add(new EquationInfo(i, mgr.Equation[i], mgr.Value[i], mgr.GlobalVariable[i]));
        }
        return list;
    }

    [McpServerTool(Name = "sw_set_global_variable", Title = "Set global variable", Idempotent = true, OpenWorld = false)]
    [Description("Create or update a global variable (e.g. Width = 1200) and rebuild. The expression may reference other variables: \"Length\" * 2.")]
    public Task<IReadOnlyList<EquationInfo>> SetGlobalVariable(
        [Description("Variable name without quotes, e.g. Width.")] string name,
        [Description("Value or expression, e.g. 1200, 1200mm, \"Height\" / 2.")] string expression,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<EquationInfo>>(document, (app, doc) =>
        {
            EquationMgr mgr = doc.GetEquationMgr();
            string eq = $"\"{name.Trim('"')}\" = {expression}";
            int index = FindVariable(mgr, name);
            if (index >= 0)
            {
                mgr.Equation[index] = eq;
            }
            else if (mgr.Add2(-1, eq, true) < 0)
            {
                throw new McpException($"SolidWorks rejected the equation {eq}.");
            }
            Evaluate(mgr, doc);
            return Read(mgr);
        }, ct);

    [McpServerTool(Name = "sw_add_equation", Title = "Add equation", OpenWorld = false)]
    [Description("Add an equation that drives a dimension, e.g. \"D1@Sketch1\" = \"Width\" - 2 * \"Thickness\".")]
    public Task<IReadOnlyList<EquationInfo>> AddEquation(
        [Description("Full equation text including quotes around names.")] string equation,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<EquationInfo>>(document, (app, doc) =>
        {
            EquationMgr mgr = doc.GetEquationMgr();
            if (mgr.Add2(-1, equation, true) < 0)
            {
                throw new McpException($"SolidWorks rejected the equation {equation}.");
            }
            Evaluate(mgr, doc);
            return Read(mgr);
        }, ct);

    [McpServerTool(Name = "sw_delete_equation", Title = "Delete equation", Destructive = true, OpenWorld = false)]
    [Description("Delete an equation or global variable by index (from sw_list_equations).")]
    public Task<IReadOnlyList<EquationInfo>> DeleteEquation(
        [Description("Index from sw_list_equations.")] int index,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<EquationInfo>>(document, (app, doc) =>
        {
            EquationMgr mgr = doc.GetEquationMgr();
            if (index < 0 || index >= mgr.GetCount())
            {
                throw new McpException($"Index {index} is out of range (0..{mgr.GetCount() - 1}).");
            }
            mgr.Delete(index);
            Evaluate(mgr, doc);
            return Read(mgr);
        }, ct);

    private static int FindVariable(EquationMgr mgr, string name)
    {
        string quoted = "\"" + name.Trim('"') + "\"";
        for (int i = 0; i < mgr.GetCount(); i++)
        {
            if (mgr.GlobalVariable[i] && mgr.Equation[i].TrimStart().StartsWith(quoted, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static void Evaluate(EquationMgr mgr, ModelDoc2 doc)
    {
        mgr.EvaluateAll();
        doc.EditRebuild3();
        if (mgr.Status != 0)
        {
            throw new McpException($"Equation evaluation reported status {mgr.Status} (syntax error or circular reference?).");
        }
    }
}
