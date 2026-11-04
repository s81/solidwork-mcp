using SolidWorksMcp.Infrastructure;

namespace SolidWorksMcp.Tests;

public class UnitsTests
{
    [Fact] public void MillimetresToMetres() => Assert.Equal(0.025, Units.MmToM(25), 12);
    [Fact] public void MetresToMillimetres() => Assert.Equal(1500, Units.MToMm(1.5), 9);
    [Fact] public void DegreesRoundTrip() => Assert.Equal(37.5, Units.RadToDeg(Units.DegToRad(37.5)), 9);
    [Fact] public void RightAngleInRadians() => Assert.Equal(Math.PI / 2, Units.DegToRad(90), 12);
    [Fact] public void RoundRemovesFloatingNoise() => Assert.Equal(100.0, Units.Round(Units.MToMm(0.1 + 1e-13)));
    [Fact] public void ArrayConversionHandlesNull() => Assert.Empty(Units.MToMm((double[]?)null));
    [Fact] public void ArrayConversion() => Assert.Equal(new[] { 10.0, -20.0, 0.5 }, Units.MToMm(new[] { 0.01, -0.02, 0.0005 }));
}
