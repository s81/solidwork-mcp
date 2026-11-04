using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Infrastructure;
using SolidWorksMcp.SolidWorks;

namespace SolidWorksMcp.Tools;

[McpServerToolType]
public sealed class MateTools(SolidWorksSession sw)
{
    // IAssemblyDoc::AddMate5 acts on the current selection; mate entities are selected with mark 1.
    private const int MateMark = 1;

    [McpServerTool(Name = "sw_add_mate", Title = "Add mate", OpenWorld = false)]
    [Description("Add a mate between two components of the active assembly: coincident, concentric, parallel, perpendicular, tangent, distance, angle or lock. Each side is a component feature (front/top/right/origin plane, or a named plane/axis/sketch), a face/edge/vertex at a point (assembly mm), or, for lock, the component itself. Example: coincident between {component:'Leg-1', feature:'top'} and {component:'Top-1', feature:'top'}.")]
    public Task<object> AddMate(
        [Description("coincident | concentric | parallel | perpendicular | tangent | distance | angle | lock")] string type,
        [Description("Exactly two references, e.g. [{\"component\":\"Leg-1\",\"feature\":\"top\"},{\"component\":\"Top-1\",\"entity\":\"face\",\"point\":[0,0,740]}]")] MateTarget[] references,
        [Description("closest (default) | aligned | anti_aligned: whether the faces' normals point the same way or opposite.")] string alignment = "closest",
        [Description("Distance in mm (distance mates).")] double distance = 0,
        [Description("Angle in degrees (angle mates).")] double angleDegrees = 0,
        [Description("Flip the side the distance/angle is measured to.")] bool flip = false,
        [Description("Concentric only: also stop rotation about the shared axis.")] bool lockRotation = false,
        [Description("Optional name for the new mate.")] string? name = null,
        [Description("Path, file name or title of an assembly. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
    {
        var mateType = MateSpec.ParseType(type);
        var align = MateSpec.ParseAlignment(alignment);
        if (references is not { Length: 2 })
        {
            throw new McpException("references must contain exactly two entries.");
        }
        var kinds = references.Select((r, i) => MateSpec.Classify(r, i, mateType)).ToArray();
        if (mateType == swMateType_e.swMateDISTANCE && distance < 0)
        {
            throw new McpException("distance must be >= 0 mm; use flip to measure to the other side.");
        }
        if (mateType == swMateType_e.swMateANGLE && (angleDegrees < 0 || angleDegrees > 180))
        {
            throw new McpException("angleDegrees must be between 0 and 180.");
        }

        return sw.RunOnDocumentAsync<object>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocASSEMBLY, "Mates");
            var assy = (AssemblyDoc)doc;
            var selMgr = (SelectionMgr)doc.SelectionManager;

            doc.ClearSelection2(true);
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    Select(doc, assy, selMgr, references[i], kinds[i], i);
                }

                double d = Units.MmToM(distance);
                double a = Units.DegToRad(angleDegrees);
                int err = 0;
                // AddMate5(Type, Align, Flip, Distance, DistanceAbsUpperLimit, DistanceAbsLowerLimit, GearRatioNumerator,
                //   GearRatioDenominator, Angle, AngleAbsUpperLimit, AngleAbsLowerLimit, ForPositioningOnly, LockRotation,
                //   WidthMateOption, out ErrorStatus). Limits equal to the value = a standard (non-limit) mate.
                var mate = assy.AddMate5((int)mateType, (int)align, flip, d, d, d, 1, 1, a, a, a,
                    false, lockRotation, 0, out err);

                if (mate is null || err != (int)swAddMateError_e.swAddMateError_NoError)
                {
                    string reason = Enum.IsDefined(typeof(swAddMateError_e), err) ? ((swAddMateError_e)err).ToString().Replace("swAddMateError_", string.Empty) : err.ToString();
                    throw new McpException($"Mate failed: {reason}. " + reason switch
                    {
                        "IncorrectSelections" => "Check that both references exist and suit this mate type (e.g. concentric needs cylindrical faces, edges or axes).",
                        "OverDefinedAssembly" => "The assembly would be over-defined; remove a conflicting mate or use sw_list_mates to inspect.",
                        "IncorrectAlignment" => "Try alignment closest, or the opposite of what you passed.",
                        _ => string.Empty,
                    });
                }

                var feature = (Feature)mate;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    feature.Name = name;
                }
                doc.EditRebuild3();
                int code = feature.GetErrorCode2(out bool isWarning);
                return new
                {
                    mate = feature.Name,
                    type = MateSpec.TypeName(mate.Type),
                    alignment = MateSpec.AlignmentName(mate.Alignment),
                    references = references.Select(r => r.ToString()).ToArray(),
                    status = code == 0 ? "ok" : (isWarning ? "warning: " : "error: ") + (swFeatureError_e)code,
                };
            }
            finally
            {
                doc.ClearSelection2(true);
            }
        }, ct);
    }

    private static void Select(ModelDoc2 doc, AssemblyDoc assy, SelectionMgr selMgr, MateTarget r, MateTargetKind kind, int index)
    {
        Component2? comp = string.IsNullOrWhiteSpace(r.Component) ? null : FindComponent(assy, r.Component!);
        SelectData sd = selMgr.CreateSelectData();
        sd.Mark = MateMark;
        string label = $"references[{index}] ({r})";

        switch (kind)
        {
            case MateTargetKind.WholeComponent:
                if (!comp!.Select4(true, sd, false))
                {
                    throw new McpException($"{label}: could not select the component.");
                }
                return;

            case MateTargetKind.Feature:
                Feature f = comp is null ? FindFeature(FeaturesOf(doc), r.Feature!, doc.GetTitle()) : FindFeature(FeaturesOf(comp), r.Feature!, comp.Name2);
                if (!f.Select2(true, MateMark))
                {
                    throw new McpException($"{label}: could not select feature '{f.Name}'.");
                }
                return;

            case MateTargetKind.Entity:
                int before = selMgr.GetSelectedObjectCount2(-1);
                double[] p = r.Point!;
                // An empty name with coordinates selects the entity of that type at the location.
                bool ok = doc.Extension.SelectByID2(string.Empty, MateSpec.EntityType(r.Entity!), Units.MmToM(p[0]), Units.MmToM(p[1]), Units.MmToM(p[2]),
                    true, MateMark, null, 0);
                int after = selMgr.GetSelectedObjectCount2(-1);
                if (!ok || after <= before)
                {
                    throw new McpException($"{label}: no {r.Entity} found at [{string.Join(", ", p)}] mm. The point must lie on the geometry; try a feature reference instead.");
                }
                if (comp is not null && selMgr.GetSelectedObjectsComponent4(after, -1) is Component2 picked &&
                    !string.Equals(picked.Name2, comp.Name2, StringComparison.OrdinalIgnoreCase))
                {
                    throw new McpException($"{label}: the {r.Entity} at that point belongs to '{picked.Name2}', not '{comp.Name2}'.");
                }
                return;
        }
    }

    internal static Component2 FindComponent(AssemblyDoc assy, string name)
    {
        var all = (assy.GetComponents(false) as object[] ?? Array.Empty<object>()).OfType<Component2>().ToList();
        return all[MateSpec.MatchComponent(all.Select(c => c.Name2).ToList(), name)];
    }

    private static IEnumerable<Feature> FeaturesOf(Component2 comp)
    {
        for (var f = comp.FirstFeature(); f is not null; f = f.GetNextFeature() as Feature)
        {
            yield return f;
        }
    }

    private static IEnumerable<Feature> FeaturesOf(ModelDoc2 doc) => FeatureWalker.Walk(doc, includeSubFeatures: false).Select(x => x.Feature);

    /// <summary>Like FeatureWalker.FindPlane, plus "origin" and any named feature; works on component features in assembly context.</summary>
    private static Feature FindFeature(IEnumerable<Feature> features, string wanted, string owner)
    {
        var list = features.ToList();
        var exact = list.FirstOrDefault(f => string.Equals(f.Name, wanted, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }
        string w = wanted.Trim().ToLowerInvariant();
        if (w == "origin")
        {
            return list.FirstOrDefault(f => f.GetTypeName2() == "OriginProfileFeature")
                ?? throw new McpException($"'{owner}' has no origin feature.");
        }
        int planeIndex = w switch
        {
            "front" or "front plane" => 0,
            "top" or "top plane" => 1,
            "right" or "right plane" => 2,
            _ => -1,
        };
        var planes = list.Where(f => f.GetTypeName2() == "RefPlane").ToList();
        if (planeIndex >= 0 && planeIndex < planes.Count)
        {
            return planes[planeIndex];
        }
        throw new McpException($"Feature '{wanted}' not found in '{owner}'. Use front, top, right, origin, or one of: " +
            string.Join(", ", list.Where(f => f.GetTypeName2() is "RefPlane" or "RefAxis" or "RefPoint" or "ProfileFeature").Select(f => f.Name)));
    }

    public sealed record MateInfo(string Name, string Type, string Alignment, bool Flipped, bool Suppressed, IReadOnlyList<string> Components, string? Error);

    [McpServerTool(Name = "sw_list_mates", Title = "List mates", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the mates of an assembly with type, alignment, the components they connect, and errors. Delete one with sw_delete_feature, suppress with sw_set_feature_suppression.")]
    public Task<IReadOnlyList<MateInfo>> ListMates(
        [Description("Only mates involving this component instance (optional).")] string? component = null,
        [Description("Path, file name or title of an assembly. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync<IReadOnlyList<MateInfo>>(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocASSEMBLY, "Listing mates");
            var result = new List<MateInfo>();
            foreach (var (f, depth) in FeatureWalker.Walk(doc))
            {
                if (depth == 0 || f.GetSpecificFeature2() is not Mate2 mate)
                {
                    continue;
                }
                var comps = new List<string>();
                for (int i = 0; i < mate.GetMateEntityCount(); i++)
                {
                    comps.Add(mate.MateEntity(i)?.ReferenceComponent?.Name2 ?? "(assembly)");
                }
                if (component is not null && !comps.Any(c => string.Equals(c, component, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c.Split('/')[^1], component, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                int code = f.GetErrorCode2(out bool warn);
                result.Add(new MateInfo(f.Name, MateSpec.TypeName(mate.Type), MateSpec.AlignmentName(mate.Alignment), mate.Flipped,
                    f.IsSuppressed(), comps, code == 0 ? null : (warn ? "warning: " : "error: ") + (swFeatureError_e)code));
            }
            return result;
        }, ct);

    [McpServerTool(Name = "sw_set_component_fixed", Title = "Fix / float component", Idempotent = true, OpenWorld = false)]
    [Description("Fix a component in place (no degrees of freedom) or float it so mates can position it. Inserted components start floating; usually fix the first/base component.")]
    public Task<string> SetComponentFixed(
        [Description("Component instance name, e.g. Base-1.")] string component,
        [Description("true = fix, false = float.")] bool isFixed,
        [Description("Path, file name or title of an assembly. Omit for the active document.")] string? document = null,
        CancellationToken ct = default)
        => sw.RunOnDocumentAsync(document, (app, doc) =>
        {
            Documents.RequireType(doc, swDocumentTypes_e.swDocASSEMBLY, "Fixing components");
            var assy = (AssemblyDoc)doc;
            Component2 comp = FindComponent(assy, component);
            doc.ClearSelection2(true);
            if (!comp.Select4(false, null, false))
            {
                throw new McpException($"Could not select '{comp.Name2}'.");
            }
            if (isFixed) assy.FixComponent(); else assy.UnfixComponent();
            doc.ClearSelection2(true);
            doc.EditRebuild3();
            return $"{comp.Name2} is now {(comp.IsFixed() ? "fixed" : "floating")}";
        }, ct);
}
