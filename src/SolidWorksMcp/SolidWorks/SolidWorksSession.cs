using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using SolidWorks.Interop.sldworks;
using SolidWorksMcp.Infrastructure;

namespace SolidWorksMcp.SolidWorks;

/// <summary>
/// Owns the connection to SolidWorks. Every API call goes through <see cref="RunAsync{T}(Func{SldWorks, T}, CancellationToken)"/>
/// so it executes on the STA thread with a live, verified application object.
/// COM objects must never escape the delegate: return plain DTOs only.
/// </summary>
public sealed class SolidWorksSession : IDisposable
{
    private const string ProgId = "SldWorks.Application";

    private readonly StaDispatcher _sta;
    private readonly ServerOptions _options;
    private readonly ILogger<SolidWorksSession> _logger;
    private SldWorks? _app;

    public SolidWorksSession(StaDispatcher sta, ServerOptions options, ILogger<SolidWorksSession> logger)
    {
        _sta = sta;
        _options = options;
        _logger = logger;
    }

    public ServerOptions Options => _options;

    /// <summary>Run <paramref name="action"/> against SolidWorks on the STA thread.</summary>
    public Task<T> RunAsync<T>(Func<SldWorks, T> action, CancellationToken cancellationToken = default)
        => _sta.InvokeAsync(() => Execute(action), cancellationToken);

    /// <summary>Run <paramref name="action"/> against a document (null/empty = active document).</summary>
    public Task<T> RunOnDocumentAsync<T>(string? document, Func<SldWorks, ModelDoc2, T> action, CancellationToken cancellationToken = default)
        => RunAsync(app => action(app, Documents.Resolve(app, document)), cancellationToken);

    private T Execute<T>(Func<SldWorks, T> action)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new McpException("SolidWorks automation requires Windows with SolidWorks installed.");
        }

        SldWorks app = EnsureConnected();
        try
        {
            return action(app);
        }
        catch (COMException ex) when (ComInterop.IsServerGone(ex))
        {
            _app = null;
            throw new McpException("Lost connection to SolidWorks (it was closed or crashed). Call the tool again to reconnect.", ex);
        }
        catch (COMException ex)
        {
            throw new McpException($"SolidWorks API error 0x{ex.HResult:X8}: {ex.Message}", ex);
        }
        catch (InvalidCastException ex)
        {
            throw new McpException($"Unexpected SolidWorks object type: {ex.Message}", ex);
        }
    }

    private SldWorks EnsureConnected()
    {
        if (_app is not null)
        {
            try
            {
                _ = _app.RevisionNumber(); // cheap liveness ping
                return _app;
            }
            catch (COMException ex) when (ComInterop.IsServerGone(ex))
            {
                _logger.LogWarning("SolidWorks connection lost; reconnecting.");
                _app = null;
            }
            catch (InvalidComObjectException)
            {
                _app = null;
            }
        }

        _app = Connect();
        return _app;
    }

    private SldWorks Connect()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        if (ComInterop.TryGetActiveObject(ProgId) is SldWorks running)
        {
            _logger.LogInformation("Attached to running SolidWorks {Revision}.", running.RevisionNumber());
            return running;
        }

        if (_options.AttachOnly)
        {
            throw new McpException("SolidWorks is not running and SWMCP_ATTACH_ONLY=true. Start SolidWorks and retry.");
        }

        Type type = Type.GetTypeFromProgID(ProgId)
            ?? throw new McpException("SolidWorks is not installed on this machine (ProgID 'SldWorks.Application' is not registered).");

        _logger.LogInformation("Launching SolidWorks...");
        var app = (SldWorks)(Activator.CreateInstance(type)
            ?? throw new McpException("Failed to create a SolidWorks instance."));

        var sw = Stopwatch.StartNew();
        while (!app.StartupProcessCompleted)
        {
            if (sw.Elapsed > _options.StartupTimeout)
            {
                throw new McpException($"SolidWorks did not finish starting within {_options.StartupTimeout.TotalSeconds:0} s.");
            }
            Thread.Sleep(500);
        }

        app.Visible = _options.Visible;
        app.UserControl = _options.Visible; // keep SolidWorks running for the user when the MCP server exits
        _logger.LogInformation("SolidWorks {Revision} started in {Seconds:0.0} s.", app.RevisionNumber(), sw.Elapsed.TotalSeconds);
        return app;
    }

    public void Dispose()
    {
        // Intentionally do not ExitApp(): SolidWorks belongs to the user, not to this process.
        if (_app is not null && OperatingSystem.IsWindows())
        {
            try
            {
                _sta.InvokeAsync(() => { Marshal.FinalReleaseComObject(_app); _app = null; }).Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // best effort
            }
        }
    }
}
