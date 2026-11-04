using System.Globalization;
using SolidWorks.Interop.sldworks;

namespace SolidWorksMcp.SolidWorks;

public sealed record CutListItem(string Name, int Quantity, IReadOnlyList<string> Bodies,
    IReadOnlyDictionary<string, double> Numbers, IReadOnlyDictionary<string, string> Properties);

/// <summary>Reads a part's cut list (weldment members, sheet metal bodies). Call only from the STA thread.</summary>
public static class CutList
{
    public static List<CutListItem> Read(ModelDoc2 doc)
    {
        var solidFolder = FeatureWalker.Walk(doc, includeSubFeatures: false).Select(x => x.Feature)
            .FirstOrDefault(f => f.GetTypeName2() == "SolidBodyFolder");
        if (solidFolder?.GetSpecificFeature2() is BodyFolder root)
        {
            root.UpdateCutList(); // cut-list properties are only current after an update
        }

        var items = new List<CutListItem>();
        foreach (var (f, _) in FeatureWalker.Walk(doc))
        {
            if (f.GetTypeName2() != "CutListFolder")
            {
                continue;
            }
            var props = new Dictionary<string, string>();
            var numbers = new Dictionary<string, double>();
            CustomPropertyManager? mgr = f.CustomPropertyManager;
            if (mgr?.GetNames() is object[] names)
            {
                foreach (string n in names.Cast<string>())
                {
                    mgr.Get6(n, false, out _, out string resolved, out _, out _);
                    props[n] = resolved ?? string.Empty;
                    if (ParseNumber(resolved) is double v)
                    {
                        numbers[n] = v;
                    }
                }
            }
            var folder = f.GetSpecificFeature2() as BodyFolder;
            var bodies = (folder?.GetBodies() as object[] ?? Array.Empty<object>()).OfType<Body2>().Select(b => b.Name).ToList();
            items.Add(new CutListItem(f.Name, folder?.GetBodyCount() ?? bodies.Count, bodies, numbers, props));
        }
        return items;
    }

    /// <summary>
    /// Cut-list values are text in document units, e.g. "250", "250.00", "1,250.5" or "250mm".
    /// Returns the number, or null when the text is not numeric (e.g. "Yes", a material name or an unresolved link).
    /// </summary>
    public static double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        string t = text.Trim();
        int end = 0;
        while (end < t.Length && (char.IsDigit(t[end]) || t[end] is '.' or ',' or '-' or '+'))
        {
            end++;
        }
        if (end == 0)
        {
            return null;
        }
        string number = t[..end].Replace(",", string.Empty);
        string rest = t[end..].Trim().ToLowerInvariant();
        if (rest.Length > 0 && rest is not ("mm" or "in" or "kg" or "g" or "mm^2" or "mm²" or "m" or "\"" or "lb" or "deg" or "°"))
        {
            return null;
        }
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
    }
}
