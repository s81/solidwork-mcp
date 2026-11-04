using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class SheetMetalTools(SolidWorksSession sw)
{
    [McpServerTool(Name = "sw_get_sheet_metal_info", Title = "Sheet metal info", ReadOnly = true, OpenWorld = false)]
    [Description("Read a sheet metal part's parameters (thickness, bend radius, K-factor / bend allowance), whether it is flattened, and its cut-list data: flat-pattern bounding box length/width/area, cutting lengths, cut-outs and bend count. Use for laser/punch costing and quotes. Cut-list values are in the document's units.")]
    public Task<object> GetSheetMetalInfo(
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Sheet metal info");
            var sheetMetals = FeatureWalker.Walk(doc, includeSubFeatures: false).Select(x => x.Feature)
                .Where(f => f.GetTypeName2() == "SheetMetal").ToList();
            if (sheetMetals.Count == 0)
            {
                throw new McpException($"'{doc.GetTitle()}' is not a sheet metal part. Create one with sw_base_flange.");
            }

            var parameters = sheetMetals.Select(f =>
            {
                var d = f.GetDefinition() as SheetMetalFeatureData;
                return new
                {
                    feature = f.Name,
                    thicknessMm = d is null ? (double?)null : Units.Round(Units.MToMm(d.Thickness)),
                    bendRadiusMm = d is null ? (double?)null : Units.Round(Units.MToMm(d.BendRadius)),
                    bendAllowanceType = d is null ? null : SheetMetalSpec.BendAllowanceName(d.BendAllowanceType),
                    kFactor = d is not null && d.BendAllowanceType == (int)swBendAllowanceTypes_e.swBendAllowanceKFactor ? Units.Round(d.KFactor, 4) : (double?)null,
                    bendAllowanceMm = d is not null && d.BendAllowanceType == (int)swBendAllowanceTypes_e.swBendAllowanceDirect ? Units.Round(Units.MToMm(d.BendAllowance)) : (double?)null,
                };
            }).ToList();

            return new
            {
                document = doc.GetTitle(),
                flattened = FlatPattern(doc) is Feature fp ? !fp.IsSuppressed() : (bool?)null,
                sheetMetal = parameters,
                cutList = CutList.Read(doc),
            };
        }, ct);

    private static Feature? FlatPattern(ModelDoc2 doc)
        => FeatureWalker.Walk(doc, includeSubFeatures: false).Select(x => x.Feature).FirstOrDefault(f => f.GetTypeName2() == "FlatPattern");

    [McpServerTool(Name = "sw_base_flange", Title = "Sheet metal base flange", OpenWorld = false)]
    [Description("Create a sheet metal part's first feature from a sketch. A closed profile makes a flat blank (tab); an open profile (lines/arcs) is bent along its corners and extruded by depthMm (e.g. a U-channel or bracket). Sets thickness, inside bend radius and K-factor.")]
    public Task<object> BaseFlange(
        [Description("Sketch name, e.g. Sketch1.")] string sketch,
        [Description("Sheet thickness in mm.")] double thicknessMm,
        [Description("Inside bend radius in mm.")] double bendRadiusMm,
        [Description("K-factor (neutral axis / thickness), typically 0.3-0.5. Default 0.5.")] double kFactor = 0.5,
        [Description("Open profiles only: extrusion depth in mm.")] double? depthMm = null,
        [Description("Open profiles only: blind or mid_plane.")] string endCondition = "blind",
        [Description("Add the thickness to the other side of the sketch.")] bool reverseThickness = false,
        [Description("Open profiles only: extrude in the opposite direction.")] bool reverseDirection = false,
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        SheetMetalSpec.ValidateThickness(thicknessMm);
        SheetMetalSpec.ValidateBendRadius(bendRadiusMm);
        SheetMetalSpec.ValidateKFactor(kFactor);
        if (depthMm is <= 0)
        {
            throw new McpException("depthMm must be > 0.");
        }
        var end = endCondition.Trim().ToLowerInvariant() switch
        {
            "blind" => swEndConditions_e.swEndCondBlind,
            "mid_plane" or "midplane" => swEndConditions_e.swEndCondMidPlane,
            _ => throw new McpException("endCondition must be blind or mid_plane."),
        };

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Base flange");
            Feature sketchFeature = FeatureWalker.Find(doc, sketch);
            if (sketchFeature.GetTypeName2() != "ProfileFeature")
            {
                throw new McpException($"'{sketch}' is not a 2D sketch.");
            }
            doc.ClearSelection2(true);
            sketchFeature.Select2(false, 0);

            var fm = doc.FeatureManager;
            CustomBendAllowance cba = fm.CreateCustomBendAllowance();
            cba.Type = (int)swBendAllowanceTypes_e.swBendAllowanceKFactor;
            cba.KFactor = kFactor;

            double d = Units.MmToM(depthMm ?? 10);
            // InsertSheetMetalBaseFlange2(Thickness, ThickenDir, Radius, ExtrudeDist1, ExtrudeDist2, FlipExtruDir,
            //   EndCondition1, EndCondition2, DirToUse, PCBA, UseDefaultRelief, ReliefType, ReliefWidth, ReliefDepth,
            //   ReliefRatio, UseReliefRatio, Merge, UseFeatScope, UseAutoSelect). Extrusion values are ignored for closed profiles.
            var feature = fm.InsertSheetMetalBaseFlange2(
                Units.MmToM(thicknessMm), reverseThickness, Units.MmToM(bendRadiusMm),
                d, 0, reverseDirection,
                (int)end, (int)swEndConditions_e.swEndCondBlind, 0,
                cba, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0, 0, 0.5, true,
                true, true, true);
            doc.ClearSelection2(true);
            if (feature is null)
            {
                throw new McpException("Base flange failed. Use a closed, non-intersecting profile, or an open profile of lines/arcs with depthMm, and a bend radius that fits the corners.");
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
                type = feature.GetTypeName2(),
                thicknessMm,
                bendRadiusMm,
                kFactor,
                status = code == 0 ? "ok" : (isWarning ? "warning: " : "error: ") + (swFeatureError_e)code,
            };
        }, ct);
    }

    [McpServerTool(Name = "sw_edge_flange", Title = "Sheet metal edge flange", OpenWorld = false)]
    [Description("Add flanges to straight edges of a sheet metal part. Pick edges by points like sw_fillet: the edge nearest each [x,y,z] mm point (use midpoints from sw_list_edges on the base flange). Length, angle and how the flange sits relative to the edge are set per call. The flange bends away from the face the edge belongs to (SolidWorks' default direction); to bend the other way, pick the matching edge on the opposite face of the sheet.")]
    public Task<object> EdgeFlange(
        [Description("Points in mm, one per edge, e.g. [[50,0,1.5]]. Pick edges on the same face of the sheet for consistent results.")] double[][] points,
        [Description("Flange length in mm.")] double lengthMm,
        [Description("Flange angle in degrees (90 = right angle).")] double angleDegrees = 90,
        [Description("material_inside | material_outside | bend_outside | bend_centerline | bend_tangent")] string position = "material_inside",
        [Description("How length is measured: outer_virtual_sharp | inner_virtual_sharp | tangent")] string lengthReference = "outer_virtual_sharp",
        [Description("Inside bend radius in mm. Omit to use the part's default.")] double? bendRadiusMm = null,
        [Description("Max distance in mm from a point to its edge.")] double toleranceMm = 0.5,
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        SheetMetalSpec.ValidateEdgeFlange(lengthMm, angleDegrees);
        SheetMetalSpec.ValidateBendRadius(bendRadiusMm);
        var pos = SheetMetalSpec.ParsePosition(position);
        var lengthRef = SheetMetalSpec.ParseLengthReference(lengthReference);
        if (points is null || points.Length == 0)
        {
            throw new McpException("points must contain at least one [x, y, z] in mm.");
        }

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Edge flange");
            if (!FeatureWalker.Walk(doc, includeSubFeatures: false).Any(x => x.Feature.GetTypeName2() == "SheetMetal"))
            {
                throw new McpException($"'{doc.GetTitle()}' is not a sheet metal part. Start with sw_base_flange.");
            }
            var edges = EdgeSelector.Nearest(EdgeSelector.Collect(doc, null), points, toleranceMm, EdgeSelector.ComDistanceMm);
            var notStraight = edges.FirstOrDefault(e => e.Kind != "line");
            if (notStraight is not null)
            {
                throw new McpException($"Edge flanges need straight edges; the edge at mid point [{string.Join(", ", notStraight.MidMm)}] is a {notStraight.Kind}.");
            }

            var data = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmEdgeFlange) as EdgeFlangeFeatureData
                ?? throw new McpException("SolidWorks did not return an edge flange definition.");
            data.BendAngle = Units.DegToRad(angleDegrees);
            data.OffsetType = (int)swFlangeOffsetTypes_e.swFlangeOffsetBlind;
            data.OffsetDistance = Units.MmToM(lengthMm);
            data.OffsetDimType = (int)lengthRef;
            data.PositionType = (int)pos;
            data.UseDefaultBendRadius = bendRadiusMm is null;
            if (bendRadiusMm is double r)
            {
                data.BendRadius = Units.MmToM(r);
            }
            data.UseDefaultBendAllowance = true;
            data.UseDefaultBendRelief = true;
            // No profile sketches: SolidWorks generates the default rectangular profile from length/angle.
            int added = data.AddEdges(edges.Select(e => (object)e.Com!).ToArray(), null);
            if (added <= 0)
            {
                throw new McpException("SolidWorks rejected the selected edges for an edge flange (they must be linear edges on the sheet's outline).");
            }

            doc.ClearSelection2(true);
            var feature = doc.FeatureManager.CreateFeature(data) as Feature;
            doc.ClearSelection2(true);
            if (feature is null)
            {
                throw new McpException("Edge flange failed. Check that the edges are on the sheet outline and the flange does not collide with existing geometry; try the matching edge on the opposite face of the sheet or a different position.");
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
                type = feature.GetTypeName2(),
                edges = edges.Count,
                lengthMm,
                angleDegrees,
                position = position.Trim().ToLowerInvariant(),
                status = code == 0 ? "ok" : (isWarning ? "warning: " : "error: ") + (swFeatureError_e)code,
            };
        }, ct);
    }

    [McpServerTool(Name = "sw_set_flattened", Title = "Flatten / fold sheet metal", Idempotent = true, OpenWorld = false)]
    [Description("Show a sheet metal part flattened (flat pattern) or folded, by unsuppressing or suppressing its Flat-Pattern feature.")]
    public Task<string> SetFlattened(
        [Description("true = flatten, false = fold.")] bool flattened,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Flatten");
            Feature fp = FlatPattern(doc) ?? throw new McpException($"'{doc.GetTitle()}' has no Flat-Pattern feature (not a sheet metal part?).");
            bool ok = fp.SetSuppression2(
                (int)(flattened ? swFeatureSuppressionAction_e.swUnSuppressFeature : swFeatureSuppressionAction_e.swSuppressFeature),
                (int)swInConfigurationOpts_e.swThisConfiguration, null);
            if (!ok)
            {
                throw new McpException($"Could not {(flattened ? "flatten" : "fold")} the part.");
            }
            doc.EditRebuild3();
            doc.ViewZoomtofit2();
            return $"{doc.GetTitle()} is now {(fp.IsSuppressed() ? "folded" : "flattened")}";
        }, ct);

    [McpServerTool(Name = "sw_set_sheet_metal_parameters", Title = "Set sheet metal parameters", Idempotent = true, OpenWorld = false)]
    [Description("Change thickness, default inside bend radius and/or K-factor of a sheet metal part (its first Sheet-Metal feature unless one is named). Bends that use the default radius follow the change. Omitted values stay as they are.")]
    public Task<object> SetSheetMetalParameters(
        [Description("New thickness in mm.")] double? thicknessMm = null,
        [Description("New default inside bend radius in mm.")] double? bendRadiusMm = null,
        [Description("New K-factor (0-1 exclusive). Switches the bend allowance type to K-factor.")] double? kFactor = null,
        [Description("Sheet-Metal feature name when the part has several bodies (e.g. Sheet-Metal2).")] string? feature = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        if (thicknessMm is null && bendRadiusMm is null && kFactor is null)
        {
            throw new McpException("Give at least one of thicknessMm, bendRadiusMm or kFactor.");
        }
        SheetMetalSpec.ValidateThickness(thicknessMm);
        SheetMetalSpec.ValidateBendRadius(bendRadiusMm);
        SheetMetalSpec.ValidateKFactor(kFactor);

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Sheet metal parameters");
            Feature f = feature is not null
                ? FeatureWalker.Find(doc, feature)
                : FeatureWalker.Walk(doc, includeSubFeatures: false).Select(x => x.Feature).FirstOrDefault(x => x.GetTypeName2() == "SheetMetal")
                    ?? throw new McpException($"'{doc.GetTitle()}' is not a sheet metal part.");
            var def = f.GetDefinition() as SheetMetalFeatureData
                ?? throw new McpException($"'{f.Name}' is not a Sheet-Metal feature.");

            if (!def.AccessSelections(doc, null))
            {
                throw new McpException($"Could not edit '{f.Name}'.");
            }
            bool modified;
            try
            {
                if (thicknessMm is double t) def.Thickness = Units.MmToM(t);
                if (bendRadiusMm is double r) def.BendRadius = Units.MmToM(r);
                if (kFactor is double k)
                {
                    CustomBendAllowance cba = def.GetCustomBendAllowance();
                    cba.Type = (int)swBendAllowanceTypes_e.swBendAllowanceKFactor;
                    cba.KFactor = k;
                    def.SetCustomBendAllowance(cba);
                }
                modified = f.ModifyDefinition(def, doc, null);
            }
            finally
            {
                // ModifyDefinition releases access on success; release explicitly otherwise.
                def.ReleaseSelectionAccess();
            }
            if (!modified)
            {
                throw new McpException($"SolidWorks rejected the new parameters for '{f.Name}'. If the part uses a gauge table or material sheet metal parameters, thickness and radius may be locked to the table.");
            }
            doc.EditRebuild3();

            var after = f.GetDefinition() as SheetMetalFeatureData;
            return new
            {
                feature = f.Name,
                thicknessMm = after is null ? (double?)null : Units.Round(Units.MToMm(after.Thickness)),
                bendRadiusMm = after is null ? (double?)null : Units.Round(Units.MToMm(after.BendRadius)),
                kFactor = after?.BendAllowanceType == (int)swBendAllowanceTypes_e.swBendAllowanceKFactor ? Units.Round(after.KFactor, 4) : (double?)null,
            };
        }, ct);
    }
}
