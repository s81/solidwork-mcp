using ModelContextProtocol;
using SolidWorksMcp.Infrastructure;

namespace SolidWorksMcp.Tests;

public class PathGuardTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("swmcp_root").FullName;
    private readonly string _outside = Directory.CreateTempSubdirectory("swmcp_out").FullName;

    private PathGuard Guard(params string[] roots) => new(new ServerOptions { AllowedRoots = roots });

    [Fact]
    public void NoRootsAllowsAnything() => Assert.Equal(Path.Combine(_outside, "a.step"), Guard().Check(Path.Combine(_outside, "a.step")));

    [Fact]
    public void PathInsideRootIsAllowed()
    {
        string p = Path.Combine(_root, "sub", "part.sldprt");
        Assert.Equal(p, Guard(_root).Check(p));
    }

    [Fact]
    public void PathOutsideRootIsDenied()
        => Assert.Throws<McpException>(() => Guard(_root).Check(Path.Combine(_outside, "x.sldprt")));

    [Fact]
    public void TraversalOutOfRootIsDenied()
        => Assert.Throws<McpException>(() => Guard(_root).Check(Path.Combine(_root, "..", Path.GetFileName(_outside), "x.sldprt")));

    [Fact]
    public void SiblingWithSharedPrefixIsDenied()
        => Assert.Throws<McpException>(() => Guard(_root).Check(_root + "_evil" + Path.DirectorySeparatorChar + "x.sldprt"));

    [Fact]
    public void RootItselfIsAllowed() => Assert.Equal(_root, Guard(_root).Check(_root));

    [Fact]
    public void QuotesAreStripped() => Assert.Equal(Path.Combine(_root, "a b.step"), Guard(_root).Check("\"" + Path.Combine(_root, "a b.step") + "\""));

    [Fact]
    public void EmptyPathIsRejected() => Assert.Throws<McpException>(() => Guard().Check("  "));

    [Fact]
    public void MustExistIsEnforced() => Assert.Throws<McpException>(() => Guard().Check(Path.Combine(_root, "missing.sldprt"), mustExist: true));

    public void Dispose()
    {
        Directory.Delete(_root, true);
        Directory.Delete(_outside, true);
    }
}
