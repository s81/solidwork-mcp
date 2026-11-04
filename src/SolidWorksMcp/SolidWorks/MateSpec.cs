using System.ComponentModel;
using ModelContextProtocol;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

/// <summary>One side of a mate. Give a feature, or an entity type + point, or only a component (lock mates).</summary>
public sealed class MateTarget
{
    [Description("Component instance name, e.g. Leg-1, or Frame-1/Rail-2 for a nested one. Omit to use the top assembly's own planes/origin.")]
    public string? Component { get; set; }

    [Description("front | top | right | origin, or the name of a reference plane, axis, point or sketch in that component.")]
    public string? Feature { get; set; }

    [Description("face | edge | vertex: pick geometry at 'point' instead of a feature.")]
    public string? Entity { get; set; }

    [Description("[x, y, z] in mm, assembly coordinates, lying on the face/edge/vertex to pick.")]
    public double[]? Point { get; set; }

    public override string ToString()
        => (Component ?? "assembly") + ":" + (Feature ?? (Entity is null ? "component" : $"{Entity}@[{string.Join(", ", Point ?? Array.Empty<double>())}]"));
}

public enum MateTargetKind { Feature, Entity, WholeComponent }

public static class MateSpec
{
    public static swMateType_e ParseType(string type) => type.Trim().ToLowerInvariant() switch
    {
        "coincident" => swMateType_e.swMateCOINCIDENT,
        "concentric" => swMateType_e.swMateCONCENTRIC,
        "parallel" => swMateType_e.swMatePARALLEL,
        "perpendicular" => swMateType_e.swMatePERPENDICULAR,
        "tangent" => swMateType_e.swMateTANGENT,
        "distance" => swMateType_e.swMateDISTANCE,
        "angle" => swMateType_e.swMateANGLE,
        "lock" => swMateType_e.swMateLOCK,
        _ => throw new McpException($"Unsupported mate type '{type}'. Use coincident, concentric, parallel, perpendicular, tangent, distance, angle or lock."),
    };

    public static swMateAlign_e ParseAlignment(string? alignment) => (alignment ?? "closest").Trim().ToLowerInvariant().Replace('-', '_') switch
    {
        "closest" or "" => swMateAlign_e.swMateAlignCLOSEST,
        "aligned" => swMateAlign_e.swMateAlignALIGNED,
        "anti_aligned" or "antialigned" => swMateAlign_e.swMateAlignANTI_ALIGNED,
        var a => throw new McpException($"Unknown alignment '{a}'. Use closest, aligned or anti_aligned."),
    };

    public static string TypeName(int type) => Enum.IsDefined(typeof(swMateType_e), type)
        ? ((swMateType_e)type).ToString().Replace("swMate", string.Empty).ToLowerInvariant()
        : $"type{type}";

    public static string AlignmentName(int alignment) => alignment switch
    {
        (int)swMateAlign_e.swMateAlignALIGNED => "aligned",
        (int)swMateAlign_e.swMateAlignANTI_ALIGNED => "anti_aligned",
        (int)swMateAlign_e.swMateAlignCLOSEST => "closest",
        _ => alignment.ToString(),
    };

    /// <summary>Validate one side of a mate and say how it will be selected.</summary>
    public static MateTargetKind Classify(MateTarget r, int index, swMateType_e type)
    {
        string label = $"references[{index}]";
        bool hasFeature = !string.IsNullOrWhiteSpace(r.Feature);
        bool hasEntity = !string.IsNullOrWhiteSpace(r.Entity);
        if (hasFeature && hasEntity)
        {
            throw new McpException($"{label}: give either 'feature' or 'entity' + 'point', not both.");
        }
        if (hasEntity)
        {
            EntityType(r.Entity!, label);
            if (r.Point is not { Length: 3 })
            {
                throw new McpException($"{label}: 'entity' needs 'point' as [x, y, z] in mm.");
            }
            return MateTargetKind.Entity;
        }
        if (hasFeature)
        {
            return MateTargetKind.Feature;
        }
        if (string.IsNullOrWhiteSpace(r.Component))
        {
            throw new McpException($"{label}: give a component, a feature, or an entity + point.");
        }
        if (type != swMateType_e.swMateLOCK)
        {
            throw new McpException($"{label}: a whole component can only be used in a lock mate; give a 'feature' or 'entity' + 'point'.");
        }
        return MateTargetKind.WholeComponent;
    }

    /// <summary>SelectByID2 type string for a picked entity.</summary>
    public static string EntityType(string entity, string label = "entity") => entity.Trim().ToLowerInvariant() switch
    {
        "face" => "FACE",
        "edge" => "EDGE",
        "vertex" => "VERTEX",
        var e => throw new McpException($"{label}: unknown entity '{e}'. Use face, edge or vertex."),
    };

    /// <summary>
    /// Match a component instance name. Accepts the full path name ("Frame-1/Rail-2") or, when unique, just the
    /// last segment ("Rail-2"). Comparison ignores case. Returns the index into <paramref name="names"/>.
    /// </summary>
    public static int MatchComponent(IReadOnlyList<string> names, string wanted)
    {
        string w = wanted.Trim();
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], w, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        var bySegment = names.Select((n, i) => (n, i))
            .Where(x => string.Equals(x.n.Split('/')[^1], w, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return bySegment.Count switch
        {
            1 => bySegment[0].i,
            0 => throw new McpException($"Component '{wanted}' not found. Use sw_get_component_tree to see instance names."),
            _ => throw new McpException($"'{wanted}' is ambiguous: {string.Join(", ", bySegment.Select(x => x.n))}. Use the full path name."),
        };
    }
}
