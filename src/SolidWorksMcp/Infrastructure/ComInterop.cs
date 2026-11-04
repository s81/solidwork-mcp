using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SolidWorksMcp.Infrastructure;

/// <summary>
/// .NET (Core) removed Marshal.GetActiveObject; this re-implements it via oleaut32 so we can attach
/// to an already running SolidWorks instance (Running Object Table).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ComInterop
{
    private const int MK_E_UNAVAILABLE = unchecked((int)0x800401E3);

    public static object? TryGetActiveObject(string progId)
    {
        int hr = CLSIDFromProgIDEx(progId, out Guid clsid);
        if (hr < 0)
        {
            hr = CLSIDFromProgID(progId, out clsid);
            if (hr < 0)
            {
                return null; // ProgID not registered => SolidWorks not installed
            }
        }

        hr = GetActiveObject(ref clsid, IntPtr.Zero, out object? obj);
        if (hr == MK_E_UNAVAILABLE)
        {
            return null;
        }
        Marshal.ThrowExceptionForHR(hr);
        return obj;
    }

    /// <summary>HRESULTs that mean the out-of-proc server is gone (SolidWorks closed or crashed).</summary>
    public static bool IsServerGone(COMException ex) => ex.HResult is
        unchecked((int)0x800706BA) or // RPC_S_SERVER_UNAVAILABLE
        unchecked((int)0x800706BE) or // RPC_S_CALL_FAILED
        unchecked((int)0x80010108) or // RPC_E_DISCONNECTED
        unchecked((int)0x80010012);   // RPC_E_SERVER_DIED_DNE

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgIDEx(string lpszProgID, out Guid lpclsid);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string lpszProgID, out Guid lpclsid);

    [DllImport("oleaut32.dll")]
    private static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object? ppunk);
}
