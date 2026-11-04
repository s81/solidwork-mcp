using SolidWorks.Interop.swconst;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tests;

public class SwFlagsTests
{
    [Fact]
    public void ZeroIsNone() => Assert.Equal("none", SwFlags.Describe<swFileLoadError_e>(0));

    [Fact]
    public void DecodesCombinedBits()
    {
        int v = (int)swFileLoadError_e.swFileNotFoundError | (int)swFileLoadError_e.swInvalidFileTypeError;
        string s = SwFlags.Describe<swFileLoadError_e>(v);
        Assert.Contains(nameof(swFileLoadError_e.swFileNotFoundError), s);
        Assert.Contains(nameof(swFileLoadError_e.swInvalidFileTypeError), s);
    }

    [Fact]
    public void UnknownBitsShownAsHex() => Assert.Contains("0x40000000", SwFlags.Describe<swFileLoadError_e>(0x40000000));
}
