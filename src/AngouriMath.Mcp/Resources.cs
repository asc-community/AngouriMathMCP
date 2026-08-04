using System.Text.Json.Nodes;

namespace AngouriMath.Mcp;

/// <summary>
/// Two documents the model can read. Zero code, and arguably the highest value-per-effort
/// item in the whole server: the reliability map is empirical per-category data from
/// work/coverage.md, which tells the model when to trust a result and when to expect a
/// decline. Nobody else wrapping a CAS can ship this, because nobody else measured it.
/// </summary>
public static class Resources
{
    public static JsonArray List() =>
    [
        new JsonObject
        {
            ["uri"] = "angourimath://syntax",
            ["name"] = "AngouriMath expression syntax",
            ["description"] = "How to write expressions for this server, including the two " +
                              "parsing traps that silently change meaning. Read before " +
                              "composing a non-trivial expression.",
            ["mimeType"] = "text/markdown",
        },
        new JsonObject
        {
            ["uri"] = "angourimath://reliability",
            ["name"] = "Measured reliability by problem class",
            ["description"] = "Which operations are trustworthy and which are known to " +
                              "decline or mislead, measured against a 117-problem corpus.",
            ["mimeType"] = "text/markdown",
        },
    ];

    public static string? Read(string uri) => uri switch
    {
        "angourimath://syntax" => Syntax,
        "angourimath://reliability" => Reliability,
        _ => null,
    };

    private const string Syntax = """
        # AngouriMath expression syntax

        Plain text. `x^2 + 3*x - 1`, `sin(x)/x`, `sqrt(2)`, `e^x`, `+oo` / `-oo` for infinity.

        ## Two traps that change the meaning of your expression without any error

        **1. A number directly after an identifier is an EXPONENT, not a factor.**

        | You write | It means |
        |---|---|
        | `x2`        | x²             |
        | `2x`        | 2·x            |
        | `2(g + e)3` | 2·(g + e)³     |

        So a variable named `x2`, `v1` or `a0` is silently squared / raised. Avoid variable
        names ending in a digit; write `x_2` or `xa` instead. Always read the `parsed` field
        that every response returns.

        **2. An unknown function name becomes a multiplication.**

        `pow(x, y)` is not a function here — it lexes as `p*o*w(...)`. `arcsinh(x)` becomes
        the product `arcsinh * x`. The parse succeeds and produces a different expression.

        Known functions: `sin cos tan cotan sec cosec`, `arcsin arccos arctan arccotan`,
        `sinh cosh tanh cotanh sech cosech`, `arsinh arcosh artanh arcotanh`,
        `ln log sqrt cbrt sqr abs signum gamma factorial phi`,
        `derivative integral limit piecewise provided apply lambda`.

        ## Spellings worth knowing (verified against the grammar, not guessed)

        | You might write | Reality |
        |---|---|
        | `factorial(10)` | **silently becomes a variable.** Use postfix: `10!` |
        | `exp(x)`        | **silently becomes `exp * x`.** Write `e^x` |
        | `min(a,b)`, `max(a,b)` | silently become variables. Use `piecewise` |
        | `pow(x,y)`      | works *here* (issue #625) but lexes as `p*o*w` on the release |
        | `mod`, `%`      | no modulus in this grammar at all |
        | `log(2, 8)`     | correct — base first, then argument |

        The "silently becomes a variable" cases are the dangerous ones: they parse, and they
        mean something else. Anything else followed by `(` gets a warning.

        ## Statements

        `=` is equality (`x^2 = 4`), and `>`, `<`, `>=`, `<=` are comparisons. Combine with
        `and`, `or`, `not`, `xor`, `implies`. `am_solve` accepts these, so you can solve
        under constraints: `['x^2 = 4', 'x > 0']` yields `2`.

        ## LaTeX

        LaTeX is OUTPUT only. This server cannot parse LaTeX input — convert
        `\frac{a}{b}` to `a/b` yourself before calling.
        """;

    private const string Reliability = """
        # Measured reliability

        ## What this server is running against

        **A work-in-progress development branch, not a released version.** Fixes are actively
        being written and proposed upstream, so behaviour here differs from the published
        AngouriMath package and will keep changing. Two consequences:

        - Do not assume a result you get here matches what the released library would give.
          Problems that hang or answer wrongly on the release are fixed on this build.
        - Conversely, the figures below are a snapshot. If something contradicts them, trust
          the tool's own `status` and `verified` fields over this document, and say so.

        Measured against a 117-problem corpus (drawn from SymPy's test suite, the Rubi
        integration suite, and the Gruntz thesis), 20 s budget per problem. Answers are not
        trusted on their face: integrals are checked by differentiating back, equation roots
        by substituting them in. Current score on this build: **111/117, 0 wrong, 0 hangs**.

        ## Trustworthy

        - **Equations** — all 12 categories at 100%: linear, quadratic, cubic, quartic,
          higher polynomial, rational, radical, trigonometric, exponential, logarithmic,
          absolute-value, transcendental. Every root was residual-verified.
        - **Derivatives** — 0 failures across 151 expressions checked against a difference
          quotient. Fast and dependable.
        - **Integrals** — 11 of 13 categories at 100%: table forms, linearity, f(ax+b),
          u-substitution, by parts (including cyclic), arctan and arcsin forms,
          rational-quadratic, trig powers, trig substitution.
        - **Limits** — 7 of 8 categories at 100%, including Gruntz-class, 0/0, ∞/∞, 1^∞, ∞−∞.

        ## Expect a decline (this is correct behaviour, not a bug)

        - `∫ e^x/x` and `∫ e^(x^2)` — **no elementary antiderivative exists**. Nothing can
          do these in closed form; do not retry or reword.
        - `∫ sqrt(tan(x))`, `∫ x^2/(x^4+1)` — no rule; the second factors only over the
          irrationals.
        - Limits requiring factorial asymptotics, e.g. `lim x→∞ (x!/x^x)^(1/x)`.

        ## Where results mislead

        - **`sqrt(x^2)` simplifies to `x`, not `abs(x)`.** The library then disagrees with
          itself: evaluating `sqrt(x^2)` at x = -2 gives 2, while the simplified form gives
          -2. Any simplification that removes an even root over an even power is suspect on
          the negatives. `am_verify_equal` cross-checks for this and reports
          `status: conflict`; believe the conflict over the simplification.
        - **Simplify on multivariate rational functions** returns something equivalent but
          **unreduced, with no error** — e.g. `(x^2+2xy+y^2)/(x^2-y^2)` is not cancelled.
          A `status` of `unchanged` means no progress, not "already simplest".
        - **Simplify is not canonical**: `sqrt(12)+sqrt(27)` may come back as `sqrt(3)*5`
          while `5*sqrt(3)` is left alone. Two different-looking outputs can be equal — use
          `am_verify_equal` rather than comparing strings.
        - **Output tidiness** is a genuine weakness: `expand (x+1)^3` gives
          `1 + x^3 + 3*(x + x^2)` rather than the collected form. Pass `alternatives: true`
          to `am_simplify` and pick a nicer one.
        - **Nonlinear systems** can return no solution when one exists (issue #629).

        ## How to talk about a result

        State the status you were given. If a tool returns `declined`, say the library has no
        rule for it rather than presenting the unevaluated form as an answer. If it returns
        `unchanged`, say it made no progress. If `verified` is false or a `conflict` is
        reported, do not use the result. Say which tool produced a number rather than
        implying you worked it out.

        ## Soundness note

        Unlike some CAS libraries, this one tracks domain conditions: `(x^2-1)/(x-1)`
        simplifies to `x + 1 provided not x - 1 = 0`, rather than an unconditional `x + 1`
        that is wrong at x = 1. The `provided` clause is a feature — preserve it.
        """;
}
