using System.Text.RegularExpressions;
using ModelContextProtocol;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

/// <summary>Validated Hole Wizard request (ISO standard). Pure logic, testable without SolidWorks.</summary>
public sealed record HoleSpec(
    string Kind,
    swWzdGeneralHoleTypes_e HoleType,
    swWzdHoleStandardFastenerTypes_e FastenerType,
    string Size,
    swEndConditions_e EndCondition,
    double? DepthMm,
    double? ThreadDepthMm)
{
    /// <summary>ISO hole kinds and the ISO fastener table SolidWorks takes its standard dimensions from.</summary>
    public static readonly IReadOnlyDictionary<string, (swWzdGeneralHoleTypes_e Type, swWzdHoleStandardFastenerTypes_e Fastener, string Use)> Kinds =
        new Dictionary<string, (swWzdGeneralHoleTypes_e, swWzdHoleStandardFastenerTypes_e, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["clearance"] = (swWzdGeneralHoleTypes_e.swWzdHole, swWzdHoleStandardFastenerTypes_e.swStandardISOScrewClearances, "plain through/blind hole sized for a screw"),
            ["tapped"] = (swWzdGeneralHoleTypes_e.swWzdTap, swWzdHoleStandardFastenerTypes_e.swStandardISOTappedHole, "threaded hole (tap drill + cosmetic thread)"),
            ["counterbore"] = (swWzdGeneralHoleTypes_e.swWzdCounterBore, swWzdHoleStandardFastenerTypes_e.swStandardISOSocketHeadCap, "for ISO socket head cap screws (DIN 912 / ISO 4762)"),
            ["countersink"] = (swWzdGeneralHoleTypes_e.swWzdCounterSink, swWzdHoleStandardFastenerTypes_e.swStandardISOSocketCTSKFlatHead, "for ISO countersunk socket screws (ISO 10642)"),
        };

    private static readonly Regex MetricSize = new(@"^M(\d+(?:\.\d+)?)(?:\s*[xX×]\s*(\d+(?:\.\d+)?))?$", RegexOptions.Compiled);

    /// <summary>"m6" -> "M6", "M8 x 1" -> "M8x1.0" (SolidWorks lists fine pitches with one decimal).</summary>
    public static string NormalizeSize(string size)
    {
        var m = MetricSize.Match(size.Trim().ToUpperInvariant().Replace(" ", string.Empty));
        if (!m.Success)
        {
            throw new McpException($"Size '{size}' is not an ISO metric size. Use e.g. M3, M6, M10, or a fine pitch such as M8x1.0.");
        }
        double nominal = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (nominal < 1 || nominal > 100)
        {
            throw new McpException($"Size '{size}' is outside M1-M100.");
        }
        string result = "M" + m.Groups[1].Value;
        if (m.Groups[2].Success)
        {
            double pitch = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            result += "x" + pitch.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);
        }
        return result;
    }

    public static HoleSpec Parse(string kind, string size, string endCondition, double? depthMm, double? threadDepthMm)
    {
        if (!Kinds.TryGetValue(kind.Trim(), out var k))
        {
            throw new McpException($"Unknown hole type '{kind}'. Use: {string.Join(", ", Kinds.Select(x => $"{x.Key} ({x.Value.Use})"))}.");
        }

        var end = endCondition.Trim().ToLowerInvariant() switch
        {
            "through_all" => swEndConditions_e.swEndCondThroughAll,
            "blind" => swEndConditions_e.swEndCondBlind,
            _ => throw new McpException("endCondition must be through_all or blind."),
        };

        if (end == swEndConditions_e.swEndCondBlind && depthMm is not > 0)
        {
            throw new McpException("endCondition blind requires depthMm > 0.");
        }
        if (threadDepthMm is not null)
        {
            if (!kind.Trim().Equals("tapped", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException("threadDepthMm applies only to tapped holes.");
            }
            if (threadDepthMm <= 0)
            {
                throw new McpException("threadDepthMm must be > 0.");
            }
            if (end == swEndConditions_e.swEndCondBlind && threadDepthMm > depthMm)
            {
                throw new McpException($"threadDepthMm ({threadDepthMm}) cannot exceed the tap drill depth depthMm ({depthMm}).");
            }
        }

        return new HoleSpec(kind.Trim().ToLowerInvariant(), k.Type, k.Fastener, NormalizeSize(size), end,
            end == swEndConditions_e.swEndCondBlind ? depthMm : null, threadDepthMm);
    }

    /// <summary>Points must be [x,y,z] mm; at least one; duplicates (within 0.01 mm) rejected because SolidWorks would stack two holes.</summary>
    public static void ValidatePoints(double[][]? points)
    {
        if (points is null || points.Length == 0)
        {
            throw new McpException("points must contain at least one [x, y, z] location in mm on the face.");
        }
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] is not { Length: 3 })
            {
                throw new McpException($"points[{i}] must be [x, y, z] in mm.");
            }
            for (int j = 0; j < i; j++)
            {
                if (EdgeGeometry.Distance(points[i], points[j]) < 0.01)
                {
                    throw new McpException($"points[{i}] duplicates points[{j}].");
                }
            }
        }
    }

    /// <summary>Distance (mm) of a point from a plane given by a normal and a point on it (all mm, normal any length).</summary>
    public static double PlaneDistanceMm(double[] normal, double[] planePoint, double[] point)
    {
        double len = Math.Sqrt(normal.Sum(v => v * v));
        if (len < 1e-12)
        {
            throw new ArgumentException("normal must be non-zero", nameof(normal));
        }
        double dot = 0;
        for (int i = 0; i < 3; i++)
        {
            dot += (point[i] - planePoint[i]) * normal[i];
        }
        return Math.Abs(dot / len);
    }

    /// <summary>Index of the first point further than <paramref name="toleranceMm"/> from the plane, or -1.</summary>
    public static int FirstOffPlane(double[] normal, double[] planePoint, double[][] points, double toleranceMm)
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (PlaneDistanceMm(normal, planePoint, points[i]) > toleranceMm)
            {
                return i;
            }
        }
        return -1;
    }
}
