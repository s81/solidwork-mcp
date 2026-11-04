using System.Globalization;
using ModelContextProtocol;
using SolidWorks.Interop.swconst;

namespace SolidWorksMcp.SolidWorks;

/// <summary>Sheet metal parameter rules. Pure logic, testable without SolidWorks.</summary>
public static class SheetMetalSpec
{
    public static void ValidateThickness(double? thicknessMm)
    {
        if (thicknessMm is double t && (t <= 0 || t > 50))
        {
            throw new McpException("thicknessMm must be > 0 and <= 50 mm.");
        }
    }

    public static void ValidateBendRadius(double? bendRadiusMm)
    {
        if (bendRadiusMm is double r && (r < 0 || r > 500))
        {
            throw new McpException("bendRadiusMm must be between 0 and 500 mm.");
        }
    }

    /// <summary>The K-factor is the neutral axis position as a fraction of thickness, so it lies strictly between 0 and 1 (typically 0.3-0.5).</summary>
    public static void ValidateKFactor(double? kFactor)
    {
        if (kFactor is double k && (k <= 0 || k >= 1))
        {
            throw new McpException("kFactor must be > 0 and < 1 (typically 0.3-0.5).");
        }
    }

    public static swFlangePositionTypes_e ParsePosition(string? position) => (position ?? "material_inside").Trim().ToLowerInvariant().Replace('-', '_') switch
    {
        "material_inside" => swFlangePositionTypes_e.swFlangePositionTypeMaterialInside,
        "material_outside" => swFlangePositionTypes_e.swFlangePositionTypeMaterialOutside,
        "bend_outside" => swFlangePositionTypes_e.swFlangePositionTypeBendOutside,
        "bend_centerline" or "bend_center_line" => swFlangePositionTypes_e.swFlangePositionTypeBendCenterLine,
        "bend_tangent" => swFlangePositionTypes_e.swFlangePositionTypeBendTangent,
        var p => throw new McpException($"Unknown flange position '{p}'. Use material_inside, material_outside, bend_outside, bend_centerline or bend_tangent."),
    };

    /// <summary>How the flange length is measured.</summary>
    public static swFlangeDimTypes_e ParseLengthReference(string? reference) => (reference ?? "outer_virtual_sharp").Trim().ToLowerInvariant().Replace('-', '_') switch
    {
        "outer_virtual_sharp" or "outer" => swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp,
        "inner_virtual_sharp" or "inner" => swFlangeDimTypes_e.swFlangeDimTypeInnerVirtualSharp,
        "tangent" or "bend_tangent" => swFlangeDimTypes_e.swFlangeDimTypeBendTangentArc,
        var r => throw new McpException($"Unknown length reference '{r}'. Use outer_virtual_sharp, inner_virtual_sharp or tangent."),
    };

    public static void ValidateEdgeFlange(double lengthMm, double angleDegrees)
    {
        if (lengthMm <= 0)
        {
            throw new McpException("lengthMm must be > 0.");
        }
        if (angleDegrees <= 0 || angleDegrees > 180)
        {
            throw new McpException("angleDegrees must be > 0 and <= 180.");
        }
    }

    public static string BendAllowanceName(int type) => type switch
    {
        (int)swBendAllowanceTypes_e.swBendAllowanceBendTable => "bend_table",
        (int)swBendAllowanceTypes_e.swBendAllowanceKFactor => "k_factor",
        (int)swBendAllowanceTypes_e.swBendAllowanceDirect => "bend_allowance",
        (int)swBendAllowanceTypes_e.swBendAllowanceDeduction => "bend_deduction",
        (int)swBendAllowanceTypes_e.swBendAllowanceBendCalculationTable => "bend_calculation_table",
        _ => type.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Cut-list values are text in document units, e.g. "250", "250.00", "1,250.5" or "250mm".
    /// Returns the number, or null when the text is not numeric (e.g. "Yes" or an unresolved link).
    /// </summary>
    public static double? ParseCutListNumber(string? text)
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
        if (rest.Length > 0 && rest is not ("mm" or "in" or "kg" or "g" or "mm^2" or "mm²" or "m" or "\"" or "lb"))
        {
            return null;
        }
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
    }

    /// <summary>Cut-list properties worth parsing into numbers for costing and nesting.</summary>
    public static readonly string[] NumericCutListKeys =
    {
        "Bounding Box Length", "Bounding Box Width", "Sheet Metal Thickness", "Bounding Box Area",
        "Bounding Box Area-Blank", "Cutting Length-Outer", "Cutting Length-Inner", "Cut Outs", "Bends",
        "Bend Radius", "Mass", "Quantity",
    };
}
