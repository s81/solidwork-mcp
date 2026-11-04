using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class WeldmentTools(SolidWorksSession sw, PathGuard paths)
{
    private static IReadOnlyList<string> ProfileRoots(SldWorks app)
        => WeldmentProfiles.SplitRoots(app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsWeldmentProfiles));

    [McpServerTool(Name = "sw_list_weldment_profiles", Title = "List weldment profiles", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the structural profiles available to weldments (standard / type / size), read from the folder set in SolidWorks under Tools > Options > File Locations > Weldment Profiles. Use the names with sw_add_structural_member.")]
    public Task<object> ListWeldmentProfiles(
        [Description("Only this standard, e.g. iso or ansi inch (optional).")] string? standard = null,
        [Description("Only this type, e.g. square tube, angle iron, c channel (optional).")] string? type = null,
        CancellationToken ct = default)
        => sw.RunAsync<object>(app =>
        {
            var roots = ProfileRoots(app);
            var all = WeldmentProfiles.Scan(roots);
            var filtered = all
                .Where(p => standard is null || string.Equals(p.Standard, standard.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(p => type is null || string.Equals(p.Type, type.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            return new
            {
                folders = roots,
                total = filtered.Count,
                standards = filtered.GroupBy(p => p.Standard, StringComparer.OrdinalIgnoreCase).Select(g => new
                {
                    standard = g.Key,
                    types = g.GroupBy(p => p.Type, StringComparer.OrdinalIgnoreCase).Select(t => new { type = t.Key, sizes = t.Select(p => p.Size).ToList() }).ToList(),
                }).ToList(),
            };
        }, ct);

    [McpServerTool(Name = "sw_add_structural_member", Title = "Add structural member", OpenWorld = false)]
    [Description("Create weldment structural members (tube, angle, channel, beam...) along the lines of a sketch, using a profile from the weldment library. Creates the Weldment feature first if the part has none. All chosen segments form one group with one corner treatment. Draw the frame as a 2D or 3D sketch first (sw_create_sketch).")]
    public Task<object> AddStructuralMember(
        [Description("Sketch containing the path lines, e.g. Sketch1.")] string sketch,
        [Description("Profile standard, e.g. iso.")] string? standard = null,
        [Description("Profile type, e.g. square tube.")] string? type = null,
        [Description("Profile size, e.g. 40 x 40 x 4.")] string? size = null,
        [Description("Alternatively, the full path of a .sldlfp profile (overrides standard/type/size).")] string? profilePath = null,
        [Description("Profile configuration, for libraries that keep sizes as configurations of one file.")] string? configuration = null,
        [Description("Only these sketch segments (e.g. Line1, Line3). Omit for every non-construction segment of the sketch.")] string[]? segments = null,
        [Description("Corner treatment where members meet: miter | butt1 | butt2 | none.")] string cornerTreatment = "miter",
        [Description("Rotate the profile about the path, in degrees.")] double rotationDegrees = 0,
        [Description("How connected segments are cut: simple | coped.")] string connectedSegments = "simple",
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        var corner = WeldmentProfiles.ParseCornerTreatment(cornerTreatment);
        var connected = WeldmentProfiles.ParseConnectedSegments(connectedSegments);
        string? explicitProfile = profilePath is null ? null : paths.Check(profilePath, mustExist: true, nameof(profilePath));
        if (explicitProfile is null && (standard is null || type is null || size is null))
        {
            throw new McpException("Give standard, type and size (see sw_list_weldment_profiles), or profilePath.");
        }

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Structural members");
            string profile = explicitProfile ?? WeldmentProfiles.Resolve(WeldmentProfiles.Scan(ProfileRoots(app)), standard!, type!, size!).Path;

            Feature sketchFeature = FeatureWalker.Find(doc, sketch);
            if (sketchFeature.GetTypeName2() is not ("ProfileFeature" or "3DProfileFeature") || sketchFeature.GetSpecificFeature2() is not Sketch sk)
            {
                throw new McpException($"'{sketch}' is not a sketch.");
            }
            var all = (sk.GetSketchSegments() as object[] ?? Array.Empty<object>()).OfType<SketchSegment>().Where(s => !s.ConstructionGeometry).ToList();
            List<SketchSegment> chosen;
            if (segments is { Length: > 0 })
            {
                var names = all.Select(s => s.GetName()).ToList();
                chosen = WeldmentProfiles.MatchBodies(names, segments, nameof(segments), "Segment", "sketch segment names such as Line1").Select(i => all[i]).ToList();
            }
            else
            {
                chosen = all;
            }
            if (chosen.Count == 0)
            {
                throw new McpException($"'{sketch}' has no usable (non-construction) segments.");
            }

            var fm = doc.FeatureManager;
            if (!FeatureWalker.Walk(doc, includeSubFeatures: false).Any(x => x.Feature.GetTypeName2() == "WeldMent"))
            {
                fm.InsertWeldmentFeature();
            }

            StructuralMemberGroup group = fm.CreateStructuralMemberGroup();
            group.Segments = chosen.Cast<object>().ToArray();
            group.Angle = Units.DegToRad(rotationDegrees);
            group.ApplyCornerTreatment = corner != swSolidworksWeldmentEndCondOptions_e.swEndConditionNone;
            if (group.ApplyCornerTreatment)
            {
                group.CornerTreatmentType = (int)corner;
            }

            doc.ClearSelection2(true);
            // InsertStructuralWeldment5(Path, ConnectedSegmentsOption, AllowProtrusion, Groups, ConfigurationName)
            var feature = fm.InsertStructuralWeldment5(profile, (int)connected, true, new object[] { group }, configuration ?? string.Empty);
            doc.ClearSelection2(true);
            if (feature is null)
            {
                throw new McpException("Structural member failed. Segments in one group must be connected end-to-end or parallel; split other layouts into several calls.");
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
                profile,
                configuration,
                segments = chosen.Select(s => s.GetName()).ToList(),
                cornerTreatment = cornerTreatment.Trim().ToLowerInvariant(),
                status = code == 0 ? "ok" : (isWarning ? "warning: " : "error: ") + (swFeatureError_e)code,
            };
        }, ct);
    }

    [McpServerTool(Name = "sw_get_weldment_cut_list", Title = "Weldment cut list", ReadOnly = true, OpenWorld = false)]
    [Description("Read the weldment cut list: one item per group of identical members, with quantity, body names, and the properties SolidWorks maintains (LENGTH, ANGLE1, ANGLE2, DESCRIPTION, MATERIAL, mass...). Numeric values are also parsed. Values are in document units. Use for saw cutting lists and material ordering.")]
    public Task<object> GetWeldmentCutList(
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Cut list");
            if (!FeatureWalker.Walk(doc, includeSubFeatures: false).Any(x => x.Feature.GetTypeName2() == "WeldMent"))
            {
                throw new McpException($"'{doc.GetTitle()}' is not a weldment. Create members with sw_add_structural_member.");
            }
            var items = CutList.Read(doc);
            return new { document = doc.GetTitle(), items = items.Count, totalBodies = items.Sum(i => i.Quantity), cutList = items };
        }, ct);

    private static List<Body2> FindBodies(ModelDoc2 doc, string[]? names, string argument)
    {
        var bodies = (((PartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? Array.Empty<object>()).OfType<Body2>().ToList();
        return WeldmentProfiles.MatchBodies(bodies.Select(b => b.Name).ToList(), names ?? Array.Empty<string>(), argument).Select(i => bodies[i]).ToList();
    }

    [McpServerTool(Name = "sw_trim_extend", Title = "Weldment trim/extend", OpenWorld = false)]
    [Description("Trim or extend weldment members against other member bodies to clean up joints. Body names come from sw_get_weldment_cut_list (e.g. 'Structural Member1[2]').")]
    public Task<object> TrimExtend(
        [Description("Bodies to trim or extend.")] string[] bodiesToTrim,
        [Description("Boundary bodies to trim/extend to.")] string[] boundaryBodies,
        [Description("end_trim | miter | butt1 | butt2")] string type = "end_trim",
        [Description("Allow extending trimmed bodies to reach the boundary.")] bool allowExtension = true,
        [Description("Coped cut: trim to the boundary body's shape instead of a flat plane.")] bool coped = false,
        [Description("Weld gap in mm (0 = none).")] double weldGapMm = 0,
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        var trimType = WeldmentProfiles.ParseTrimType(type);
        if (weldGapMm < 0)
        {
            throw new McpException("weldGapMm must be >= 0.");
        }
        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Trim/extend");
            var toTrim = FindBodies(doc, bodiesToTrim, nameof(bodiesToTrim));
            var boundary = FindBodies(doc, boundaryBodies, nameof(boundaryBodies));

            int options = 0;
            if (allowExtension) options |= (int)swWeldmentTrimExtendOptionType_e.swWeldmentTrimExtendOption_AllowTrimmedExtensionTrim;
            if (coped) options |= (int)swWeldmentTrimExtendOptionType_e.swWeldmentTrimExtendOption_CopedCut;
            if (weldGapMm > 0) options |= (int)swWeldmentTrimExtendOptionType_e.swWeldmentTrimExtendOption_WeldGap;

            doc.ClearSelection2(true);
            // InsertWeldmentTrimFeature2(EndCond, Options, GapValue, BodiesToTrim, BodiesOrFaces)
            var feature = doc.FeatureManager.InsertWeldmentTrimFeature2((int)trimType, options, Units.MmToM(weldGapMm),
                toTrim.Cast<object>().ToArray(), boundary.Cast<object>().ToArray());
            doc.ClearSelection2(true);
            if (feature is null)
            {
                throw new McpException("Trim/extend failed. The bodies to trim must reach (or be extendable to) the boundary bodies.");
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
                trimmed = toTrim.Select(b => b.Name).ToList(),
                boundary = boundary.Select(b => b.Name).ToList(),
                status = code == 0 ? "ok" : (isWarning ? "warning: " : "error: ") + (swFeatureError_e)code,
            };
        }, ct);
    }

    /// <summary>Pick the face at each point (model mm). The points must lie on the faces.</summary>
    private static List<Face2> FacesAt(ModelDoc2 doc, double[][] points, string argument)
    {
        var selMgr = (SelectionMgr)doc.SelectionManager;
        var faces = new List<Face2>();
        for (int i = 0; i < points.Length; i++)
        {
            double[] p = points[i];
            doc.ClearSelection2(true);
            if (!doc.Extension.SelectByID2(string.Empty, "FACE", Units.MmToM(p[0]), Units.MmToM(p[1]), Units.MmToM(p[2]), false, 0, null, 0) ||
                selMgr.GetSelectedObject6(1, -1) is not Face2 face)
            {
                doc.ClearSelection2(true);
                throw new McpException($"No face found at {argument}[{i}] [{string.Join(", ", p)}] mm; the point must lie on the face.");
            }
            faces.Add(face);
        }
        doc.ClearSelection2(true);
        return faces;
    }

    [McpServerTool(Name = "sw_add_end_cap", Title = "Weldment end cap", OpenWorld = false)]
    [Description("Close open ends of tube members with end caps. Each point (mm) must lie on the end face of a member (the face across the open end).")]
    public Task<object> AddEndCap(
        [Description("One point per end face, e.g. [[0,0,500]].")] double[][] points,
        [Description("Cap plate thickness in mm.")] double thicknessMm,
        [Description("Inset (offset) from the outer edge in mm. Omit to use offsetRatio instead.")] double? offsetMm = null,
        [Description("Offset as a ratio of wall thickness when offsetMm is omitted (SolidWorks default 0.5).")] double offsetRatio = 0.5,
        [Description("Chamfer the cap's corners by this distance in mm (0 = none).")] double chamferMm = 0,
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        WeldmentProfiles.RequirePoints(points, 0, nameof(points));
        WeldmentProfiles.RequirePositive(thicknessMm, nameof(thicknessMm));
        if (offsetMm is < 0 || offsetRatio < 0 || chamferMm < 0)
        {
            throw new McpException("offsetMm, offsetRatio and chamferMm must be >= 0.");
        }
        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "End caps");
            var faces = FacesAt(doc, points, nameof(points));
            // InsertEndCapFeature2(Depth, BIsGivenOffset, BIsChamfer, OffsetValue, WallThicknessRatio, ChamferValue, Faces)
            var feature = doc.FeatureManager.InsertEndCapFeature2(Units.MmToM(thicknessMm), offsetMm is not null, chamferMm > 0,
                Units.MmToM(offsetMm ?? 0), offsetRatio, Units.MmToM(chamferMm), faces.Cast<object>().ToArray());
            doc.ClearSelection2(true);
            if (feature is null)
            {
                throw new McpException("End cap failed. Each point must be on a planar end face of a structural member.");
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                feature.Name = name;
            }
            doc.EditRebuild3();
            return new { feature = feature.Name, caps = faces.Count, thicknessMm };
        }, ct);
    }

    [McpServerTool(Name = "sw_add_gusset", Title = "Weldment gusset", OpenWorld = false)]
    [Description("Add a triangular gusset plate between two members meeting at a corner. Give two points (mm), each on one of the two adjoining planar faces.")]
    public Task<object> AddGusset(
        [Description("Exactly two points, one on each adjoining face, e.g. [[20,0,100],[100,0,20]].")] double[][] points,
        [Description("Plate thickness in mm.")] double thicknessMm,
        [Description("Length of the first leg (along the first face) in mm.")] double leg1Mm,
        [Description("Length of the second leg in mm.")] double leg2Mm,
        [Description("Where the plate sits across the member: start | center | end.")] string location = "center",
        [Description("Which side of the profile plane the thickness goes: inner | both | outer.")] string thicknessSide = "both",
        [Description("Optional name for the new feature.")] string? name = null,
        [Description("Path, file name or title of a part. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        WeldmentProfiles.RequirePoints(points, 2, nameof(points));
        WeldmentProfiles.RequirePositive(thicknessMm, nameof(thicknessMm));
        WeldmentProfiles.RequirePositive(leg1Mm, nameof(leg1Mm));
        WeldmentProfiles.RequirePositive(leg2Mm, nameof(leg2Mm));
        var loc = WeldmentProfiles.ParseGussetLocation(location);
        var side = WeldmentProfiles.ParseGussetThickness(thicknessSide);

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocPART, "Gussets");
            var faces = FacesAt(doc, points, nameof(points));
            // InsertGussetFeature2(Depth, DirType, LocType, BIsProfile, ProfileD1, ProfileD2, ProfileD3, ProfileAngle, ProfileD4,
            //   BOffset, DProfileOffset, CrvIndex, BReverseDir, BReverseFace, BUseLenDim, Faces).
            // Triangular profile: BIsProfile = false (polygonal profiles also use D3), no chamfer (D4 = 0).
            var feature = doc.FeatureManager.InsertGussetFeature2(Units.MmToM(thicknessMm), (short)side, (short)loc, false,
                Units.MmToM(leg1Mm), Units.MmToM(leg2Mm), 0, 0, 0,
                false, 0, 0, false, false, true, faces.Cast<object>().ToArray());
            doc.ClearSelection2(true);
            if (feature is null)
            {
                throw new McpException("Gusset failed. The two points must be on two planar faces of members that meet at an angle, and the legs must fit on those faces.");
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                feature.Name = name;
            }
            doc.EditRebuild3();
            return new { feature = feature.Name, thicknessMm, leg1Mm, leg2Mm, location = location.Trim().ToLowerInvariant() };
        }, ct);
    }
}
