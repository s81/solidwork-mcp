using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class FeatureTools(SolidWorksSession sw)
{
    [McpServerTool(Name = "sw_revolve", Title = "Revolve boss / revolve cut", OpenWorld = false)]
    [Description("Revolve a closed sketch profile around a centerline in the same sketch, as a boss (adds material) or a cut. Draw the profile on one side of the axis and add the axis with sw_create_sketch type 'centerline'. Typical uses: shafts, bushings, knobs, turned parts, grooves.")]
    public Task<object> Revolve(
        [Description("Sketch name containing the profile and a centerline, e.g. Sketch1.")] string sketch,
        [Description("Revolution angle in degrees (0 < angle <= 360).")] double angleDegrees = 360,
        [Description("one_direction or mid_plane (angle split equally on both sides of the sketch plane).")] string direction = "one_direction",
        [Description("Reverse the revolution direction (one_direction only).")] bool reverse = false,
        [Description("true = revolved cut (removes material), false = revolved boss.")] bool cut = false,
        [Description("Name of the centerline to use as axis (e.g. Line3) when the sketch has more than one. Omit to use its only centerline.")] string? axis = null,
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        if (angleDegrees <= 0 || angleDegrees > 360)
        {
            throw new McpException("angleDegrees must be > 0 and <= 360.");
        }
        var dir = direction.Trim().ToLowerInvariant() switch
        {
            "one_direction" or "one" or "blind" => swEndConditions_e.swEndCondBlind,
            "mid_plane" or "midplane" => swEndConditions_e.swEndCondMidPlane,
            _ => throw new McpException("direction must be one_direction or mid_plane."),
        };

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Revolve");
            Feature sketchFeature = FeatureWalker.Find(doc, sketch);
            if (sketchFeature.GetTypeName2() != "ProfileFeature" || sketchFeature.GetSpecificFeature2() is not Sketch sk)
            {
                throw new McpException($"'{sketch}' is not a 2D sketch.");
            }

            SketchSegment axisSegment = FindAxis(sk, axis);

            // FeatureRevolve2 (API help): the sketch is selected with mark 0 and the axis of revolution with mark 4.
            doc.ClearSelection2(true);
            if (!sketchFeature.Select2(false, 0))
            {
                throw new McpException($"Could not select sketch '{sketch}'.");
            }
            SelectData sd = ((SelectionMgr)doc.SelectionManager).CreateSelectData();
            sd.Mark = 4;
            if (!axisSegment.Select4(true, sd))
            {
                throw new McpException($"Could not select axis '{axisSegment.GetName()}'.");
            }

            // FeatureRevolve2(SingleDir, IsSolid, IsThin, IsCut, ReverseDir, BothDirectionUpToSameEntity, Dir1Type, Dir2Type,
            //   Dir1Angle, Dir2Angle, OffsetReverse1, OffsetReverse2, OffsetDistance1, OffsetDistance2, ThinType,
            //   ThinThickness1, ThinThickness2, Merge, UseFeatScope, UseAutoSelect)
            var feature = doc.FeatureManager.FeatureRevolve2(
                true, true, false, cut, reverse && dir == swEndConditions_e.swEndCondBlind, false,
                (int)dir, (int)swEndConditions_e.swEndCondBlind,
                Units.DegToRad(angleDegrees), 0,
                false, false, 0, 0,
                0, 0, 0,
                true, true, true) as Feature;
            doc.ClearSelection2(true);

            if (feature is null)
            {
                throw new McpException(cut
                    ? "Revolved cut failed. The profile must be closed, lie entirely on one side of the axis, and overlap existing material."
                    : "Revolve failed. The profile must be closed and lie entirely on one side of the axis (it may touch it but not cross it).");
            }
            return FeatureResult(feature, name, new { axis = axisSegment.GetName(), angleDegrees, direction = dir == swEndConditions_e.swEndCondMidPlane ? "mid_plane" : "one_direction", cut });
        }, ct);
    }

    private static SketchSegment FindAxis(Sketch sk, string? axis)
    {
        var segments = (sk.GetSketchSegments() as object[] ?? Array.Empty<object>()).OfType<SketchSegment>().ToList();
        if (!string.IsNullOrWhiteSpace(axis))
        {
            return segments.FirstOrDefault(s => string.Equals(s.GetName(), axis, StringComparison.OrdinalIgnoreCase))
                ?? throw new McpException($"Sketch segment '{axis}' not found. Centerlines in this sketch: {string.Join(", ", Centerlines(segments).Select(s => s.GetName()))}");
        }
        var centerlines = Centerlines(segments).ToList();
        return centerlines.Count switch
        {
            1 => centerlines[0],
            0 => throw new McpException("The sketch has no centerline. Add one with sw_create_sketch (type 'centerline') to define the axis of revolution."),
            _ => throw new McpException($"The sketch has {centerlines.Count} centerlines ({string.Join(", ", centerlines.Select(s => s.GetName()))}); pass 'axis'."),
        };
    }

    private static IEnumerable<SketchSegment> Centerlines(IEnumerable<SketchSegment> segments)
        => segments.Where(s => s.ConstructionGeometry && s.GetType() == (int)swSketchSegments_e.swSketchLINE);

    [McpServerTool(Name = "sw_list_edges", Title = "List edges", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the edges of a feature (or of all solid bodies) with type, start/end/mid points (mm), length and radius. Use the midpoints with sw_fillet selection 'points'.")]
    public Task<object> ListEdges(
        [Description("Feature name, e.g. Boss-Extrude1. Omit for every edge of every solid body.")] string? feature = null,
        [Description("any | line | circle | other")] string kind = "any",
        [Description("Maximum number of edges to return.")] int limit = 200,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Listing edges");
            var edges = EdgeSelector.Collect(doc, feature, kind);
            return new
            {
                total = edges.Count,
                truncated = edges.Count > limit,
                edges = edges.Take(Math.Max(1, limit)).Select((e, i) => new { index = i, kind = e.Kind, start = e.StartMm, end = e.EndMm, mid = e.MidMm, lengthMm = e.LengthMm, radiusMm = e.RadiusMm }),
            };
        }, ct);

    [McpServerTool(Name = "sw_fillet", Title = "Constant-radius fillet", OpenWorld = false)]
    [Description("Round edges with a constant-radius fillet. Choose edges by: 'feature' (all edges of a feature, optionally only lines or circles), 'all' (every edge of the part), or 'points' (the edge nearest each [x,y,z] mm point — use midpoints from sw_list_edges).")]
    public Task<object> Fillet(
        [Description("Fillet radius in mm.")] double radius,
        [Description("feature | all | points")] string selection,
        [Description("Feature whose edges to fillet (selection 'feature'), e.g. Boss-Extrude1.")] string? feature = null,
        [Description("Points in mm, one per edge (selection 'points'), e.g. [[50,25,10],[-50,25,10]].")] double[][]? points = null,
        [Description("Only edges of this kind: any | line | circle | other (selections 'feature' and 'all').")] string kind = "any",
        [Description("Max distance in mm from a point to its edge (selection 'points').")] double toleranceMm = 0.5,
        [Description("Also fillet edges tangent to the selected ones.")] bool tangentPropagation = true,
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        if (radius <= 0)
        {
            throw new McpException("radius must be > 0 mm.");
        }
        string mode = ValidateSelection(selection, feature, points);

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Fillet");
            List<EdgeGeometry> edges = ResolveEdges(doc, mode, feature, points, kind, toleranceMm);

            var data = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet) as SimpleFilletFeatureData2
                ?? throw new McpException("SolidWorks did not return a fillet definition.");
            data.Initialize((int)swSimpleFilletType_e.swConstRadiusFillet);
            data.DefaultRadius = Units.MmToM(radius);
            data.PropagateToTangentFaces = tangentPropagation;
            data.Edges = edges.Select(e => (object)e.Com!).ToArray();

            doc.ClearSelection2(true);
            var feat = doc.FeatureManager.CreateFeature(data) as Feature;
            doc.ClearSelection2(true);
            if (feat is null)
            {
                throw new McpException($"Fillet of {edges.Count} edge(s) at R{radius} mm failed. The radius may be too large for an adjacent face; try a smaller radius or fewer edges.");
            }
            return FeatureResult(feat, name, new { radiusMm = radius, edgeCount = edges.Count });
        }, ct);
    }

    internal static string ValidateSelection(string selection, string? feature, double[][]? points)
    {
        string mode = selection.Trim().ToLowerInvariant();
        if (mode is not ("feature" or "all" or "points"))
        {
            throw new McpException("selection must be feature, all or points.");
        }
        if (mode == "feature" && string.IsNullOrWhiteSpace(feature))
        {
            throw new McpException("selection 'feature' requires 'feature'.");
        }
        if (mode == "points" && (points is null || points.Length == 0))
        {
            throw new McpException("selection 'points' requires at least one point.");
        }
        return mode;
    }

    private static List<EdgeGeometry> ResolveEdges(ModelDoc2 doc, string mode, string? feature, double[][]? points, string kind, double toleranceMm)
    {
        List<EdgeGeometry> edges = mode switch
        {
            "feature" => EdgeSelector.Collect(doc, feature, kind),
            "all" => EdgeSelector.Collect(doc, null, kind),
            _ => EdgeSelector.Nearest(EdgeSelector.Collect(doc, feature), points!, toleranceMm, EdgeSelector.ComDistanceMm),
        };
        if (edges.Count == 0)
        {
            throw new McpException("No edges matched the selection.");
        }
        return edges;
    }

    internal static (swChamferType_e Type, double Width, double Angle, double OtherDist) ChamferParameters(
        string type, double distance, double? distance2, double angleDegrees)
    {
        if (distance <= 0)
        {
            throw new McpException("distance must be > 0 mm.");
        }
        switch (type.Trim().ToLowerInvariant().Replace('-', '_'))
        {
            case "equal_distance" or "equal":
                // Distance-distance with both sides equal: the long-standing equivalent of the newer swChamferEqualDistance type.
                return (swChamferType_e.swChamferDistanceDistance, Units.MmToM(distance), 0, Units.MmToM(distance));
            case "distance_distance":
                if (distance2 is not > 0)
                {
                    throw new McpException("type distance_distance requires distance2 > 0 mm.");
                }
                return (swChamferType_e.swChamferDistanceDistance, Units.MmToM(distance), 0, Units.MmToM(distance2.Value));
            case "angle_distance":
                if (angleDegrees <= 0 || angleDegrees >= 90)
                {
                    throw new McpException("angleDegrees must be between 0 and 90 (exclusive).");
                }
                return (swChamferType_e.swChamferAngleDistance, Units.MmToM(distance), Units.DegToRad(angleDegrees), 0);
            default:
                throw new McpException($"Unknown chamfer type '{type}'. Use equal_distance, distance_distance or angle_distance.");
        }
    }

    [McpServerTool(Name = "sw_chamfer", Title = "Chamfer edges", OpenWorld = false)]
    [Description("Bevel edges with a chamfer: equal distance (e.g. 1 x 45 deg), two distances, or distance + angle. Choose edges like sw_fillet: 'feature' (all edges of a feature, optionally only lines or circles), 'all' (every edge of the part), or 'points' (the edge nearest each [x,y,z] mm point; use midpoints from sw_list_edges).")]
    public Task<object> Chamfer(
        [Description("Chamfer distance in mm (first distance for distance_distance; the distance for angle_distance).")] double distance,
        [Description("feature | all | points")] string selection,
        [Description("equal_distance | distance_distance | angle_distance")] string type = "equal_distance",
        [Description("Second distance in mm (distance_distance only).")] double? distance2 = null,
        [Description("Angle in degrees, 0-90 exclusive (angle_distance only).")] double angleDegrees = 45,
        [Description("Swap which face each distance (or the angle) is measured on. Applies to distance_distance and angle_distance.")] bool flip = false,
        [Description("Feature whose edges to chamfer (selection 'feature'), e.g. Boss-Extrude1.")] string? feature = null,
        [Description("Points in mm, one per edge (selection 'points'), e.g. [[50,25,10],[-50,25,10]].")] double[][]? points = null,
        [Description("Only edges of this kind: any | line | circle | other (selections 'feature' and 'all').")] string kind = "any",
        [Description("Max distance in mm from a point to its edge (selection 'points').")] double toleranceMm = 0.5,
        [Description("Also chamfer edges tangent to the selected ones.")] bool tangentPropagation = true,
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        var (chamferType, width, angle, otherDist) = ChamferParameters(type, distance, distance2, angleDegrees);
        string mode = ValidateSelection(selection, feature, points);

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Chamfer");
            List<EdgeGeometry> edges = ResolveEdges(doc, mode, feature, points, kind, toleranceMm);

            // IFeatureManager::InsertFeatureChamfer acts on the current selection, so select the edges first (mark 0).
            doc.ClearSelection2(true);
            SelectData sd = ((SelectionMgr)doc.SelectionManager).CreateSelectData();
            sd.Mark = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Com is not Entity entity || !entity.Select4(i > 0, sd))
                {
                    doc.ClearSelection2(true);
                    throw new McpException($"Could not select edge {i} (mid point [{string.Join(", ", edges[i].MidMm)}] mm).");
                }
            }

            int options = 0;
            if (flip) options |= (int)swFeatureChamferOption_e.swFeatureChamferFlipDirection;
            if (tangentPropagation) options |= (int)swFeatureChamferOption_e.swFeatureChamferTangentPropagation;

            // InsertFeatureChamfer(Options, ChamferType, Width, Angle, OtherDist, VertexChamDist1..3); vertex distances unused for edge chamfers.
            var feat = doc.FeatureManager.InsertFeatureChamfer(options, (int)chamferType, width, angle, otherDist, 0, 0, 0);
            doc.ClearSelection2(true);
            if (feat is null)
            {
                throw new McpException($"Chamfer of {edges.Count} edge(s) failed. The distance may be larger than an adjacent face allows; try a smaller value or fewer edges.");
            }
            return FeatureResult(feat, name, new
            {
                type = type.Trim().ToLowerInvariant(),
                distanceMm = distance,
                distance2Mm = chamferType == swChamferType_e.swChamferDistanceDistance ? Units.Round(Units.MToMm(otherDist)) : (double?)null,
                angleDegrees = chamferType == swChamferType_e.swChamferAngleDistance ? angleDegrees : (double?)null,
                edgeCount = edges.Count,
            });
        }, ct);
    }

    private static object FeatureResult(Feature feature, string? name, object details)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            feature.Name = name;
        }
        int code = feature.GetErrorCode2(out bool isWarning);
        return new
        {
            feature = feature.Name,
            type = feature.GetTypeName2(),
            status = code == 0 ? "ok" : (isWarning ? "warning: " : "error: ") + (swFeatureError_e)code,
            details,
        };
    }
}
