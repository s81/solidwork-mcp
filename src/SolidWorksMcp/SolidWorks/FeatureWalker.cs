using ModelContextProtocol;
using SolidWorks.Interop.sldworks;

namespace SolidWorksMcp.SolidWorks;

/// <summary>Feature tree traversal. Call only from the STA thread.</summary>
public static class FeatureWalker
{
    /// <summary>Top-level features in tree order, each followed by its sub-features (e.g. the sketch consumed by an extrude).</summary>
    public static IEnumerable<(Feature Feature, int Depth)> Walk(ModelDoc2 model, bool includeSubFeatures = true)
    {
        var f = model.FirstFeature() as Feature;
        while (f is not null)
        {
            yield return (f, 0);
            if (includeSubFeatures)
            {
                foreach (var sub in WalkSub(f, 1))
                {
                    yield return sub;
                }
            }
            f = f.GetNextFeature() as Feature;
        }
    }

    private static IEnumerable<(Feature, int)> WalkSub(Feature parent, int depth)
    {
        var sub = parent.GetFirstSubFeature() as Feature;
        while (sub is not null)
        {
            yield return (sub, depth);
            foreach (var s in WalkSub(sub, depth + 1))
            {
                yield return s;
            }
            sub = sub.GetNextSubFeature() as Feature;
        }
    }

    public static Feature Find(ModelDoc2 model, string name)
    {
        foreach (var (f, _) in Walk(model))
        {
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return f;
            }
        }
        throw new McpException($"Feature '{name}' not found in '{model.GetTitle()}'. Use sw_list_features to see feature names.");
    }

    /// <summary>
    /// Resolve a reference plane. Accepts the plane's feature name (any language) or the aliases
    /// front/top/right, which map to the first three planes of the tree (the template's default planes).
    /// This avoids relying on English names such as "Front Plane" that differ in localized installs.
    /// </summary>
    public static Feature FindPlane(ModelDoc2 model, string plane)
    {
        var planes = Walk(model, includeSubFeatures: false)
            .Select(x => x.Feature)
            .Where(f => f.GetTypeName2() == "RefPlane")
            .ToList();

        var byName = planes.FirstOrDefault(p => string.Equals(p.Name, plane, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        int index = plane.Trim().ToLowerInvariant() switch
        {
            "front" or "front plane" or "xy" => 0,
            "top" or "top plane" or "xz" => 1,
            "right" or "right plane" or "yz" => 2,
            _ => -1,
        };
        if (index >= 0 && index < planes.Count)
        {
            return planes[index];
        }

        throw new McpException($"Plane '{plane}' not found. Use front, top, right or one of: {string.Join(", ", planes.Select(p => p.Name))}.");
    }
}
