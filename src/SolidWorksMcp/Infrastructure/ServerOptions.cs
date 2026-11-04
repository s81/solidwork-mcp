namespace SolidWorksMcp.Infrastructure;

/// <summary>Runtime configuration, read from environment variables (set them in the MCP client config).</summary>
public sealed class ServerOptions
{
    /// <summary>SWMCP_ATTACH_ONLY=true: never launch SolidWorks, only attach to a running instance.</summary>
    public bool AttachOnly { get; init; }

    /// <summary>SWMCP_VISIBLE (default true): show the SolidWorks window when the server launches it.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>SWMCP_ALLOW_MACROS=true enables sw_run_macro. Off by default: a macro is arbitrary code.</summary>
    public bool AllowMacros { get; init; }

    /// <summary>
    /// SWMCP_ALLOWED_ROOTS: semicolon-separated folders. When set, every file path a tool reads or writes
    /// must be inside one of them (e.g. "D:\PDM\Vault;C:\Exports"). Empty = no restriction.
    /// </summary>
    public IReadOnlyList<string> AllowedRoots { get; init; } = Array.Empty<string>();

    /// <summary>SWMCP_STARTUP_TIMEOUT_SECONDS (default 180): how long to wait for a launched SolidWorks to finish starting.</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(180);

    /// <summary>SWMCP_COM_RETRY_TIMEOUT_SECONDS (default 60): how long to retry calls SolidWorks rejects while busy.</summary>
    public TimeSpan ComRetryTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public static ServerOptions FromEnvironment() => FromLookup(Environment.GetEnvironmentVariable);

    public static ServerOptions FromLookup(Func<string, string?> get)
    {
        return new ServerOptions
        {
            AttachOnly = ParseBool(get("SWMCP_ATTACH_ONLY"), false),
            Visible = ParseBool(get("SWMCP_VISIBLE"), true),
            AllowMacros = ParseBool(get("SWMCP_ALLOW_MACROS"), false),
            AllowedRoots = (get("SWMCP_ALLOWED_ROOTS") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetFullPath)
                .ToArray(),
            StartupTimeout = TimeSpan.FromSeconds(ParseInt(get("SWMCP_STARTUP_TIMEOUT_SECONDS"), 180)),
            ComRetryTimeout = TimeSpan.FromSeconds(ParseInt(get("SWMCP_COM_RETRY_TIMEOUT_SECONDS"), 60)),
        };
    }

    internal static bool ParseBool(string? value, bool fallback) => value?.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "on" => true,
        "0" or "false" or "no" or "off" => false,
        _ => fallback,
    };

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out int v) && v > 0 ? v : fallback;
}
