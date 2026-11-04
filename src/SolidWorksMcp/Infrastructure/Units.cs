namespace SolidWorksMcp.Infrastructure;

/// <summary>
/// The SolidWorks API always works in SI system units (metres, radians, kilograms) regardless of the
/// document's display units. Tools expose millimetres and degrees, which is what engineers type.
/// </summary>
public static class Units
{
    public static double MmToM(double mm) => mm / 1000.0;
    public static double MToMm(double m) => m * 1000.0;
    public static double DegToRad(double deg) => deg * Math.PI / 180.0;
    public static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

    public static double[] MToMm(double[]? m) => m is null ? Array.Empty<double>() : m.Select(v => Round(MToMm(v))).ToArray();

    /// <summary>Round away floating noise (1e-9 mm is far below any manufacturing tolerance).</summary>
    public static double Round(double value, int digits = 6) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
