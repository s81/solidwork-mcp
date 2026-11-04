using System.Reflection;

namespace SolidWorksMcp;

/// <summary>The server version reported to MCP clients: the build's &lt;Version&gt; (set by the csproj or -p:Version).</summary>
public static class ServerVersion
{
    public static string Current { get; } = Normalize(
        typeof(ServerVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Drops the "+commit" build metadata the SDK appends, e.g. "0.1.1+a8add7f" → "0.1.1".</summary>
    public static string Normalize(string? informationalVersion)
    {
        string v = (informationalVersion ?? string.Empty).Trim();
        int plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        return v.Length == 0 ? "0.0.0" : v;
    }
}
