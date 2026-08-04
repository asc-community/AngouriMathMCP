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
        // `pow` is genuinely a function ON THIS BRANCH — issue #625 fixed it, and
        // pow(x,y) now parses to x^y. On the released package it still lexes as p*o*w,
        // so this entry is correct here and would be wrong against the NuGet build.
        "pow",
        "gamma", "phi", "derivative", "integral", "limit",
        "limitleft", "limitright", "piecewise", "provided", "apply", "lambda",
        "domain", "intersect", "and", "or", "not", "xor", "impl",
        // Verified by parsing `name(x)` against the actual grammar. Names that error on
        // wrong arity are fine to list — the failure is loud. Deliberately ABSENT, because
        // they silently degrade into a variable times a bracket: factorial, elementin,
        // union, setsubtraction, min, max. Listing those suppressed the very warning this
        // whitelist exists to raise, which is how `factorial(10)` quietly became a variable.
    };

    /// <summary>Names people reach for that this grammar spells differently.</summary>
    private static readonly Dictionary<string, string> Misspellings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["factorial"] = "postfix '!' — write 10! rather than factorial(10)",
        ["exp"] = "'e^x' — there is no exp function",
        ["mod"] = "there is no modulus operator or function in this grammar",
        ["min"] = "not available; use piecewise or a comparison",
        ["max"] = "not available; use piecewise or a comparison",
    };

    // The alphabet AngouriMath's VARIABLE rule actually accepts (AngouriMath.g:442): ASCII
    // letters, Greek and Coptic, Greek Extended, Cyrillic. Both warnings below are lexical
    // guesses at what the grammar will do, so they have to guess in the grammar's alphabet —
    // `[A-Za-z]` silently exempted every Greek and Cyrillic name from both checks, and `α2`
    // was squared with nothing said.
    // Kept as escapes rather than literal Greek so the ranges can be read straight off the
    // grammar. The string is verbatim, so C# passes the escapes through untouched and the
    // regex engine is what interprets them.
    private const string VariableChars = @"a-zA-Z\u0370-\u03FF\u1F00-\u1FFF\u0400-\u04FF";

    // A digit directly after a LETTER is the trap: `x2` parses as x^2. Two things are NOT
    // the trap. A digit after an underscore — `t_0` is a single variable named t_0, and is
    // the conventional safe way to write a subscript. And the digits of an exponent: `1.5e3`
    // is ONE number token, because EXPONENT is a fragment of NUMBER, so its `e` belongs to
    // the literal rather than to a variable. Hence the second branch: an `e` IS a warnable
    // identifier when no numeric literal precedes it, which is why bare `e3` (that is, e^3)
    // still warns. False positives are how you teach a caller to ignore warnings.
    private static readonly Regex TrailingDigit =
        new($@"[{VariableChars}-[eE]]\d|(?<![\d.])[eE]\d", RegexOptions.Compiled);

    // A name followed by '(' — the grammar has no leading-underscore variable, so the name
    // must start with a letter, and only continue with what VARIABLE allows.
    private static readonly Regex CallLike =
        new($@"([{VariableChars}][{VariableChars}0-9_]*)\s*\(", RegexOptions.Compiled);

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

            var hint = Misspellings.TryGetValue(name, out var spelling)
                ? $" Use {spelling}."
                : string.Empty;

            warnings.Add(
                $"unknown-function: '{name}' is not a function AngouriMath knows, so it was " +
                $"read as a VARIABLE multiplied by the bracketed group, not as a call." +
                hint + " Check the 'parsed' field.");
        }

        return warnings;
    }
}
