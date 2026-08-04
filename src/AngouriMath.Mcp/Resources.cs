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
            ["uri"] = "angourimath://curiosities",
            ["name"] = "Verified mathematical curiosities",
            ["description"] = "Famous results and coincidences, each with the exact " +
                              "expression to reproduce it on this server. Doubles as a " +
                              "self-test corpus.",
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
        "angourimath://curiosities" => Curiosities,
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

    private const string Curiosities = """
        # Curiosities, each verified on this server

        Every entry below was checked here, not copied from memory. The expression given is
        exactly what to run — so this doubles as a self-test corpus. If one of these stops
        reproducing, something regressed.

        ## Exact identities

        | Result | Run |
        |---|---|
        | Euler's identity, exactly 0 — not 1e-16 | `am_evaluate  e^(i*pi) + 1` |
        | Machin's formula: how π was computed to 100 digits by hand in 1706 | `am_verify_equal  4*arctan(1/5) - arctan(1/239)  vs  pi/4` |
        | Two arctans make a right angle's eighth | `am_verify_equal  arctan(1/2) + arctan(1/3)  vs  pi/4` |
        | Golden ratio is a pentagon in disguise | `am_verify_equal  (1+sqrt(5))/2  vs  2*cos(pi/5)` |
        | φ from its defining property | `am_solve  ['x^2 = x + 1', 'x > 0']  for x` |
        | Ramanujan's taxicab: 1729 two ways | `am_verify_equal  1^3 + 12^3  vs  9^3 + 10^3` |

        ## A machine-checked proof that 22/7 > π

        The integrand is positive on (0,1), so the integral is positive — which proves the
        inequality. It needs the polynomial division done by hand first, because the
        integrator declines the unexpanded rational form:

        1. `am_verify_equal  x^4*(1-x)^4/(1+x^2)  vs  x^6 - 4*x^5 + 5*x^4 - 4*x^2 + 4 - 4/(1+x^2)`
        2. `am_integrate  x^6 - 4*x^5 + 5*x^4 - 4*x^2 + 4 - 4/(1+x^2)  dx`  (verifies)
        3. `am_evaluate  1/7 - 4/6 + 1 - 4/3 + 4 - 4*arctan(1)`  → **exactly `22/7 - pi`**

        ## Near misses — where floating point would lie to you

        | Result | Run |
        |---|---|
        | Homer Simpson's Fermat counterexample. Agrees to **ten** significant figures — exactly a pocket calculator's width, which is the joke | `am_evaluate  3987^12 + 4365^12 - 4472^12` → 1211886809373872630985912112862690, not 0 |
        | 42 as a sum of three cubes (Booker & Sutherland, 2019). Each term is ~5e50, so a double returns pure noise | `am_evaluate  (-80538738812075974)^3 + 80435758145817515^3 + 12602123297335631^3` → 42 |
        | 355/113, the best simple approximation to π (Milü, 5th century) | `am_evaluate  355/113 - pi  digits 20` → 2.67e-7 |
        | Ramanujan's quartic approximation to π | `am_evaluate  (2143/22)^(1/4) - pi  digits 20` → -1.01e-9 |
        | One term of Ramanujan's 1/π series already gives 8 digits | `am_evaluate  9801/(2*sqrt(2)*1103) - pi  digits 20` → 7.6e-8 |
        | e^π − π is almost exactly 20, for no known reason | `am_evaluate  e^pi - pi  digits 20` → 19.999099979189475768 |

        ## 42

        | Result | Run |
        |---|---|
        | Adams' "six by nine" is true in base 13 | `am_base_convert  54  to base 13` → 42 |
        | 42 = 101010, a perfect alternating bit pattern | `am_base_convert  42  to base 2` |
        | The 5th Catalan number — counts the ways to parenthesise six factors | `am_evaluate  10! / (6! * 5!)` |
        | 6¹ + 6², and also the sum of the first six even numbers | `am_evaluate  6^1 + 6^2` |
        | The rainbow really is at 42°: minimise deviation through a raindrop | `am_solve ['(4/3)^2 - 1 = 3*c^2','c > 0'] for c` → `sqrt(7/27)`, giving 42.03° |

        ## Known to be WRONG here — do not quote these

        - `e^(pi*sqrt(163))` (Ramanujan's constant) should be
          `262537412640768743.99999999999925...`, sitting 7.5e-13 below an integer. This
          build returns `...744.000000000024` at 30 digits — on the wrong side of the integer,
          so the near-miss that makes the number famous is not reproduced. The exact wrong
          value shifts between builds, which is itself a sign the computation is unstable. Every component evaluates
          correctly; only the composed form fails.
        - `sqrt(x^2)` simplifies to `x` rather than `abs(x)`.

        ## Beyond this server

        No infinite series: there is no summation operator, so Basel (π²/6) and the Leibniz
        series cannot be evaluated here. Worth knowing anyway — truncating Leibniz at
        500,000 terms gives
        `3.14159065358979324046264338326950` against π's
        `3.14159265358979323846264338327950`: nearly every digit correct, with isolated
        wrong ones. Those errors are not noise — they are the Euler numbers.

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
        - **Output tidiness** varies. Results are correct but not always in the form a human
          would write. Pass `alternatives: true` to `am_simplify` and pick a nicer form.
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
