using AngouriMath;
using static AngouriMath.Entity;

namespace AngouriMath.Mcp;

/// <summary>
/// `angourimath-mcp --selftest` — does this install work, and is the library still behaving
/// the way the documentation claims?
///
/// Two categories, and the distinction matters. Identities MUST hold; if one fails the
/// install or the library is broken and the exit code says so. Documented defects are
/// merely *reported*: a defect that stops reproducing is good news, not a failure — but it
/// does mean the docs have drifted and need editing. That second category exists because
/// exactly that happened: `Factorize(x^2 - 1)` was documented as emitting `sqrt(1)`, which
/// was true of the released package and not of the development branch, and it took a manual
/// re-check to notice.
/// </summary>
public static class SelfTest
{
    private static bool IsZero(string expression)
    {
        var outcome = Parsing.Parse(expression);
        return outcome.Entity is not null && Numeric.IsZero(outcome.Entity.Simplify());
    }

    private static string Value(string expression)
    {
        var outcome = Parsing.Parse(expression);
        return outcome.Entity is null ? "<parse error>" : outcome.Entity.Evaled.Stringize();
    }

    private static readonly (string Name, Func<bool> Holds)[] Identities =
    [
        ("Euler's identity is exactly zero", () => Value("e^(i*pi) + 1") == "0"),
        ("Machin: 4*atan(1/5) - atan(1/239) = pi/4",
            () => IsZero("4*arctan(1/5) - arctan(1/239) - pi/4")),
        ("golden ratio = 2*cos(pi/5)", () => IsZero("(1+sqrt(5))/2 - 2*cos(pi/5)")),
        ("taxicab 1729 two ways", () => IsZero("(1^3 + 12^3) - (9^3 + 10^3)")),
        ("42 as a sum of three cubes",
            () => Value("(-80538738812075974)^3 + 80435758145817515^3 + 12602123297335631^3") == "42"),
        ("Simpsons' Fermat near-miss is NOT equal",
            () => Value("3987^12 + 4365^12 - 4472^12") != "0"),
        ("six by nine is 42 in base 13", () => MathS.ToBaseN(54, 13) == "42"),
        ("42 is 101010 in binary", () => MathS.ToBaseN(42, 2) == "101010"),
        ("42 is the 5th Catalan number", () => Value("10! / (6! * 5!)") == "42"),
        ("Pythagoras: sin^2 + cos^2 = 1", () => IsZero("sin(x)^2 + cos(x)^2 - 1")),
        ("d/dx integral of x*ln(x) returns the integrand", () =>
        {
            var outcome = Parsing.Parse("x*ln(x)");
            if (outcome.Entity is null) return false;
            var x = MathS.Var("x");
            var back = outcome.Entity.Integrate(x).Simplify().Differentiate(x);
            return Numeric.Equal(back, outcome.Entity) == true;
        }),
    ];

    private static readonly (string Name, Func<bool> StillBroken, string Documented)[] Defects =
    [
        ("Simplify(sqrt(x^2))", () => Value("sqrt(x^2)") != "abs(x)",
            "returns x rather than abs(x)"),
        ("exp(x) parsing", () =>
        {
            var outcome = Parsing.Parse("exp(x)");
            return outcome.Entity is not null
                   && outcome.Entity.Vars.Any(v => v.Stringize() == "exp");
        }, "parses as exp * x, a silent multiplication"),
        ("integral of x^4*(1-x)^4/(1+x^2)", () =>
        {
            var outcome = Parsing.Parse("x^4*(1-x)^4/(1+x^2)");
            if (outcome.Entity is null) return false;
            var result = outcome.Entity.Integrate(MathS.Var("x")).Stringize();
            return Guard.IsDeclined(result);
        }, "declined, though it succeeds once divided out by hand"),
        ("determinant leaves a pivot guard", () =>
        {
            var m = MathS.Matrix(new Entity[,]
            {
                { MathS.Var("a"), MathS.Var("b") },
                { MathS.Var("c"), MathS.Var("d") },
            });
            return m.Determinant is { } det && Analysis.Conditions(det.Simplify()).Count > 0;
        }, "det([[a,b],[c,d]]) carries `provided not a = 0`"),
    ];

    public static int Run(TextWriter output)
    {
        output.WriteLine("angourimath-mcp self-test");
        output.WriteLine();
        output.WriteLine("Identities (these must hold):");

        var failed = 0;
        foreach (var (name, holds) in Identities)
        {
            // Run through Guard, not directly. On the released package the x*ln(x) check
            // overflows the stack inside IntegrateByPartsPolynomial and would take the
            // self-test process down with it — a diagnostic tool that dies on the thing it
            // is diagnosing is worse than useless.
            var outcome = Guard.Run(holds, timeoutMs: 30_000);

            if (!outcome.Ok)
            {
                output.WriteLine($"  FAIL {name} — {outcome.Status}: {outcome.Error}");
                failed++;
                continue;
            }

            output.WriteLine($"  {(outcome.Value ? "ok  " : "FAIL")} {name}");
            if (!outcome.Value) failed++;
        }

        output.WriteLine();
        output.WriteLine("Documented defects (a fix here means the docs need updating):");

        var drifted = 0;
        foreach (var (name, stillBroken, documented) in Defects)
        {
            var outcome = Guard.Run(stillBroken, timeoutMs: 30_000);
            if (!outcome.Ok)
            {
                output.WriteLine($"  ?      {name} — could not check ({outcome.Status})");
                continue;
            }

            var broken = outcome.Value;
            if (broken)
            {
                output.WriteLine($"  still  {name}: {documented}");
            }
            else
            {
                output.WriteLine($"  FIXED  {name} — documented as: {documented}");
                output.WriteLine($"         Update UPSTREAM.md and angourimath://reliability.");
                drifted++;
            }
        }

        output.WriteLine();
        if (failed > 0)
            output.WriteLine($"{failed} identity check(s) FAILED — this install is not trustworthy.");
        else
            output.WriteLine($"All {Identities.Length} identities hold.");

        if (drifted > 0)
            output.WriteLine($"{drifted} documented defect(s) no longer reproduce. The docs have drifted.");

        return failed > 0 ? 1 : 0;
    }
}
