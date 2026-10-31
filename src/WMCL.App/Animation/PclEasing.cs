namespace WMCL.App.Animation;

/// <summary>PCL2 ModAnimation 的缓动公式；power 对应 Weak=2/Middle=3/Strong=4/ExtraStrong=5。</summary>
internal static class PclEasing
{
    public static double Clamp(double value) => Math.Clamp(value, 0, 1);

    public static double Out(double progress, int power = 3)
    {
        var t = Clamp(progress);
        return 1 - Math.Pow(1 - t, power);
    }

    public static double In(double progress, int power = 3)
    {
        var t = Clamp(progress);
        return Math.Pow(t, power);
    }

    public static double InOut(double progress, int power = 3, double midpoint = 0.5)
    {
        var t = Clamp(progress);
        return t < midpoint
            ? In(t / midpoint, power) * midpoint
            : 1 - Out((t - midpoint) / (1 - midpoint), power) * (1 - midpoint);
    }

    public static double OutBack(double progress, int power = 2)
    {
        var t = Clamp(progress);
        var exponent = 3 - power / 2d;
        return 1 - Math.Pow(1 - t, exponent) * Math.Cos(1.5 * Math.PI * t);
    }

    public static double InBack(double progress, int power = 2)
    {
        var t = Clamp(progress);
        var exponent = 3 - power / 2d;
        return Math.Pow(t, exponent) * Math.Cos(1.5 * Math.PI * (1 - t));
    }

    public static double OutWithInitial(double progress, double alpha)
    {
        var t = Clamp(progress);
        if (alpha <= 0) return t;
        return (alpha + 1) * t / (1 + alpha * t);
    }
}
