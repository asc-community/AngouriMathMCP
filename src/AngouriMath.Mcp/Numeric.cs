using AngouriMath;
using static AngouriMath.Entity;

namespace AngouriMath.Mcp;

/// <summary>
/// Tolerance-based numeric equality.
///
/// Why not just use MathS.UnsafeAndInternal.AreEqualNumerically? Because despite the name it
/// compares with EXACT equality — the implementation is `if (evaled1 != evaled2 ...) return
/// false` over arbitrary-precision decimals, with no tolerance at all. Any expression
/// involving a transcendental therefore fails: ln(x) computed via two different but
/// mathematically identical routes disagrees in the last digit and the check reports the
/// expressions as different.
///
/// That is not a hypothetical. `∫ x*ln(x)` returns the correct antiderivative, differentiates
/// back to the correct integrand, and AreEqualNumerically still says false. work/casbench
/// hit the same wall and implements its own comparator with a relative tolerance; this is
/// that comparator.
/// </summary>
public static class Numeric
{
    /// <summary>Deliberately positive reals: correct antiderivatives contain ln(x) and
    /// abs(x), which are undefined or non-holomorphic on the negatives.</summary>
    public static readonly Entity[] PositivePoints =
        ["0.37", "1.31", "2.17", "0.59", "3.43"];

    private const double RelativeTolerance = 1e-6;

    /// <summary>
    /// True / false / null, where null means "could not be evaluated at any sample point"
    /// — which is not evidence either way and must not be reported as inequality.
    /// </summary>
    public static bool? Equal(Entity left, Entity right, Entity[]? points = null)
    {
        points ??= PositivePoints;

        // `provided` guards are correct mathematics but do not evaluate to a number, so
        // they are dropped for the comparison only.
        var a = Strip(left);
        var b = Strip(right);

        var variables = a.Vars.Concat(b.Vars)
            .Select(v => v.Name)
            .Distinct()
            .ToArray();

        var usable = 0;

        // Rotating the offset gives each variable a different value on each pass, so
        // f(x, y) = x*y is not mistaken for f(x, y) = x^2.
        for (var offset = 0; offset < points.Length; offset++)
        {
            var subA = a;
            var subB = b;
            for (var i = 0; i < variables.Length; i++)
            {
                var point = points[(i + offset) % points.Length];
                var variable = MathS.Var(variables[i]);
                subA = subA.Substitute(variable, point);
                subB = subB.Substitute(variable, point);
            }

            var valueA = AsDouble(subA);
            var valueB = AsDouble(subB);

            // A point where either side is undefined tells us nothing; skip it rather than
            // counting it as disagreement.
            if (valueA is null || valueB is null) continue;

            var (realA, imagA) = valueA.Value;
            var (realB, imagB) = valueB.Value;

            var separation = Math.Sqrt(
                (realA - realB) * (realA - realB) + (imagA - imagB) * (imagA - imagB));
            var scale = Math.Max(
                1.0,
                Math.Max(Math.Sqrt(realA * realA + imagA * imagA),
                         Math.Sqrt(realB * realB + imagB * imagB)));

            if (separation / scale > RelativeTolerance) return false;
            usable++;
        }

        return usable == 0 ? null : true;
    }

    public static Entity Strip(Entity e) =>
        e.Replace(node => node is Providedf provided ? provided.Expression : node);

    /// <summary>Is this expression structurally zero once domain guards are removed?</summary>
    public static bool IsZero(Entity e)
    {
        var bare = Strip(e).InnerSimplified;
        return bare.Stringize() is "0" or "0.0" or "-0";
    }

    private static (double Real, double Imaginary)? AsDouble(Entity e)
    {
        if (!e.EvaluableNumerical) return null;

        try
        {
            var value = e.EvalNumerical();
            var real = value.RealPart.EDecimal.ToDouble();
            var imaginary = value.ImaginaryPart.EDecimal.ToDouble();

            if (double.IsNaN(real) || double.IsNaN(imaginary)) return null;
            if (double.IsInfinity(real) || double.IsInfinity(imaginary)) return null;

            return (real, imaginary);
        }
        catch
        {
            return null;
        }
    }
}
