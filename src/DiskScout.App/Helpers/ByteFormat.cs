namespace DiskScout.Helpers;

/// <summary>
/// Single source of truth for human-friendly byte formatting.
/// Use this everywhere instead of re-implementing.
/// </summary>
public static class ByteFormat
{
    private static readonly string[] Units = { "o", "Ko", "Mo", "Go", "To" };

    public static string Fmt(long bytes, string zeroLabel = "0 o")
    {
        if (bytes <= 0) return zeroLabel;
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {Units[i]}";
    }

    public static string FmtOrDash(long bytes) => bytes <= 0 ? "—" : Fmt(bytes);
}
