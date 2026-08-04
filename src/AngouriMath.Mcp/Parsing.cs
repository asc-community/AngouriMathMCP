using System.Text.RegularExpressions;
using AngouriMath;
using HonkSharp.Functional;

namespace AngouriMath.Mcp;

/// <summary>
/// Parsing plus the two warnings that matter.
///
/// AngouriMath's parser is permissive in two ways that are silent and therefore dangerous
/// when the caller is a language model rather than a human reading its own formula:
///
///   * a trailing number is an EXPONENT, not a factor: `x2` is x², `2(g+e)3` is 2(g+e)³.
///     A model that names a variable `x2`, `v1` or `a0` — completely ordinary naming —
///     gets it silently squared. (MathS.cs documents this on ExplicitParsingOnly.)
///   * an unknown identifier becomes implicit multiplication: `pow(x,y)` lexes as p*o*w(…)
///     and `arcsinh(x)` as the product `arcsinh * x`. Issue #625; the triage notes record
///     that this happens "with nothing said".
///
/// Both produce a valid parse of a DIFFERENT expression, which is the worst failure class
/// available: no exception, plausible answer, wrong. Rather than force strict mode on
/// everyone (which rejects the very common `2x`), the server parses permissively and always
/// reports what it understood, with a warning when either pattern is present.
/// </summary>
public static class Parsing
{
    /// <summary>Either a parsed entity with its warnings, or an error string — never both.</summary>
    public sealed record Outcome(Entity? Entity, List<string> Warnings, string? Error);

    /// <summary>Functions AngouriMath's grammar actually knows. Anything else followed by
    /// '(' is a variable multiplied by a parenthesised group, not a call.</summary>
    private static readonly HashSet<string> KnownFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sin", "cos", "tan", "cotan", "cot", "sec", "cosec", "csc",
        "arcsin", "arccos", "arctan", "arccotan", "arccot", "arcsec", "arccosec", "arccsc",
        "sinh", "cosh", "tanh", "cotanh", "coth", "sech", "cosech", "csch",
        "arsinh", "arcosh", "artanh", "arcotanh", "arsech", "arcosech",
        "arcsinh", "arccosh", "arctanh", "arccotanh",
        "log", "ln", "sqrt", "cbrt", "sqr", "abs", "signum", "sgn",
        "gamma", "factorial", "phi", "derivative", "integral", "limit",
        "limitleft", "limitright", "piecewise", "provided", "apply", "lambda",
        "domain", "elementin", "union", "intersect", "setsubtraction",
        "and", "or", "not", "xor", "impl", "min", "max",
    };

    private static readonly Regex TrailingDigit = new(@"[A-Za-z_]\w*?\d", RegexOptions.Compiled);
    private static readonly Regex CallLike = new(@"([A-Za-z_]\w*)\s*\(", RegexOptions.Compiled);

    public static Outcome Parse(string source, bool strict = false)
    {
        // Scoped, auto-reverting. Note MathS.Settings is process-global (a KeyStack over a
        // plain List, no thread affinity), so this is only safe because the server handles
        // one request at a time. See README.
        using var _ = MathS.Settings.ExplicitParsingOnly.Set(strict);

        // MathS.Parse is the non-throwing parser: it returns a reason rather than raising,
        // which is what lets the caller report a clean message instead of a stack trace.
        return MathS.Parse(source).Switch(
            entity => new Outcome(entity, Warnings(source), null),
            failure => new Outcome(null, [], failure.Reason.Switch<string>(
                unknown => $"could not parse: {unknown.Reason}",
                missingOperator => $"missing operator: {missingOperator.Details}",
                internalError => $"internal parser error: {internalError.Details}")));
    }

    private static List<string> Warnings(string source)
    {
        var warnings = new List<string>();

        if (TrailingDigit.IsMatch(source))
            warnings.Add(
                "implicit-power: a number directly after an identifier is an EXPONENT, " +
                "not a factor — 'x2' parses as x^2. Check the 'parsed' field; write 'x*2' " +
                "if you meant multiplication, and avoid variable names ending in a digit.");

        foreach (Match m in CallLike.Matches(source))
        {
            var name = m.Groups[1].Value;
            if (KnownFunctions.Contains(name)) continue;
            warnings.Add(
                $"unknown-function: '{name}' is not a function AngouriMath knows, so it was " +
                $"read as a VARIABLE multiplied by the bracketed group, not as a call. " +
                $"Check the 'parsed' field.");
        }

        return warnings;
    }
}
