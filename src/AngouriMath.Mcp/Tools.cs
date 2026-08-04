using System.Text.Json.Nodes;
using AngouriMath;
using AngouriMath.Core;
using AngouriMath.Extensions;
using static AngouriMath.Entity;

namespace AngouriMath.Mcp;

public static class Tools
{
    // ---------------------------------------------------------------- schemas

    private static JsonObject Str(string desc) =>
        new() { ["type"] = "string", ["description"] = desc };

    private static JsonObject Schema(JsonObject props, params string[] required)
    {
        var req = new JsonArray();
        foreach (var r in required) req.Add(r);
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = req,
        };
    }

    private static JsonObject Tool(string name, string description, JsonObject schema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = schema,
        // Pure computation: no side effects, no network, no filesystem. Declaring this lets
        // clients auto-approve instead of prompting on every call — and a math tool that
        // costs a permission click per invocation simply will not get used.
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = true,
            ["destructiveHint"] = false,
            ["idempotentHint"] = true,
            ["openWorldHint"] = false,
        },
    };

    private const string CallEvenIfConfident =
        " Call this even when you believe you can do the algebra unaided — unaided symbolic " +
        "manipulation is unreliable for exactly this class of problem, and the result here is " +
        "machine-checked.";

    public static JsonArray List() =>
    [
        Tool("am_parse",
            "Parse an expression and show exactly how AngouriMath understood it. Use this " +
            "first whenever an expression contains identifiers with digits (x2, v1) or " +
            "function-looking names, because both are silently misread. Returns the canonical " +
            "form, LaTeX, free variables, and warnings.",
            Schema(new JsonObject
            {
                ["expression"] = Str("Expression in AngouriMath syntax, e.g. 'x^2 + 3*x - 1'."),
                ["strict"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Reject implicit multiplication instead of guessing. Default false.",
                },
            }, "expression")),

        Tool("am_simplify",
            "Simplify an expression exactly." + CallEvenIfConfident +
            " Set alternatives=true to get several candidate forms ranked by complexity, " +
            "which is useful because the default form is not always the tidiest.",
            Schema(new JsonObject
            {
                ["expression"] = Str("Expression to simplify."),
                ["alternatives"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Also return alternative simplified forms. Default false.",
                },
            }, "expression")),

        Tool("am_solve",
            "Solve an equation, inequality, or a set of simultaneous constraints for one " +
            "variable. Constraints are combined, so you can narrow a solution incrementally " +
            "(e.g. ['x^2 = 4', 'x > 0'] gives 2)." + CallEvenIfConfident,
            Schema(new JsonObject
            {
                ["constraints"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "One or more statements, e.g. ['x^2 - 1 = 0'] or ['x^2=4','x>0'].",
                },
                ["variable"] = Str("Variable to solve for, e.g. 'x'."),
            }, "constraints", "variable")),

        Tool("am_differentiate",
            "Differentiate an expression with respect to a variable." + CallEvenIfConfident,
            Schema(new JsonObject
            {
                ["expression"] = Str("Expression to differentiate."),
                ["variable"] = Str("Variable to differentiate by."),
                ["order"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Order of the derivative. Default 1.",
                },
            }, "expression", "variable")),

        Tool("am_integrate",
            "Integrate an expression symbolically. The answer is ALWAYS verified by " +
            "differentiating it back and comparing numerically; check the 'verified' field. " +
            "If no elementary antiderivative exists the status is 'declined', which is a " +
            "correct answer, not a failure." + CallEvenIfConfident,
            Schema(new JsonObject
            {
                ["expression"] = Str("Integrand."),
                ["variable"] = Str("Variable of integration."),
            }, "expression", "variable")),

        Tool("am_limit",
            "Compute a limit, optionally one-sided." + CallEvenIfConfident,
            Schema(new JsonObject
            {
                ["expression"] = Str("Expression."),
                ["variable"] = Str("Variable approaching the destination."),
                ["to"] = Str("Destination, e.g. '0', '+oo', '-oo'."),
                ["side"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "both", "left", "right" },
                    ["description"] = "Approach direction. Default 'both'.",
                },
            }, "expression", "variable", "to")),

        Tool("am_evaluate",
            "Evaluate an expression numerically, optionally substituting values first. " +
            "Returns exact form and a decimal approximation.",
            Schema(new JsonObject
            {
                ["expression"] = Str("Expression to evaluate."),
                ["substitutions"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Optional map of variable -> value, e.g. {\"x\": \"2\"}.",
                },
            }, "expression")),

        Tool("am_verify_equal",
            "Check whether two expressions are mathematically equal. Use this to check your " +
            "OWN algebra: give it your hand-derived form and the original, and it will tell " +
            "you if you changed the meaning. Tries exact simplification first, then falls " +
            "back to numeric sampling.",
            Schema(new JsonObject
            {
                ["left"] = Str("First expression."),
                ["right"] = Str("Second expression."),
            }, "left", "right")),

        Tool("am_truth_table",
            "Build a truth table for a boolean expression, and list the assignments that " +
            "satisfy it. Useful for reasoning about branch conditions and predicates. " +
            "Operators: 'and', 'or', 'not', 'xor', 'implies'.",
            Schema(new JsonObject
            {
                ["expression"] = Str("Boolean expression, e.g. 'a and (b or not c)'."),
                ["variables"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Variable order. Inferred from the expression if omitted.",
                },
            }, "expression")),

        Tool("am_solve_system",
            "Solve a system of simultaneous equations for several variables. Returns one row " +
            "per solution. Note: nonlinear systems may return no solution even when one " +
            "exists (a known library limitation).",
            Schema(new JsonObject
            {
                ["equations"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Equations, e.g. ['x + y = 3', 'x - y = 1'].",
                },
                ["variables"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Variables to solve for, e.g. ['x','y'].",
                },
            }, "equations", "variables")),

        Tool("am_to_sympy",
            "Emit a runnable SymPy program for an expression. Use this to hand the problem to " +
            "Python, or to cross-check a result against a second computer algebra system.",
            Schema(new JsonObject { ["expression"] = Str("Expression to translate.") },
                "expression")),
    ];

    // ---------------------------------------------------------------- dispatch

    public static JsonObject Call(string name, JsonObject args) => name switch
    {
        "am_parse" => Parse(args),
        "am_simplify" => Simplify(args),
        "am_solve" => Solve(args),
        "am_differentiate" => Differentiate(args),
        "am_integrate" => Integrate(args),
        "am_limit" => Limit(args),
        "am_evaluate" => Evaluate(args),
        "am_verify_equal" => VerifyEqual(args),
        "am_truth_table" => TruthTable(args),
        "am_solve_system" => SolveSystem(args),
        "am_to_sympy" => ToSympy(args),
        _ => Fail($"unknown tool '{name}'"),
    };

    // ---------------------------------------------------------------- helpers

    private static string? S(JsonObject o, string key) =>
        o.TryGetPropertyValue(key, out var v) ? v?.GetValue<string>() : null;

    private static bool B(JsonObject o, string key, bool fallback = false) =>
        o.TryGetPropertyValue(key, out var v) && v is not null ? v.GetValue<bool>() : fallback;

    private static List<string> A(JsonObject o, string key)
    {
        var list = new List<string>();
        if (o.TryGetPropertyValue(key, out var v) && v is JsonArray arr)
            foreach (var item in arr)
                if (item is not null) list.Add(item.GetValue<string>());
        return list;
    }

    private static JsonObject Fail(string error) =>
        new() { ["status"] = "failed", ["error"] = error };

    private static JsonArray Warn(List<string> warnings)
    {
        var arr = new JsonArray();
        foreach (var w in warnings) arr.Add(w);
        return arr;
    }

    /// <summary>Parse one input, or produce the error object.</summary>
    private static bool TryParse(string? src, string label, bool strict,
        out Entity entity, out List<string> warnings, out JsonObject? error)
    {
        entity = 0; warnings = []; error = null;
        if (string.IsNullOrWhiteSpace(src))
        {
            error = Fail($"'{label}' is required");
            return false;
        }

        var outcome = Parsing.Parse(src, strict);
        if (outcome.Entity is null)
        {
            error = Fail(outcome.Error ?? "parse failed");
            return false;
        }

        entity = outcome.Entity;
        warnings = outcome.Warnings;
        return true;
    }

    /// <summary>
    /// Build the standard response. The order matters: decline detection runs on the RAW
    /// stringized result before anything simplifies it, because an unevaluated limit(...)
    /// simplifies to NaN and would otherwise be reported as a wrong answer.
    /// </summary>
    private static JsonObject Respond(Entity input, Entity result, List<string> warnings)
    {
        var raw = result.Stringize();
        var status =
            Guard.IsDeclined(raw) ? "declined"
            : raw == input.Stringize() ? "unchanged"
            : Guard.LooksLikeNaN(raw) ? "suspect"
            : "solved";

        var response = new JsonObject
        {
            ["status"] = status,
            ["parsed"] = input.Stringize(),
            ["result"] = raw,
            ["latex"] = result.Latexise(),
        };

        if (status == "declined")
            response["note"] = "AngouriMath returned the expression unevaluated: it has no " +
                "rule for this input. This is a decline, not a wrong answer — do not present " +
                "the unevaluated form as a result.";
        if (status == "unchanged")
            response["note"] = "The result is identical to the input. The library could not " +
                "reduce it further; treat this as 'no progress', not as a simplest form.";
        if (status == "suspect")
            response["note"] = "The result contains NaN, which is almost never a legitimate " +
                "answer here. Treat it as a library failure.";

        if (warnings.Count > 0) response["warnings"] = Warn(warnings);
        return response;
    }

    private static JsonObject FromOutcome<T>(Guard.Outcome<T> outcome, Func<T, JsonObject> ok) =>
        outcome.Status switch
        {
            "ok" => ok(outcome.Value!),
            "timeout" => new JsonObject
            {
                ["status"] = "timeout",
                ["error"] = outcome.Error,
                ["note"] = "The call exceeded its budget and was abandoned. Try a simpler " +
                           "form, or accept that this input is out of reach.",
            },
            _ => Fail(outcome.Error ?? "unknown failure"),
        };

    private static Variable Var(string name) => MathS.Var(name);

    // Numeric comparison, `provided`-stripping and the sample points all live in Numeric.cs.

    // ---------------------------------------------------------------- tools

    private static JsonObject Parse(JsonObject args)
    {
        if (!TryParse(S(args, "expression"), "expression", B(args, "strict"),
                out var e, out var warnings, out var error))
            return error!;

        var vars = new JsonArray();
        foreach (var v in e.Vars) vars.Add(v.Stringize());

        var response = new JsonObject
        {
            ["status"] = "solved",
            ["parsed"] = e.Stringize(),
            ["result"] = e.Stringize(),
            ["latex"] = e.Latexise(),
            ["free_variables"] = vars,
            ["node_count"] = e.Nodes.Count(),
        };
        if (warnings.Count > 0) response["warnings"] = Warn(warnings);
        return response;
    }

    private static JsonObject Simplify(JsonObject args)
    {
        if (!TryParse(S(args, "expression"), "expression", false,
                out var e, out var warnings, out var error))
            return error!;

        var wantAlternatives = B(args, "alternatives");

        return FromOutcome(Guard.Run(() =>
        {
            var simplified = e.Simplify();
            var alternates = new List<string>();
            if (wantAlternatives)
                foreach (var alt in e.Alternate(4).Take(5))
                {
                    var s = alt.Stringize();
                    if (s != simplified.Stringize() && !alternates.Contains(s))
                        alternates.Add(s);
                }
            return (simplified, alternates);
        }), r =>
        {
            var response = Respond(e, r.simplified, warnings);
            if (r.alternates.Count > 0)
            {
                var arr = new JsonArray();
                foreach (var a in r.alternates) arr.Add(a);
                response["alternatives"] = arr;
                response["alternatives_note"] =
                    "Ranked by the library's complexity metric. The first result is not " +
                    "always the tidiest for a human — pick whichever form suits.";
            }
            return response;
        });
    }

    private static JsonObject Solve(JsonObject args)
    {
        var constraints = A(args, "constraints");
        var variable = S(args, "variable");
        if (constraints.Count == 0) return Fail("'constraints' must contain at least one statement");
        if (string.IsNullOrWhiteSpace(variable)) return Fail("'variable' is required");

        // Combining with 'and' turns several constraints into a single boolean statement,
        // which Solve handles natively (it accepts statements and inequalities, not just
        // equations). Borrowed from the SharpCells Excel example.
        var combined = string.Join(" and ", constraints);

        if (!TryParse(combined, "constraints", false, out var e, out var warnings, out var error))
            return error!;

        return FromOutcome(Guard.Run(() => e.Solve(Var(variable!))), set =>
        {
            var response = Respond(e, set, warnings);
            if (set is Set.FiniteSet finite)
            {
                var roots = new JsonArray();
                foreach (var root in finite)
                {
                    // Solve leaves roots in raw form: solving f = 1/(2*pi*R*C) for R yields
                    // "--1/2 * 1/pi * 1/C/f". Correct, but a double negation is exactly the
                    // output-tidiness weakness the library is known for, so tidy each root.
                    // Guarded because Simplify can be slow and must not lose the answer.
                    Entity tidy;
                    try { tidy = root.Simplify(); }
                    catch { tidy = root; }
                    roots.Add(tidy.Stringize());
                }
                response["solutions"] = roots;
                response["solution_count"] = finite.Count;
            }
            else
            {
                response["note"] = "The solution set is not finite (an interval, a " +
                    "conditional set, or a periodic family). Read 'result' directly.";
            }
            return response;
        });
    }

    private static JsonObject Differentiate(JsonObject args)
    {
        if (!TryParse(S(args, "expression"), "expression", false,
                out var e, out var warnings, out var error))
            return error!;
        var variable = S(args, "variable");
        if (string.IsNullOrWhiteSpace(variable)) return Fail("'variable' is required");

        var order = args.TryGetPropertyValue("order", out var o) && o is not null
            ? o.GetValue<int>() : 1;
        if (order < 1) return Fail("'order' must be at least 1");

        return FromOutcome(
            Guard.Run(() => e.Differentiate(Var(variable!), order).Simplify()),
            d => Respond(e, d, warnings));
    }

    private static JsonObject Integrate(JsonObject args)
    {
        if (!TryParse(S(args, "expression"), "expression", false,
                out var e, out var warnings, out var error))
            return error!;
        var variable = S(args, "variable");
        if (string.IsNullOrWhiteSpace(variable)) return Fail("'variable' is required");

        return FromOutcome(Guard.Run(() =>
        {
            var v = Var(variable!);
            var antiderivative = e.Integrate(v);

            // Decline check on the RAW result, before Simplify — see Guard.IsDeclined.
            if (Guard.IsDeclined(antiderivative.Stringize()))
                return (antiderivative, (bool?)null);

            var tidy = antiderivative.Simplify();

            // Verify by differentiating back. This is exactly what work/casbench does, and
            // it is the whole reason to trust the answer.
            bool? verified;
            try
            {
                verified = Numeric.Equal(tidy.Differentiate(v), e);
            }
            catch
            {
                verified = null;
            }

            return (tidy, verified);
        }), r =>
        {
            var response = Respond(e, r.Item1, warnings);
            response["verified"] = r.Item2;
            response["verification_method"] =
                "differentiated the answer and compared it numerically to the integrand at " +
                "five positive real points";

            if (r.Item2 == false)
            {
                response["status"] = "suspect";
                response["note"] = "The answer does NOT differentiate back to the integrand. " +
                    "Do not use it.";
            }
            else if (r.Item2 is null && (string?)response["status"] == "declined")
            {
                response["note"] = "No antiderivative was found. For some integrands " +
                    "(e.g. e^x/x, e^(x^2)) no elementary antiderivative exists at all, so " +
                    "this is the mathematically correct outcome.";
            }
            return response;
        });
    }

    private static JsonObject Limit(JsonObject args)
    {
        if (!TryParse(S(args, "expression"), "expression", false,
                out var e, out var warnings, out var error))
            return error!;
        var variable = S(args, "variable");
        var to = S(args, "to");
        if (string.IsNullOrWhiteSpace(variable)) return Fail("'variable' is required");
        if (string.IsNullOrWhiteSpace(to)) return Fail("'to' is required");

        if (!TryParse(to, "to", false, out var destination, out _, out var destError))
            return destError!;

        var side = (S(args, "side") ?? "both").ToLowerInvariant() switch
        {
            "left" => ApproachFrom.Left,
            "right" => ApproachFrom.Right,
            _ => ApproachFrom.BothSides,
        };

        return FromOutcome(Guard.Run(() =>
        {
            var raw = e.Limit(Var(variable!), destination, side);
            // Check for a decline BEFORE simplifying: an unevaluated limit(...) simplifies
            // to NaN, which would look like a wrong answer instead of a missing solver.
            return Guard.IsDeclined(raw.Stringize()) ? raw : raw.Simplify();
        }), r =>
        {
            var response = Respond(e, r, warnings);
            if ((string?)response["status"] == "suspect")
            {
                // NaN from a genuinely evaluated limit means "does not exist", which is a
                // real answer rather than a library failure.
                response["status"] = "no_limit";
                response["note"] = "The limit evaluated to NaN, which here means the limit " +
                    "does not exist (e.g. 1/x as x -> 0 from both sides).";
            }
            return response;
        });
    }

    private static JsonObject Evaluate(JsonObject args)
    {
        if (!TryParse(S(args, "expression"), "expression", false,
                out var e, out var warnings, out var error))
            return error!;

        var substitutions = new List<(string Name, Entity Value)>();
        if (args.TryGetPropertyValue("substitutions", out var subsNode) && subsNode is JsonObject subs)
        {
            foreach (var (key, value) in subs)
            {
                if (value is null) continue;
                if (!TryParse(value.GetValue<string>(), $"substitutions.{key}", false,
                        out var replacement, out _, out var subError))
                    return subError!;
                substitutions.Add((key, replacement));
            }
        }

        var input = e;
        return FromOutcome(Guard.Run(() =>
        {
            // Resolve calculus nodes BEFORE substituting. Substituting into
            // derivative(f(x), x) cannot work — x is bound there — so `d/dx sqrt(1+x) at
            // x=0` silently came back with x still free. Differentiate first, then plug in.
            var resolved = input.InnerSimplified;
            foreach (var (name, value) in substitutions)
                resolved = resolved.Substitute(Var(name), value);

            // Simplify, not Evaled: Evaled numericises aggressively, turning
            // sin(pi/3) + cos(pi/6) into a hundred decimal digits and throwing away the
            // exact sqrt(3) that is the whole reason to use a CAS.
            var exact = resolved.Simplify();

            string? approx = null;
            if (exact.EvaluableNumerical)
            {
                var complex = exact.EvalNumerical();
                approx = complex.ImaginaryPart.EDecimal.IsZero
                    ? complex.RealPart.EDecimal.ToDouble().ToString("R")
                    : complex.Stringize();
            }
            return (exact, approx);
        }), r =>
        {
            var response = Respond(input, r.exact, warnings);
            if (r.approx is not null) response["approximate"] = r.approx;
            else response["note"] = "The expression is not fully numeric — it still contains " +
                "free variables. Supply 'substitutions' to get a number.";
            return response;
        });
    }

    private static JsonObject VerifyEqual(JsonObject args)
    {
        if (!TryParse(S(args, "left"), "left", false, out var left, out var lw, out var le))
            return le!;
        if (!TryParse(S(args, "right"), "right", false, out var right, out var rw, out var re))
            return re!;

        return FromOutcome(Guard.Run(() =>
        {
            // Exact first: if the difference reduces to zero, that settles it. The zero test
            // has to look through any `provided` guard — the difference of two equal
            // expressions is routinely "0 provided x > 0", which is still zero.
            var difference = (left - right).Simplify();
            var exactlyZero = Numeric.IsZero(difference);

            bool? numerically = null;
            bool? wholeLine = null;
            if (!exactlyZero)
            {
                // Compare the DIFFERENCE against zero rather than the two sides against
                // each other. Simplify has already collapsed any unevaluated derivative(…)
                // or integral(…) node in the difference, whereas the raw sides may still
                // contain one — and an unevaluated node is not numerically evaluable, so
                // comparing the sides directly returns "inconclusive" for inputs that are
                // in fact decidable.
                try { numerically = Numeric.Equal(difference, 0); }
                catch { numerically = null; }

                // The positive-real points above cannot see a disagreement that only occurs
                // on the negatives (abs(x) vs x, say). A second pass across the whole real
                // line catches that.
                if (numerically == true)
                {
                    try
                    {
                        wholeLine = Numeric.Equal(difference, 0,
                            ["-3.1", "-1.7", "-0.4", "0.8", "2.6"]);
                    }
                    catch { wholeLine = null; }
                }
            }

            return (difference, exactlyZero, numerically, wholeLine);
        }), r =>
        {
            var equal = r.exactlyZero || r.numerically == true;
            // Inconclusive is reported as null, never as false: "we could not check" is not
            // the same claim as "they differ", and a caller acting on a bare false would be
            // acting on something the server never established.
            var conclusive = r.exactlyZero || r.numerically is not null;

            var response = new JsonObject
            {
                ["status"] = conclusive ? "solved" : "inconclusive",
                ["equal"] = conclusive ? equal : null,
                ["method"] = r.exactlyZero
                    ? "exact: left - right simplified to 0"
                    : r.numerically is null
                        ? "inconclusive: neither side could be evaluated at any sample point"
                        : "numeric: sampled at five positive real points, relative tolerance 1e-6",
                ["difference"] = r.difference.Stringize(),
                ["left_parsed"] = left.Stringize(),
                ["right_parsed"] = right.Stringize(),
                ["note"] = equal
                    ? "The two expressions agree."
                    : r.numerically is null
                        ? "Could not establish equality either way. Not proof of inequality."
                        : "The expressions differ. Check 'difference' to see by how much.",
                ["warnings"] = Warn([.. lw, .. rw]),
            };

            if (equal && r.wholeLine == false)
            {
                response["equal_only_on_positive_reals"] = true;
                response["note"] =
                    "The expressions agree for positive real inputs but disagree elsewhere " +
                    "on the real line — the usual cause is a logarithm, square root or " +
                    "absolute value. Treat them as equal only if your domain is restricted " +
                    "to the positives.";
            }

            return response;
        });
    }

    private static JsonObject TruthTable(JsonObject args)
    {
        if (!TryParse(S(args, "expression"), "expression", false,
                out var e, out var warnings, out var error))
            return error!;

        var names = A(args, "variables");
        var vars = names.Count > 0
            ? names.Select(Var).ToArray()
            : e.Vars.ToArray();

        if (vars.Length == 0) return Fail("no variables found in the expression");
        if (vars.Length > 12) return Fail($"{vars.Length} variables would need {1L << vars.Length} rows; cap is 12");

        return FromOutcome(Guard.Run(() =>
        {
            var table = MathS.Boolean.BuildTruthTable(e, vars);
            var satisfying = MathS.SolveBooleanTable(e, vars);
            return (table, satisfying);
        }), r =>
        {
            var order = new JsonArray();
            foreach (var v in vars) order.Add(v.Stringize());

            var response = new JsonObject
            {
                ["status"] = r.table is null ? "failed" : "solved",
                ["parsed"] = e.Stringize(),
                ["variable_order"] = order,
                ["truth_table"] = r.table?.ToString(multilineFormat: true),
                ["satisfiable"] = r.satisfying is not null,
                ["satisfying_assignments"] = r.satisfying?.ToString(multilineFormat: true),
            };
            if (r.satisfying is null)
                response["note"] = "No assignment satisfies this expression — it is a contradiction.";
            if (warnings.Count > 0) response["warnings"] = Warn(warnings);
            return response;
        });
    }

    private static JsonObject SolveSystem(JsonObject args)
    {
        var equations = A(args, "equations");
        var variables = A(args, "variables");
        if (equations.Count == 0) return Fail("'equations' must not be empty");
        if (variables.Count == 0) return Fail("'variables' must not be empty");

        var parsed = new List<Entity>();
        var warnings = new List<string>();
        foreach (var eq in equations)
        {
            if (!TryParse(eq, "equations", false, out var e, out var w, out var error))
                return error!;

            // EquationSystem wants each equation in "= 0" form. Handing it an actual
            // equality statement throws FutureReleaseException, so normalise here — writing
            // 'x + y = 3' is the natural thing for a caller to do, and it should just work.
            parsed.Add(e is Equalsf equality ? equality.Left - equality.Right : e);
            warnings.AddRange(w);
        }

        return FromOutcome(Guard.Run(() =>
            MathS.Equations([.. parsed]).Solve([.. variables.Select(Var)])), matrix =>
        {
            var order = new JsonArray();
            foreach (var v in variables) order.Add(v);

            var response = new JsonObject
            {
                ["status"] = matrix is null ? "declined" : "solved",
                ["variable_order"] = order,
                ["solutions"] = matrix?.ToString(multilineFormat: true),
                ["solution_count"] = matrix?.RowCount,
            };

            if (matrix is null)
                response["note"] = "No solution was returned. Note that nonlinear systems " +
                    "can return nothing even when a solution exists (issue #629) — absence " +
                    "here is not proof that the system is unsolvable.";

            if (warnings.Count > 0) response["warnings"] = Warn(warnings);
            return response;
        });
    }

    private static JsonObject ToSympy(JsonObject args)
    {
        if (!TryParse(S(args, "expression"), "expression", false,
                out var e, out var warnings, out var error))
            return error!;

        var response = new JsonObject
        {
            ["status"] = "solved",
            ["parsed"] = e.Stringize(),
            ["sympy"] = MathS.ToSympyCode(e),
        };
        if (warnings.Count > 0) response["warnings"] = Warn(warnings);
        return response;
    }
}
