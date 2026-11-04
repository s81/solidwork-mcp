using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class MacroTools(SolidWorksSession sw, PathGuard paths)
{
    [McpServerTool(Name = "sw_run_macro", Title = "Run SolidWorks macro", Destructive = true, OpenWorld = true)]
    [Description("Run an existing VBA (.swp) or VSTA (.dll) macro. Disabled unless the server is started with SWMCP_ALLOW_MACROS=true, because a macro can execute arbitrary code.")]
    public Task<string> RunMacro(
        [Description("Full path of the .swp / .dll macro.")] string macroPath,
        [Description("Module name. Default for recorded VBA macros: <file name>1, e.g. Macro1.swp -> Macro11.")] string? module = null,
        [Description("Procedure (Sub) to run.")] string procedure = "main",
        CancellationToken ct = default)
    {
        if (!sw.Options.AllowMacros)
        {
            throw new McpException("Running macros is disabled. Set SWMCP_ALLOW_MACROS=true in the MCP server environment to enable it.");
        }
        string full = paths.Check(macroPath, mustExist: true, nameof(macroPath));
        string ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext is not (".swp" or ".swb" or ".dll"))
        {
            throw new McpException("macroPath must be a .swp, .swb or .dll macro.");
        }
        string mod = module ?? Path.GetFileNameWithoutExtension(full) + "1";

        return sw.RunAsync(app =>
        {
            bool ok = app.RunMacro2(full, mod, procedure, (int)swRunMacroOption_e.swRunMacroUnloadAfterRun, out int err);
            return ok ? $"Macro {Path.GetFileName(full)} ({mod}.{procedure}) finished."
                : throw new McpException($"Macro failed: {(swRunMacroError_e)err}");
        }, ct);
    }
}
