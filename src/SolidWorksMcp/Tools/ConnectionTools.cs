using System.ComponentModel;
using ModelContextProtocol.Server;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class ConnectionTools(SolidWorksSession sw)
{
    public sealed record ConnectionInfo(string Revision, string? ExecutablePath, int OpenDocuments, string? ActiveDocument, bool MacrosEnabled, IReadOnlyList<string> AllowedRoots);

    [McpServerTool(Name = "sw_connect", Title = "Connect to SolidWorks", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Attach to the running SolidWorks instance (or launch it unless SWMCP_ATTACH_ONLY=true) and report its version and open documents.")]
    public Task<ConnectionInfo> Connect(CancellationToken ct) => sw.RunAsync(app =>
    {
        var docs = Documents.Enumerate(app).ToList();
        return new ConnectionInfo(
            Revision: app.RevisionNumber(),
            ExecutablePath: app.GetExecutablePath(),
            OpenDocuments: docs.Count,
            ActiveDocument: (app.ActiveDoc as global::SolidWorks.Interop.sldworks.ModelDoc2)?.GetTitle(),
            MacrosEnabled: sw.Options.AllowMacros,
            AllowedRoots: sw.Options.AllowedRoots);
    }, ct);
}
