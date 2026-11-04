using SolidWorksMcp;

namespace SolidWorksMcp.Tests;

public class ServerVersionTests
{
    [Theory]
    [InlineData("0.1.1+a8add7f647ad6945df30ad9aff30faf0adae3ad2", "0.1.1")]
    [InlineData("0.2.0-beta.1+abc", "0.2.0-beta.1")]
    [InlineData(" 1.0.0 ", "1.0.0")]
    [InlineData(null, "0.0.0")]
    [InlineData("", "0.0.0")]
    public void Normalizes(string? input, string expected) => Assert.Equal(expected, ServerVersion.Normalize(input));

    [Fact]
    public void CurrentHasNoBuildMetadata() => Assert.DoesNotContain("+", ServerVersion.Current);
}
