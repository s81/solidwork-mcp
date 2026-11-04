using ModelContextProtocol;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

/// <summary>
/// Pure geometry and option parsing for section and detail drawing views. All lengths are sheet millimetres
/// measured from the sheet's lower-left corner; outlines are [xMin, yMin, xMax, yMax].
/// </summary>
public static class SectionDetailSpec
{
    /// <summary>Cut-line angle in degrees from the sheet X axis: horizontal = 0, vertical = 90, angled = angleDeg.</summary>
    public static double CutAngle(string orientation, double? angleDeg)
    {
        string o = orientation.Trim().ToLowerInvariant();
        double angle = o switch
        {
            "horizontal" or "h" => 0,
            "vertical" or "v" => 90,
            "angled" or "aligned" or "angle" => angleDeg ?? throw new McpException("orientation 'angled' requires angleDeg (degrees from the sheet X axis)."),
            _ => throw new McpException($"Unknown orientation '{orientation}'. Use horizontal, vertical or angled."),
        };
        if (o is not ("angled" or "aligned" or "angle") && angleDeg is not null)
        {
            throw new McpException("angleDeg is only used with orientation 'angled'.");
        }
        if (double.IsNaN(angle) || double.IsInfinity(angle))
        {
            throw new McpException("angleDeg must be a finite number.");
        }
        angle %= 180;
        return angle < 0 ? angle + 180 : angle;
    }

    public static double[] RequireOutline(double[]? outlineMm, string view)
    {
        if (outlineMm is not { Length: >= 4 } || outlineMm[2] <= outlineMm[0] || outlineMm[3] <= outlineMm[1])
        {
            throw new McpException($"View '{view}' has no usable outline (is it empty or hidden?).");
        }
        return outlineMm;
    }

    public static double[] RequirePoint(double[]? point, string name)
    {
        if (point is not { Length: 2 } || point.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
        {
            throw new McpException($"{name} must be [x, y] in mm from the sheet's lower-left corner.");
        }
        return point;
    }

    /// <summary>
    /// End points of a cut line through <paramref name="throughMm"/> at <paramref name="angleDeg"/>, clipped to the
    /// view outline grown by <paramref name="marginMm"/> on every side, so the line crosses the whole view.
    /// </summary>
    public static (double[] Start, double[] End) CutLine(double[] outlineMm, double[] throughMm, double angleDeg, double marginMm = 5)
    {
        double xMin = outlineMm[0] - marginMm, yMin = outlineMm[1] - marginMm;
        double xMax = outlineMm[2] + marginMm, yMax = outlineMm[3] + marginMm;
        double px = throughMm[0], py = throughMm[1];
        if (px < outlineMm[0] || px > outlineMm[2] || py < outlineMm[1] || py > outlineMm[3])
        {
            throw new McpException($"throughMm [{px}, {py}] is outside the parent view outline [{string.Join(", ", outlineMm.Take(4))}] mm.");
        }

        double rad = angleDeg * Math.PI / 180.0;
        double dx = Math.Round(Math.Cos(rad), 12), dy = Math.Round(Math.Sin(rad), 12);
        double tMin = double.NegativeInfinity, tMax = double.PositiveInfinity;
        Clip(dx, px, xMin, xMax, ref tMin, ref tMax);
        Clip(dy, py, yMin, yMax, ref tMin, ref tMax);
        return (new[] { R(px + tMin * dx), R(py + tMin * dy) }, new[] { R(px + tMax * dx), R(py + tMax * dy) });
    }

    private static void Clip(double d, double p, double lo, double hi, ref double tMin, ref double tMax)
    {
        if (d == 0)
        {
            return; // parallel to this pair of edges, and p is inside, so no limit
        }
        double t1 = (lo - p) / d, t2 = (hi - p) / d;
        tMin = Math.Max(tMin, Math.Min(t1, t2));
        tMax = Math.Min(tMax, Math.Max(t1, t2));
    }

    /// <summary>
    /// Default centre for an aligned section view: beside the parent, perpendicular to the cut line
    /// (horizontal cut: below; vertical cut: to the right), one parent extent plus <paramref name="gapMm"/> away.
    /// </summary>
    public static double[] SectionPlacement(double[] outlineMm, double angleDeg, double gapMm = 20)
    {
        double rad = angleDeg * Math.PI / 180.0;
        double nx = Math.Round(Math.Sin(rad), 12), ny = Math.Round(-Math.Cos(rad), 12);
        double w = outlineMm[2] - outlineMm[0], h = outlineMm[3] - outlineMm[1];
        double cx = (outlineMm[0] + outlineMm[2]) / 2, cy = (outlineMm[1] + outlineMm[3]) / 2;
        double distance = Math.Abs(nx) * w + Math.Abs(ny) * h + gapMm;
        return new[] { R(cx + nx * distance), R(cy + ny * distance) };
    }

    /// <summary>swCreateSectionViewAtOptions_e bit mask for CreateSectionViewAt5.</summary>
    public static int SectionOptions(bool aligned, bool flipDirection, bool scaleWithModel, bool partial, bool surfaceOnly, bool excludeFasteners)
    {
        int o = 0;
        if (!aligned) o |= (int)swCreateSectionViewAtOptions_e.swCreateSectionView_NotAligned;
        if (flipDirection) o |= (int)swCreateSectionViewAtOptions_e.swCreateSectionView_ChangeDirection;
        if (scaleWithModel) o |= (int)swCreateSectionViewAtOptions_e.swCreateSectionView_ScaleWithModel;
        if (partial) o |= (int)swCreateSectionViewAtOptions_e.swCreateSectionView_Partial;
        if (surfaceOnly) o |= (int)swCreateSectionViewAtOptions_e.swCreateSectionView_DisplaySurfaceCut;
        if (excludeFasteners) o |= (int)swCreateSectionViewAtOptions_e.swCreateSectionView_ExcludeFasteners;
        return o;
    }

    /// <summary>"2:1", "1:5", "2" or "0.5" → (numerator, denominator), both > 0.</summary>
    public static (double Num, double Den) ParseScale(string scale)
    {
        var parts = scale.Trim().Split(':', '/');
        static bool Num(string t, out double v)
            => double.TryParse(t.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        double num = 0, den = 1;
        bool ok = parts.Length is 1 or 2 && Num(parts[0], out num) && (parts.Length == 1 || Num(parts[1], out den));
        if (!ok || !(num > 0) || !(den > 0) || double.IsInfinity(num) || double.IsInfinity(den))
        {
            throw new McpException($"scale '{scale}' must look like 2:1, 1:2 or 2 (positive).");
        }
        return (num, den);
    }

    public static swDetViewStyle_e ParseDetailStyle(string style) => style.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
    {
        "standard" => swDetViewStyle_e.swDetViewSTANDARD,
        "broken" => swDetViewStyle_e.swDetViewBROKEN,
        "leader" => swDetViewStyle_e.swDetViewLEADER,
        "no_leader" or "noleader" => swDetViewStyle_e.swDetViewNOLEADER,
        "connected" => swDetViewStyle_e.swDetViewCONNECTED,
        _ => throw new McpException($"Unknown detail style '{style}'. Use standard, broken, leader, no_leader or connected."),
    };

    public static string DetailStyleName(int style) => (swDetViewStyle_e)style switch
    {
        swDetViewStyle_e.swDetViewSTANDARD => "standard",
        swDetViewStyle_e.swDetViewBROKEN => "broken",
        swDetViewStyle_e.swDetViewLEADER => "leader",
        swDetViewStyle_e.swDetViewNOLEADER => "no_leader",
        swDetViewStyle_e.swDetViewCONNECTED => "connected",
        _ => $"unknown ({style})",
    };

    public static swDetCircleShowType_e ParseCircleDisplay(string display) => display.Trim().ToLowerInvariant() switch
    {
        "circle" => swDetCircleShowType_e.swDetCircleCIRCLE,
        "profile" => swDetCircleShowType_e.swDetCirclePROFILE,
        "hidden" or "none" => swDetCircleShowType_e.swDetCircleDONTSHOW,
        _ => throw new McpException($"Unknown circleDisplay '{display}'. Use circle, profile or hidden."),
    };

    public static void RequireRadius(double radiusMm, double[] outlineMm, double[] centerMm)
    {
        if (!(radiusMm > 0) || double.IsInfinity(radiusMm))
        {
            throw new McpException("radiusMm must be > 0 (paper mm on the parent view).");
        }
        if (centerMm[0] < outlineMm[0] || centerMm[0] > outlineMm[2] || centerMm[1] < outlineMm[1] || centerMm[1] > outlineMm[3])
        {
            throw new McpException($"centerMm [{centerMm[0]}, {centerMm[1]}] is outside the parent view outline [{string.Join(", ", outlineMm.Take(4))}] mm.");
        }
    }

    /// <summary>
    /// Default centre for a detail view: right of the parent view, level with the circle centre. The detail is the
    /// circle magnified by detailScale / parentScale, so its half-width is radius × that factor.
    /// </summary>
    public static double[] DetailPlacement(double[] outlineMm, double[] centerMm, double radiusMm, double parentScale, double detailScale, double gapMm = 20)
    {
        double magnify = parentScale > 0 ? detailScale / parentScale : 1;
        return new[] { R(outlineMm[2] + gapMm + radiusMm * magnify), R(centerMm[1]) };
    }

    /// <summary>Letters used for view labels: A–Z without I, O and Q (easily confused with 1, 0 and O).</summary>
    private static readonly string Letters = "ABCDEFGHJKLMNPRSTUVWXYZ";

    /// <summary>The first label in A, B, … Z, AA, AB, … not already used by a section or detail view (case-insensitive).</summary>
    public static string NextLabel(IEnumerable<string?> used)
    {
        var taken = new HashSet<string>(used.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (char c in Letters)
        {
            if (!taken.Contains(c.ToString())) return c.ToString();
        }
        foreach (char a in Letters)
        {
            foreach (char b in Letters)
            {
                string l = $"{a}{b}";
                if (!taken.Contains(l)) return l;
            }
        }
        throw new McpException("No free view label left; pass label explicitly.");
    }

    private static double R(double v) => Math.Round(v, 6, MidpointRounding.AwayFromZero);
}
