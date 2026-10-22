namespace DeskWidgets.Motion;

/// <summary>A CSS-style cubic Bézier easing, evaluated by hand for the animations driven from the UI thread (the layered-window fades).</summary>
internal readonly record struct Bezier(double X1, double Y1, double X2, double Y2)
{
    /// <summary>The Glass vocabulary's easings (MOTION.md §1): Out = (0.2, 0.8, 0.2, 1), In = (0.4, 0, 1, 1), Move = (0.3, 0, 0.1, 1).</summary>
    public static readonly Bezier Out = new(0.2, 0.8, 0.2, 1), In = new(0.4, 0, 1, 1), Move = new(0.3, 0, 0.1, 1);

    /// <summary>The value at time fraction <paramref name="t"/> (0 to 1).</summary>
    public double At(double t)
    {
        if (t <= 0)
            return 0;
        if (t >= 1)
            return 1;
        // Solve x(s) = t for the curve parameter s by bisection, then read y(s).
        double low = 0, high = 1, s = t;
        for (int i = 0; i < 24; i++)
        {
            s = (low + high) / 2;
            double x = Cubic(X1, X2, s);
            if (x < t)
                low = s;
            else
                high = s;
        }
        return Cubic(Y1, Y2, s);
    }

    private static double Cubic(double p1, double p2, double s)
    {
        double u = 1 - s;
        return 3 * u * u * s * p1 + 3 * u * s * s * p2 + s * s * s;
    }
}
