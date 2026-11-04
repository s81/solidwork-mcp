using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

/// <summary>One 2D sketch entity. Coordinates/sizes in mm, in the sketch plane relative to the origin.</summary>
public sealed class SketchEntity
{
    [Description("line | centerline | corner_rectangle | center_rectangle | circle | arc | polygon | slot | point")]
    public string Type { get; set; } = string.Empty;

    [Description("line/centerline/corner_rectangle/slot: start X; arc: start point X")] public double? X1 { get; set; }
    [Description("start Y")] public double? Y1 { get; set; }
    [Description("line/centerline/corner_rectangle/slot: end X (opposite corner for rectangle); arc: end point X")] public double? X2 { get; set; }
    [Description("end Y")] public double? Y2 { get; set; }
    [Description("center X (center_rectangle, circle, arc, polygon) or point X")] public double? Cx { get; set; }
    [Description("center Y or point Y")] public double? Cy { get; set; }
    [Description("circle/polygon radius (polygon: circumscribed radius); slot: width/2")] public double? Radius { get; set; }
    [Description("center_rectangle width")] public double? Width { get; set; }
    [Description("center_rectangle height")] public double? Height { get; set; }
    [Description("polygon number of sides (3-40)")] public int? Sides { get; set; }
    [Description("arc direction: true = clockwise, false = counter-clockwise (default)")] public bool? Clockwise { get; set; }
}

[McpServerToolType]
public sealed class ModelingTools(SolidWorksSession sw)
{
    [McpServerTool(Name = "sw_create_sketch", Title = "Create sketch", OpenWorld = false)]
    [Description("Create a 2D sketch on a plane of the active part from a list of entities (mm), close it, and return its name for sw_extrude / sw_cut_extrude. Closed profiles are needed for extrusions.")]
    public Task<object> CreateSketch(
        [Description("front | top | right, or the name of a reference plane feature.")] string plane,
        [Description("Entities to draw, e.g. [{\"type\":\"center_rectangle\",\"cx\":0,\"cy\":0,\"width\":100,\"height\":50},{\"type\":\"circle\",\"cx\":0,\"cy\":0,\"radius\":10}]")] SketchEntity[] entities,
        [Description("Optional new name for the sketch feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        if (entities is null || entities.Length == 0)
        {
            throw new McpException("entities must contain at least one sketch entity.");
        }
        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Sketching");
            Feature planeFeature = FeatureWalker.FindPlane(doc, plane);
            doc.ClearSelection2(true);
            if (!planeFeature.Select2(false, 0))
            {
                throw new McpException($"Could not select plane '{planeFeature.Name}'.");
            }

            SketchManager sm = doc.SketchManager;
            sm.InsertSketch(true);
            bool addToDb = sm.AddToDB;
            bool display = sm.DisplayWhenAdded;
            // AddToDB bypasses snapping/inference so geometry lands exactly at the given coordinates.
            sm.AddToDB = true;
            sm.DisplayWhenAdded = false;
            var created = new List<string>();
            string sketchName;
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    created.Add(Draw(sm, entities[i], i));
                }
                sketchName = ((Feature)sm.ActiveSketch).Name;
            }
            finally
            {
                sm.AddToDB = addToDb;
                sm.DisplayWhenAdded = display;
                sm.InsertSketch(true); // exit sketch (also on failure, so the document is not left in sketch mode)
                doc.ClearSelection2(true);
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var f = FeatureWalker.Find(doc, sketchName);
                f.Name = name;
                sketchName = f.Name;
            }
            doc.GraphicsRedraw2();
            return new { sketch = sketchName, plane = planeFeature.Name, entities = created };
        }, ct);
    }

    private static string Draw(SketchManager sm, SketchEntity e, int index)
    {
        double R(double? v, string field) => Units.MmToM(v ?? throw new McpException($"entities[{index}] ({e.Type}) requires '{field}'."));
        object? result = e.Type.Trim().ToLowerInvariant() switch
        {
            "line" => sm.CreateLine(R(e.X1, "x1"), R(e.Y1, "y1"), 0, R(e.X2, "x2"), R(e.Y2, "y2"), 0),
            "centerline" => sm.CreateCenterLine(R(e.X1, "x1"), R(e.Y1, "y1"), 0, R(e.X2, "x2"), R(e.Y2, "y2"), 0),
            "corner_rectangle" => sm.CreateCornerRectangle(R(e.X1, "x1"), R(e.Y1, "y1"), 0, R(e.X2, "x2"), R(e.Y2, "y2"), 0),
            "center_rectangle" => sm.CreateCenterRectangle(R(e.Cx, "cx"), R(e.Cy, "cy"), 0,
                R(e.Cx, "cx") + R(e.Width, "width") / 2, R(e.Cy, "cy") + R(e.Height, "height") / 2, 0),
            "circle" => sm.CreateCircleByRadius(R(e.Cx, "cx"), R(e.Cy, "cy"), 0, R(e.Radius, "radius")),
            "arc" => sm.CreateArc(R(e.Cx, "cx"), R(e.Cy, "cy"), 0, R(e.X1, "x1"), R(e.Y1, "y1"), 0, R(e.X2, "x2"), R(e.Y2, "y2"), 0,
                (short)(e.Clockwise == true ? -1 : 1)),
            "polygon" => sm.CreatePolygon(R(e.Cx, "cx"), R(e.Cy, "cy"), 0, R(e.Cx, "cx") + R(e.Radius, "radius"), R(e.Cy, "cy"), 0,
                e.Sides is >= 3 and <= 40 ? e.Sides.Value : throw new McpException($"entities[{index}] polygon requires sides 3-40."), false),
            "slot" => sm.CreateSketchSlot((int)swSketchSlotCreationType_e.swSketchSlotCreationType_line,
                (int)swSketchSlotLengthType_e.swSketchSlotLengthType_CenterCenter, 2 * R(e.Radius, "radius"),
                R(e.X1, "x1"), R(e.Y1, "y1"), 0, R(e.X2, "x2"), R(e.Y2, "y2"), 0, 0, 0, 0, 1, false),
            "point" => sm.CreatePoint(R(e.Cx, "cx"), R(e.Cy, "cy"), 0),
            _ => throw new McpException($"entities[{index}]: unknown type '{e.Type}'."),
        };
        if (result is null)
        {
            throw new McpException($"entities[{index}] ({e.Type}) could not be created (zero size or invalid geometry?).");
        }
        return e.Type;
    }

    [McpServerTool(Name = "sw_extrude", Title = "Extruded boss/base", OpenWorld = false)]
    [Description("Extrude a closed sketch into a solid (boss/base), merged with existing bodies.")]
    public Task<object> Extrude(
        [Description("Sketch name, e.g. Sketch1.")] string sketch,
        [Description("Depth in mm (ignored for through_all).")] double depth,
        [Description("blind | mid_plane | through_all")] string endCondition = "blind",
        [Description("Reverse the extrusion direction.")] bool reverse = false,
        [Description("Draft angle in degrees (0 = none).")] double draftDegrees = 0,
        [Description("Draft outward (profile grows along the extrusion) instead of inward.")] bool draftOutward = false,
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Extrusion");
            SelectSketch(doc, sketch);
            var end = EndCondition(endCondition, allowThroughAllBoth: false);
            double d = Units.MmToM(depth);
            if (end != swEndConditions_e.swEndCondThroughAll && d <= 0)
            {
                throw new McpException("depth must be > 0 mm.");
            }
            bool draft = Math.Abs(draftDegrees) > 1e-9;
            // FeatureExtrusion3(Sd, Flip, Dir, T1, T2, D1, D2, Dchk1, Dchk2, Ddir1, Ddir2, Dang1, Dang2,
            //   OffsetReverse1, OffsetReverse2, TranslateSurface1, TranslateSurface2, Merge, UseFeatScope, UseAutoSelect,
            //   T0, StartOffset, FlipStartOffset). Flip = flip side to cut (cuts only); Dir = reverse direction.
            // Ddir1: true = draft inward (tapers), false = outward.
            var feature = doc.FeatureManager.FeatureExtrusion3(
                true, false, reverse, (int)end, 0, d, 0,
                draft, false, !draftOutward, false, Units.DegToRad(Math.Abs(draftDegrees)), 0,
                false, false, false, false,
                true, true, true,
                (int)swStartConditions_e.swStartSketchPlane, 0, false) as Feature;
            return Finish(doc, feature, name, "Extrusion");
        }, ct);

    [McpServerTool(Name = "sw_cut_extrude", Title = "Extruded cut", OpenWorld = false)]
    [Description("Cut material using a closed sketch (holes, pockets, slots). Use through_all_both when unsure of direction.")]
    public Task<object> CutExtrude(
        [Description("Sketch name, e.g. Sketch2.")] string sketch,
        [Description("blind | mid_plane | through_all | through_all_both")] string endCondition = "through_all_both",
        [Description("Depth in mm for blind / mid_plane.")] double depth = 0,
        [Description("Reverse the cut direction.")] bool reverse = false,
        [Description("Cut away the material outside the profile instead of inside.")] bool flipSideToCut = false,
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Cut");
            SelectSketch(doc, sketch);
            var end = EndCondition(endCondition, allowThroughAllBoth: true);
            double d = Units.MmToM(depth);
            if (end is swEndConditions_e.swEndCondBlind or swEndConditions_e.swEndCondMidPlane && d <= 0)
            {
                throw new McpException("depth must be > 0 mm for blind/mid_plane cuts.");
            }
            bool both = end == swEndConditions_e.swEndCondThroughAllBoth;
            // FeatureCut4(Sd, Flip, Dir, T1, T2, D1, D2, Dchk1, Dchk2, Ddir1, Ddir2, Dang1, Dang2, OffsetReverse1, OffsetReverse2,
            //   TranslateSurface1, TranslateSurface2, NormalCut, UseFeatScope, UseAutoSelect, AssemblyFeatureScope,
            //   AutoSelectComponents, PropagateFeatureToParts, T0, StartOffset, FlipStartOffset, OptimizeGeometry)
            var feature = doc.FeatureManager.FeatureCut4(
                !both, flipSideToCut, reverse,
                (int)(both ? swEndConditions_e.swEndCondThroughAll : end),
                (int)(both ? swEndConditions_e.swEndCondThroughAll : swEndConditions_e.swEndCondBlind),
                d, 0,
                false, false, false, false, 0, 0,
                false, false, false, false,
                false, true, true, false, false, false,
                (int)swStartConditions_e.swStartSketchPlane, 0, false, false) as Feature;
            return Finish(doc, feature, name, "Cut");
        }, ct);

    private static void SelectSketch(ModelDoc2 doc, string sketch)
    {
        doc.ClearSelection2(true);
        Feature f = FeatureWalker.Find(doc, sketch);
        if (f.GetTypeName2() is not ("ProfileFeature" or "3DProfileFeature"))
        {
            throw new McpException($"'{sketch}' is a {f.GetTypeName2()}, not a sketch.");
        }
        if (!f.Select2(false, 0))
        {
            throw new McpException($"Could not select sketch '{sketch}'.");
        }
    }

    internal static swEndConditions_e EndCondition(string value, bool allowThroughAllBoth) => value.Trim().ToLowerInvariant() switch
    {
        "blind" => swEndConditions_e.swEndCondBlind,
        "mid_plane" or "midplane" => swEndConditions_e.swEndCondMidPlane,
        "through_all" => swEndConditions_e.swEndCondThroughAll,
        "through_all_both" when allowThroughAllBoth => swEndConditions_e.swEndCondThroughAllBoth,
        _ => throw new McpException($"Unsupported end condition '{value}'."),
    };

    private static object Finish(ModelDoc2 doc, Feature? feature, string? name, string what)
    {
        doc.ClearSelection2(true);
        if (feature is null)
        {
            throw new McpException($"{what} failed. Check that the sketch is a closed, non-self-intersecting profile and that the depth/direction hit material (for cuts).");
        }
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
        };
    }

    public sealed record FeatureInfo(string Name, string Type, int Depth, bool Suppressed, string? Error);

    [McpServerTool(Name = "sw_list_features", Title = "List features", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the FeatureManager tree (name, type, suppression, rebuild errors). Sub-features such as consumed sketches appear with depth 1.")]
    public Task<IReadOnlyList<FeatureInfo>> ListFeatures(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("Include sub-features (sketches under extrudes etc.).")] bool includeSubFeatures = true,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<FeatureInfo>>(document, (app, doc) =>
            FeatureWalker.Walk(doc, includeSubFeatures).Select(x =>
            {
                int code = x.Feature.GetErrorCode2(out bool warn);
                return new FeatureInfo(x.Feature.Name, x.Feature.GetTypeName2(), x.Depth, x.Feature.IsSuppressed(),
                    code == 0 ? null : (warn ? "warning: " : "error: ") + (swFeatureError_e)code);
            }).ToList(), ct);

    [McpServerTool(Name = "sw_set_feature_suppression", Title = "Suppress/unsuppress feature", Idempotent = true, OpenWorld = false)]
    [Description("Suppress or unsuppress a feature in the active configuration.")]
    public Task<string> SetFeatureSuppression(
        [Description("Feature name.")] string feature,
        [Description("true = suppress, false = unsuppress.")] bool suppressed,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Feature f = FeatureWalker.Find(doc, feature);
            bool ok = f.SetSuppression2(
                (int)(suppressed ? swFeatureSuppressionAction_e.swSuppressFeature : swFeatureSuppressionAction_e.swUnSuppressFeature),
                (int)swInConfigurationOpts_e.swThisConfiguration, null);
            if (!ok)
            {
                throw new McpException($"Could not change suppression of '{feature}'.");
            }
            doc.EditRebuild3();
            return $"{f.Name} {(suppressed ? "suppressed" : "unsuppressed")}";
        }, ct);

    [McpServerTool(Name = "sw_delete_feature", Title = "Delete feature", Destructive = true, OpenWorld = false)]
    [Description("Delete a feature (and, optionally, its absorbed sketches/children).")]
    public Task<string> DeleteFeature(
        [Description("Feature name.")] string feature,
        [Description("Also delete absorbed features such as the sketch of an extrude.")] bool deleteAbsorbed = true,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Feature f = FeatureWalker.Find(doc, feature);
            doc.ClearSelection2(true);
            f.Select2(false, 0);
            int options = deleteAbsorbed ? (int)swDeleteSelectionOptions_e.swDelete_Absorbed : 0;
            if (!doc.Extension.DeleteSelection2(options))
            {
                throw new McpException($"Could not delete '{feature}'.");
            }
            doc.EditRebuild3();
            return $"Deleted {feature}";
        }, ct);

    public sealed record DimensionInfo(string FullName, string Name, string Feature, double Value, string Unit);

    [McpServerTool(Name = "sw_list_dimensions", Title = "List dimensions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List driving dimensions with their full names (e.g. D1@Sketch1) and values (mm or degrees). Use the full name with sw_set_dimension.")]
    public Task<IReadOnlyList<DimensionInfo>> ListDimensions(
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        [Description("Only dimensions of this feature (optional).")] string? feature = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<DimensionInfo>>(document, (app, doc) =>
        {
            var list = new List<DimensionInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (f, _) in FeatureWalker.Walk(doc))
            {
                if (feature is not null && !string.Equals(f.Name, feature, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var dd = f.GetFirstDisplayDimension() as DisplayDimension;
                while (dd is not null)
                {
                    if (dd.GetDimension2(0) is Dimension dim)
                    {
                        string full = dim.FullName;
                        if (seen.Add(full))
                        {
                            var (value, unit) = ToUser(dim);
                            list.Add(new DimensionInfo(full, dim.Name, f.Name, value, unit));
                        }
                    }
                    dd = f.GetNextDisplayDimension(dd) as DisplayDimension;
                }
            }
            return list;
        }, ct);

    internal static (double Value, string Unit) ToUser(Dimension dim)
    {
        double si = dim.SystemValue;
        return dim.GetType() switch
        {
            (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular => (Units.Round(Units.RadToDeg(si)), "deg"),
            (int)swDimensionParamType_e.swDimensionParamTypeDoubleLinear => (Units.Round(Units.MToMm(si)), "mm"),
            _ => (si, "unitless"),
        };
    }

    [McpServerTool(Name = "sw_set_dimension", Title = "Set dimension", Idempotent = true, OpenWorld = false)]
    [Description("Change a driving dimension (mm for lengths, degrees for angles) in the active configuration and rebuild.")]
    public Task<object> SetDimension(
        [Description("Dimension full name, e.g. D1@Sketch1 or D1@Boss-Extrude1.")] string dimension,
        [Description("New value in mm (linear) or degrees (angular).")] double value,
        [Description("Apply to all configurations instead of only the active one.")] bool allConfigurations = false,
        [Description("Path, file name or title. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            string lookup = StripDocSuffix(dimension, doc);
            var dim = doc.Parameter(lookup) as Dimension
                ?? throw new McpException($"Dimension '{dimension}' not found. Use sw_list_dimensions.");
            var (before, unit) = ToUser(dim);
            double si = unit switch
            {
                "deg" => Units.DegToRad(value),
                "mm" => Units.MmToM(value),
                _ => value,
            };
            int opt = (int)(allConfigurations
                ? swSetValueInConfiguration_e.swSetValue_InAllConfigurations
                : swSetValueInConfiguration_e.swSetValue_InThisConfiguration);
            int status = dim.SetSystemValue3(si, opt, null);
            if (status != (int)swSetValueReturnStatus_e.swSetValue_Successful)
            {
                throw new McpException($"Could not set {dimension}: {(swSetValueReturnStatus_e)status} (driven or read-only dimension?).");
            }
            doc.EditRebuild3();
            var (after, _) = ToUser(dim);
            return new { dimension = dim.FullName, before, after, unit };
        }, ct);

    // IModelDoc2::Parameter expects "D1@Sketch1"; FullName adds "@Part1.SLDPRT".
    internal static string StripDocSuffix(string dimension, ModelDoc2 doc)
    {
        string title = doc.GetTitle();
        string[] parts = dimension.Split('@');
        if (parts.Length >= 3)
        {
            string last = parts[^1];
            if (last.Equals(title, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(last).Equals(Path.GetFileNameWithoutExtension(title), StringComparison.OrdinalIgnoreCase))
            {
                return string.Join('@', parts[..^1]);
            }
        }
        return dimension;
    }
}
