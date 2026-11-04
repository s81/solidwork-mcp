using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace SolidWorksMcp.Infrastructure;

/// <summary>
/// Runs every SolidWorks COM call on one dedicated single-threaded-apartment (STA) thread.
/// </summary>
/// <remarks>
/// The SolidWorks API is apartment-threaded. MCP tool calls arrive on thread-pool (MTA) threads and may
/// overlap, so all calls are serialized onto a single STA thread. That thread also registers an OLE
/// message filter so that "call rejected by callee" (SolidWorks busy rebuilding / showing a dialog) is
/// retried instead of failing immediately.
/// </remarks>
public sealed class StaDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly ILogger<StaDispatcher>? _logger;
    private readonly TimeSpan _comRetryTimeout;

    public StaDispatcher(ServerOptions options, ILogger<StaDispatcher>? logger = null)
    {
        _logger = logger;
        _comRetryTimeout = options.ComRetryTimeout;
        _thread = new Thread(Run) { IsBackground = true, Name = "SolidWorks STA" };
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }
        _thread.Start();
    }

    /// <summary>Managed thread id of the dispatcher thread (for diagnostics/tests).</summary>
    public int ThreadId => _thread.ManagedThreadId;

    public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        _queue.Add(() =>
        {
            // A COM call cannot be aborted midway; we only honour cancellation before it starts.
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }
            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, CancellationToken.None);
        return tcs.Task;
    }

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        => InvokeAsync<object?>(() => { action(); return null; }, cancellationToken);

    private void Run()
    {
        OleMessageFilter? filter = null;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                filter = OleMessageFilter.Register(_comRetryTimeout);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not register OLE message filter; busy SolidWorks calls may fail fast.");
            }
        }

        try
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                work();
            }
        }
        finally
        {
            if (filter is not null && OperatingSystem.IsWindows())
            {
                OleMessageFilter.Revoke();
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }
        _queue.Dispose();
    }
}
