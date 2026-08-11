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
///   * an unknown identifier becomes implicit multiplication: `im(z)` is the product
///     `im * z`, not the imaginary part. AngouriMath 2.0.0 refuses eleven such names by
///     name — `trunc`, `lcm`, `erf`, `conjugate` and the arc- spellings of the inverse
///     hyperbolics — so those now fail loudly; the rest still degrade with nothing said,
///     because refusing every unknown name is refusing `a(b + c)`.
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
    /// '(' is a variable multiplied by a parenthesised group, not a call.
    ///
    /// Every entry is verified by parsing `name(x)` against the grammar and reading what
    /// came back — not from the release notes, which describe this surface in two passes
    /// (`floor` and its kind are refused in one section and added in a later one). Re-run
    /// that probe after a library upgrade: a name that has become a function makes this
    /// whitelist emit a warning on correct input, and a name that has stopped being one
    /// makes it swallow the warning that matters.</summary>
    private static readonly HashSet<string> KnownFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sin", "cos", "tan", "cotan", "cot", "sec", "cosec", "csc",
        "arcsin", "arccos", "arctan", "arccotan", "arccot", "arcsec", "arccosec", "arccsc",
        "sinh", "cosh", "tanh", "cotanh", "coth", "sech", "cosech", "csch",
        // The inverse hyperbolics are AREA functions, and 2.0.0 refuses the arc- spellings
        // by name rather than accepting them. `arcsinh` and its five relatives are therefore
        // NOT here: they raise UnrecognizedFunctionParseException, whose message names these
        // spellings, so the caller is told more than a warning could say.
        "arsinh", "arcosh", "artanh", "arcotanh", "arsech", "arcosech",
        "asinh", "acosh", "atanh", "acotanh", "asech", "acosech",
        "arsh", "arch", "arth", "arcth",
        "log", "ln", "sqrt", "cbrt", "sqr", "abs", "signum", "sgn", "sign",
        // New in AngouriMath 2.0.0. Each of these was a silent implicit multiplication
        // before, which is what the unknown-function warning existed to catch; warning
        // about them now would fire on correct input.
        "exp", "log10", "log2",
        "floor", "ceil", "ceiling", "round", "min", "max", "gcd", "factorial",
        // `pow(x, y)` parses to x^y as of 2.0.0.
        "pow",
        "gamma", "phi", "derivative", "integral", "limit",
        "limitleft", "limitright", "piecewise", "provided", "apply", "lambda",
        "domain", "intersect", "and", "or", "not", "xor",
        // Names that error on wrong arity are fine to list — the failure is loud, and the
        // warning path only runs on a successful parse. Deliberately ABSENT, because they
        // still degrade silently into a variable times a bracket: elementin, union,
        // setsubtraction, impl, re, im. Listing one of those suppresses the very warning
        // this whitelist exists to raise — `impl` was listed, and `impl(a, b)` was quietly
        // becoming `impl * (a, b)` with nothing said.
    };

    /// <summary>Names people reach for that this grammar spells differently, and that parse
    /// as something else rather than failing. A name the library refuses outright is not
    /// here: its own exception says more than this table could.</summary>
    private static readonly Dictionary<string, string> Misspellings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["union"] = @"the infix '\/' — write 'A \/ B'",
        ["setsubtraction"] = @"the infix '\' — write 'A \ B'",
        ["elementin"] = "the infix 'in' — write 'x in A'",
        ["impl"] = "'->' or 'implies' — write 'a -> b'",
        ["re"] = "not in this grammar; there is no real-part function",
        ["im"] = "not in this grammar; there is no imaginary-part function",
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
        // Scoped, auto-reverting. As of AngouriMath 2.0.0 the scope is an AsyncLocal and
        // follows the call rather than the thread, so it is correct even under concurrency;
        // before that it was safe only because the server handles one request at a time.
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
        var calls = CallLike.Matches(source);

        // A known function's own name is not an implicit power, even when it ends in a
        // digit. `log2(8)` is the base-2 logarithm as of 2.0.0, and the `g2` inside it is
        // not the `x2` trap — warning there fires on correct input, which is how a caller
        // learns to ignore the channel. Spans rather than a special case for `log2` and
        // `log10`, so the next function name carrying a digit needs no second fix.
        var functionNameSpans = calls
            .Where(m => KnownFunctions.Contains(m.Groups[1].Value))
            .Select(m => (Start: m.Groups[1].Index, End: m.Groups[1].Index + m.Groups[1].Length))
            .ToList();

        var implicitPower = TrailingDigit.Matches(source).Any(m =>
            !functionNameSpans.Any(s => m.Index >= s.Start && m.Index + m.Length <= s.End));

        if (implicitPower)
            warnings.Add(
                "implicit-power: a number directly after an identifier is an EXPONENT, " +
                "not a factor — 'x2' parses as x^2. Check the 'parsed' field; write 'x*2' " +
                "if you meant multiplication, and avoid variable names ending in a digit.");

        foreach (Match m in calls)
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
