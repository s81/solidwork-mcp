namespace SolidWorksMcp.SolidWorks;

/// <summary>Turns SolidWorks error/warning bit masks (e.g. swFileLoadError_e) into readable names.</summary>
public static class SwFlags
{
    public static string Describe<TEnum>(int value) where TEnum : struct, Enum
    {
        if (value == 0)
        {
            return "none";
        }

        var names = new List<string>();
        int remaining = value;
        foreach (TEnum flag in Enum.GetValues<TEnum>())
        {
            int bit = Convert.ToInt32(flag);
            if (bit != 0 && (bit & (bit - 1)) == 0 && (value & bit) == bit)
            {
                names.Add(flag.ToString());
                remaining &= ~bit;
            }
        }

        if (names.Count == 0 && Enum.IsDefined(typeof(TEnum), value))
        {
            return Enum.GetName(typeof(TEnum), value)!;
        }
        if (remaining != 0)
        {
            names.Add($"0x{remaining:X}");
        }
        return string.Join(", ", names);
    }
}
