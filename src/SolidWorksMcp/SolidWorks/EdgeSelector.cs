using ModelContextProtocol;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;

namespace SolidWorksMcp.SolidWorks;

/// <summary>Geometry of one model edge in mm, plus the live COM edge (STA thread only, never returned from a tool).</summary>
public sealed record EdgeGeometry(string Kind, double[] StartMm, double[] EndMm, double[] MidMm, double LengthMm, double? RadiusMm)
{
    internal Edge? Com { get; init; }

    /// <summary>
    /// Identity key. The same B-rep edge is reached once from each adjacent face and SolidWorks hands out a different
    /// COM wrapper each time, so object identity cannot de-duplicate it; endpoints (order-independent) + midpoint + length can.
    /// </summary>
    public string Key
    {
        get
        {
            static string P(double[] p) => string.Join(",", p.Select(v => Math.Round(v, 3).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)));
            string a = P(StartMm), b = P(EndMm);
            if (string.CompareOrdinal(a, b) > 0) (a, b) = (b, a);
            return $"{a}|{b}|{P(MidMm)}|{Math.Round(LengthMm, 3).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
    }

    public static double Distance(double[] a, double[] b)
        => Math.Sqrt(a.Zip(b, (x, y) => (x - y) * (x - y)).Sum());
}

/// <summary>Collects and filters model edges for fillets. Call only from the STA thread.</summary>
public static class EdgeSelector
{
    public static string NormalizeKind(string? kind) => (kind ?? "any").Trim().ToLowerInvariant() switch
    {
        "any" or "all" or "" => "any",
        "line" or "linear" or "straight" => "line",
        "circle" or "circular" or "arc" => "circle",
        "other" or "spline" => "other",
        var k => throw new McpException($"Unknown edge kind '{k}'. Use any, line, circle or other."),
    };

    /// <summary>Unique edges of one feature's faces, or of every solid body when <paramref name="feature"/> is null.</summary>
    public static List<EdgeGeometry> Collect(ModelDoc2 doc, string? feature, string kind = "any")
    {
        kind = NormalizeKind(kind);
        IEnumerable<Edge> raw;
        if (!string.IsNullOrWhiteSpace(feature))
        {
            Feature f = FeatureWalker.Find(doc, feature);
            var faces = f.GetFaces() as object[] ?? Array.Empty<object>();
            raw = faces.OfType<Face2>().SelectMany(face => (face.GetEdges() as object[] ?? Array.Empty<object>()).OfType<Edge>());
        }
        else
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Edge selection");
            var bodies = ((PartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[] ?? Array.Empty<object>();
            raw = bodies.OfType<Body2>().SelectMany(b => (b.GetEdges() as object[] ?? Array.Empty<object>()).OfType<Edge>());
        }

        var seen = new HashSet<string>();
        var result = new List<EdgeGeometry>();
        foreach (Edge e in raw)
        {
            var g = Describe(e);
            if (g is null || (kind != "any" && g.Kind != kind) || !seen.Add(g.Key))
            {
                continue;
            }
            result.Add(g);
        }
        return result;
    }

    private static EdgeGeometry? Describe(Edge e)
    {
        // IEdge::GetCurveParams2: [0..2] start point, [3..5] end point, [6] start param, [7] end param (metres).
        if (e.GetCurveParams2() is not double[] p || p.Length < 8 || e.GetCurve() is not Curve c)
        {
            return null;
        }
        double t0 = p[6], t1 = p[7];
        double[] mid = c.Evaluate2((t0 + t1) / 2, 0) is double[] m && m.Length >= 3 ? m[..3] : p[..3];
        string kind = c.IsLine() ? "line" : c.IsCircle() ? "circle" : "other";
        double? radius = kind == "circle" && c.CircleParams is double[] cp && cp.Length >= 7 ? Units.Round(Units.MToMm(cp[6])) : null;
        return new EdgeGeometry(kind, Units.MToMm(p[..3]), Units.MToMm(p[3..6]), Units.MToMm(mid),
            Units.Round(Units.MToMm(c.GetLength3(t0, t1))), radius) { Com = e };
    }

    /// <summary>For each point (mm), the edge nearest to it, if within <paramref name="toleranceMm"/>.</summary>
    public static List<EdgeGeometry> Nearest(IReadOnlyList<EdgeGeometry> candidates, IReadOnlyList<double[]> pointsMm, double toleranceMm,
        Func<EdgeGeometry, double[], double> distanceMm)
    {
        var picked = new List<EdgeGeometry>();
        for (int i = 0; i < pointsMm.Count; i++)
        {
            double[] pt = pointsMm[i];
            if (pt.Length != 3)
            {
                throw new McpException($"points[{i}] must be [x, y, z] in mm.");
            }
            var best = candidates.Select(c => (Edge: c, D: distanceMm(c, pt))).OrderBy(x => x.D).FirstOrDefault();
            if (best.Edge is null || best.D > toleranceMm)
            {
                throw new McpException($"No edge within {toleranceMm} mm of point {i} [{string.Join(", ", pt)}]" +
                    (best.Edge is null ? "." : $" (nearest is {Units.Round(best.D, 3)} mm away). Use sw_list_edges to see edge midpoints."));
            }
            if (!picked.Any(p => p.Key == best.Edge.Key))
            {
                picked.Add(best.Edge);
            }
        }
        return picked;
    }

    /// <summary>Exact distance from a point to the B-rep edge, via IEdge::GetClosestPointOn.</summary>
    public static double ComDistanceMm(EdgeGeometry edge, double[] pointMm)
    {
        if (edge.Com?.GetClosestPointOn(Units.MmToM(pointMm[0]), Units.MmToM(pointMm[1]), Units.MmToM(pointMm[2])) is double[] q && q.Length >= 3)
        {
            return EdgeGeometry.Distance(Units.MToMm(q[..3]), pointMm);
        }
        return EdgeGeometry.Distance(edge.MidMm, pointMm);
    }
}
