using ModelContextProtocol;

namespace SolidWorksMcp.Infrastructure;

/// <summary>Normalizes tool-supplied paths and enforces <see cref="ServerOptions.AllowedRoots"/>.</summary>
public sealed class PathGuard
{
    private readonly IReadOnlyList<string> _roots;

    public PathGuard(ServerOptions options) => _roots = options.AllowedRoots;

    /// <summary>Returns the absolute path, or throws an <see cref="McpException"/> the model can read.</summary>
    public string Check(string? path, bool mustExist = false, string argumentName = "path")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException($"'{argumentName}' is required.");
        }

        string full;
        try
        {
            full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new McpException($"'{argumentName}' is not a valid path: {ex.Message}");
        }

        if (!IsAllowed(full))
        {
            throw new McpException(
                $"Access to '{full}' is denied. Allowed folders (SWMCP_ALLOWED_ROOTS): {string.Join("; ", _roots)}");
        }

        if (mustExist && !File.Exists(full) && !Directory.Exists(full))
        {
            throw new McpException($"'{full}' does not exist.");
        }

        return full;
    }

    public bool IsAllowed(string fullPath)
    {
        if (_roots.Count == 0)
        {
            return true;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (string root in _roots)
        {
            string rootWithSep = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (fullPath.Equals(root, comparison) || fullPath.StartsWith(rootWithSep, comparison))
            {
                return true;
            }
        }
        return false;
    }
}
