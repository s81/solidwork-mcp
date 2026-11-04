using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SolidWorksMcp.Infrastructure;

/// <summary>
/// Classic COM IOleMessageFilter. When SolidWorks is busy it rejects incoming calls with
/// SERVERCALL_RETRYLATER (RPC_E_CALL_REJECTED, 0x80010001). With this filter COM retries the call
/// every 100 ms until <see cref="_timeout"/> elapses instead of throwing immediately.
/// Must be registered on an STA thread.
/// </summary>
[SupportedOSPlatform("windows")]
[ComVisible(true)]
internal sealed class OleMessageFilter : IOleMessageFilter
{
    private const int SERVERCALL_ISHANDLED = 0;
    private const int SERVERCALL_RETRYLATER = 2;
    private const int PENDINGMSG_WAITDEFPROCESS = 2;

    private readonly TimeSpan _timeout;

    private OleMessageFilter(TimeSpan timeout) => _timeout = timeout;

    public static OleMessageFilter Register(TimeSpan retryTimeout)
    {
        var filter = new OleMessageFilter(retryTimeout);
        int hr = CoRegisterMessageFilter(filter, out _);
        Marshal.ThrowExceptionForHR(hr);
        return filter;
    }

    public static void Revoke() => CoRegisterMessageFilter(null, out _);

    int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        => SERVERCALL_ISHANDLED;

    int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
    {
        if (dwRejectType == SERVERCALL_RETRYLATER && dwTickCount < _timeout.TotalMilliseconds)
        {
            return 100; // retry after 100 ms
        }
        return -1; // give up -> caller receives RPC_E_CALL_REJECTED
    }

    int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        => PENDINGMSG_WAITDEFPROCESS;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);
}

[ComImport]
[Guid("00000016-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleMessageFilter
{
    [PreserveSig]
    int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

    [PreserveSig]
    int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

    [PreserveSig]
    int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}
