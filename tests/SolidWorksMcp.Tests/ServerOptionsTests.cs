using SolidWorksMcp.Infrastructure;

namespace SolidWorksMcp.Tests;

public class ServerOptionsTests
{
    [Fact]
    public void DefaultsAreSafe()
    {
        var o = ServerOptions.FromLookup(_ => null);
        Assert.False(o.AllowMacros);
        Assert.False(o.AttachOnly);
        Assert.True(o.Visible);
        Assert.Empty(o.AllowedRoots);
        Assert.Equal(TimeSpan.FromSeconds(180), o.StartupTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), o.ComRetryTimeout);
    }

    [Fact]
    public void ParsesEnvironment()
    {
        string a = Path.GetTempPath();
        var env = new Dictionary<string, string>
        {
            ["SWMCP_ALLOW_MACROS"] = "true",
            ["SWMCP_ATTACH_ONLY"] = "1",
            ["SWMCP_VISIBLE"] = "no",
            ["SWMCP_ALLOWED_ROOTS"] = $" {a} ; ;",
            ["SWMCP_STARTUP_TIMEOUT_SECONDS"] = "30",
            ["SWMCP_COM_RETRY_TIMEOUT_SECONDS"] = "abc",
        };
        var o = ServerOptions.FromLookup(k => env.GetValueOrDefault(k));
        Assert.True(o.AllowMacros);
        Assert.True(o.AttachOnly);
        Assert.False(o.Visible);
        Assert.Equal(new[] { Path.GetFullPath(a) }, o.AllowedRoots);
        Assert.Equal(TimeSpan.FromSeconds(30), o.StartupTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), o.ComRetryTimeout); // invalid -> default
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("on", true)]
    [InlineData("Off", false)]
    [InlineData("maybe", true)]
    public void ParseBool(string input, bool expected) => Assert.Equal(expected, ServerOptions.ParseBool(input, true));
}
