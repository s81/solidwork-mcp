using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class HoleTools(SolidWorksSession sw)
{
    // A position point further than this from the face's plane is almost certainly a mistake (wrong face or wrong axis).
    private const double PlaneToleranceMm = 0.05;

    [McpServerTool(Name = "sw_hole_wizard", Title = "Hole Wizard (ISO)", OpenWorld = false)]
    [Description("Create ISO standard holes with the Hole Wizard on a planar face of a part: clearance, tapped, counterbore (socket head cap screw) or countersink (countersunk socket screw). Standard dimensions (counterbore diameter/depth, tap drill, countersink) come from SolidWorks' ISO database for the given size. All points go into one feature; the first point picks the face, so it must lie on that face.")]
    public Task<object> HoleWizard(
        [Description("clearance | tapped | counterbore | countersink")] string type,
        [Description("ISO metric size, e.g. M6, M10, M8x1.0.")] string size,
        [Description("Hole centres in mm, model coordinates, all on the same planar face, e.g. [[25,25,10],[75,25,10]].")] double[][] points,
        [Description("through_all or blind.")] string endCondition = "through_all",
        [Description("Depth in mm for blind holes (for tapped holes: tap drill depth).")] double? depthMm = null,
        [Description("Tapped holes only: thread depth in mm. Omit for the SolidWorks default.")] double? threadDepthMm = null,
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        var spec = HoleSpec.Parse(type, size, endCondition, depthMm, threadDepthMm);
        HoleSpec.ValidatePoints(points);

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Hole Wizard");
            var fm = doc.FeatureManager;

            var data = fm.CreateDefinition((int)swFeatureNameID_e.swFmHoleWzd) as WizardHoleFeatureData2
                ?? throw new McpException("SolidWorks did not return a Hole Wizard definition.");
            // InitializeHole loads the standard's default dimensions for this fastener and size.
            data.InitializeHole((int)spec.HoleType, (int)swWzdHoleStandards_e.swStandardISO, (int)spec.FastenerType, spec.Size, (int)spec.EndCondition);
            if (!string.Equals(data.FastenerSize, spec.Size, StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException($"Size {spec.Size} is not in the ISO {spec.Kind} table (SolidWorks returned '{data.FastenerSize}').");
            }
            if (spec.DepthMm is double depth)
            {
                if (spec.HoleType == swWzdGeneralHoleTypes_e.swWzdTap)
                {
                    data.TapDrillDepth = Units.MmToM(depth);
                }
                else
                {
                    data.Depth = Units.MmToM(depth);
                }
            }
            if (spec.ThreadDepthMm is double thread)
            {
                data.ThreadDepth = Units.MmToM(thread);
            }

            // The selected face and pick point give the placement plane and the first hole centre.
            double[] first = points[0];
            doc.ClearSelection2(true);
            if (!doc.Extension.SelectByID2(string.Empty, "FACE", Units.MmToM(first[0]), Units.MmToM(first[1]), Units.MmToM(first[2]), false, 0, null, 0))
            {
                throw new McpException($"No face found at points[0] [{string.Join(", ", first)}] mm. The first point must lie on a planar face of the part.");
            }

            // Check the face is planar and every point lies on its plane before creating anything.
            var picked = ((SelectionMgr)doc.SelectionManager).GetSelectedObject6(1, -1) as Face2;
            if (picked?.GetSurface() is not Surface surface || !surface.IsPlane())
            {
                doc.ClearSelection2(true);
                throw new McpException("The face at points[0] is not planar. Hole Wizard holes need a flat face.");
            }
            // ISurface::PlaneParams: [0..2] normal, [3..5] a point on the plane (metres).
            if (surface.PlaneParams is double[] pp && pp.Length >= 6)
            {
                int bad = HoleSpec.FirstOffPlane(pp[..3], Units.MToMm(pp[3..6]), points, PlaneToleranceMm);
                if (bad >= 0)
                {
                    doc.ClearSelection2(true);
                    double off = HoleSpec.PlaneDistanceMm(pp[..3], Units.MToMm(pp[3..6]), points[bad]);
                    throw new McpException($"points[{bad}] is {Units.Round(off, 3)} mm off the plane of the face picked by points[0]; all points must lie on that face. Nothing was created.");
                }
            }

            var feature = fm.CreateFeature(data) as Feature;
            doc.ClearSelection2(true);
            if (feature is null)
            {
                throw new McpException("Hole Wizard failed. The face at points[0] must be planar and the hole must fit (e.g. the counterbore must not run off the part).");
            }

            int placed = 1;
            if (points.Length > 1)
            {
                placed = AddPositions(app, doc, feature, points);
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                feature.Name = name;
            }
            doc.EditRebuild3();

            int code = feature.GetErrorCode2(out bool isWarning);
            return new
            {
                feature = feature.Name,
                type = spec.Kind,
                standard = "ISO",
                size = spec.Size,
                holes = placed,
                endCondition = spec.EndCondition == swEndConditions_e.swEndCondBlind ? "blind" : "through_all",
                dimensionsMm = ReadBack(feature, spec),
                status = code == 0 ? "ok" : (isWarning ? "warning: " : "error: ") + (swFeatureError_e)code,
            };
        }, ct);
    }

    /// <summary>Add points[1..] to the hole's position sketch so every hole belongs to one feature.</summary>
    private static int AddPositions(SldWorks app, ModelDoc2 doc, Feature hole, double[][] points)
    {
        Feature? positionSketch = null;
        for (var sub = hole.GetFirstSubFeature() as Feature; sub is not null; sub = sub.GetNextSubFeature() as Feature)
        {
            if (sub.GetTypeName2() == "ProfileFeature")
            {
                positionSketch = sub;
                break;
            }
        }
        if (positionSketch?.GetSpecificFeature2() is not Sketch sketch)
        {
            throw new McpException($"Created {hole.Name} with one hole, but could not find its position sketch to add the other {points.Length - 1}.");
        }

        var math = (MathUtility)app.GetMathUtility();
        MathTransform toSketch = sketch.ModelToSketchTransform;

        // Convert and check every point before editing, so a bad point leaves the feature untouched.
        var sketchPoints = new List<double[]>();
        for (int i = 1; i < points.Length; i++)
        {
            var mp = (MathPoint)math.CreatePoint(points[i].Select(Units.MmToM).ToArray());
            var sp = (double[])((MathPoint)mp.MultiplyTransform(toSketch)).ArrayData;
            double offPlaneMm = Math.Abs(Units.MToMm(sp[2]));
            if (offPlaneMm > PlaneToleranceMm)
            {
                throw new McpException($"Created {hole.Name} at points[0], but points[{i}] is {Units.Round(offPlaneMm, 3)} mm off the face's plane; all points must lie on the same face. Delete the feature or fix the point and call again.");
            }
            sketchPoints.Add(sp);
        }

        doc.ClearSelection2(true);
        positionSketch.Select2(false, 0);
        doc.EditSketch();
        SketchManager sm = doc.SketchManager;
        bool addToDb = sm.AddToDB;
        sm.AddToDB = true;
        try
        {
            foreach (double[] sp in sketchPoints)
            {
                if (sm.CreatePoint(sp[0], sp[1], 0) is null)
                {
                    throw new McpException("SolidWorks rejected a hole position point.");
                }
            }
        }
        finally
        {
            sm.AddToDB = addToDb;
            sm.InsertSketch(true); // exit the sketch even on failure
            doc.ClearSelection2(true);
        }
        return points.Length;
    }

    private static Dictionary<string, double> ReadBack(Feature feature, HoleSpec spec)
    {
        var result = new Dictionary<string, double>();
        if (feature.GetDefinition() is not WizardHoleFeatureData2 d)
        {
            return result;
        }
        void Add(string key, double metres)
        {
            if (metres > 0)
            {
                result[key] = Units.Round(Units.MToMm(metres), 3);
            }
        }
        switch (spec.HoleType)
        {
            case swWzdGeneralHoleTypes_e.swWzdTap:
                Add("tapDrillDiameter", d.TapDrillDiameter);
                Add("threadDiameter", d.ThreadDiameter);
                Add("threadDepth", d.ThreadDepth);
                Add("tapDrillDepth", d.TapDrillDepth);
                break;
            case swWzdGeneralHoleTypes_e.swWzdCounterBore:
                Add("holeDiameter", d.ThruHoleDiameter);
                Add("counterboreDiameter", d.CounterBoreDiameter);
                Add("counterboreDepth", d.CounterBoreDepth);
                break;
            case swWzdGeneralHoleTypes_e.swWzdCounterSink:
                Add("holeDiameter", d.ThruHoleDiameter);
                Add("countersinkDiameter", d.CounterSinkDiameter);
                if (d.CounterSinkAngle > 0) result["countersinkAngleDeg"] = Units.Round(Units.RadToDeg(d.CounterSinkAngle), 3);
                break;
            default:
                Add("holeDiameter", d.HoleDiameter > 0 ? d.HoleDiameter : d.Diameter);
                break;
        }
        if (spec.EndCondition == swEndConditions_e.swEndCondBlind && spec.HoleType != swWzdGeneralHoleTypes_e.swWzdTap)
        {
            Add("depth", d.Depth);
        }
        return result;
    }
}
