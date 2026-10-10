namespace SimulationCore;

public static class Num
{
    public static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

    /// <summary>Compact, culture-invariant number formatting for the UI/config.</summary>
    public static string Fmt(double v)
    {
        if (double.IsPositiveInfinity(v)) return "∞";
        if (double.IsNegativeInfinity(v)) return "-∞";
        if (double.IsNaN(v)) return "—";
        double a = Math.Abs(v);
        if (a != 0 && (a >= 1e6 || a < 1e-3)) return v.ToString("0.###e+0", System.Globalization.CultureInfo.InvariantCulture);
        return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}

