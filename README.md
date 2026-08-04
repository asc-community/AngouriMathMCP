# angourimath-mcp

An MCP server exposing [AngouriMath](https://github.com/asc-community/AngouriMath) — exact
symbolic algebra — to an LLM agent. Prototype.

The thesis is not "give the model a calculator". Models are confident they can do algebra,
so a tool that merely offers to do it for them goes unused. The thesis is **verification**:
every integral is checked by differentiating it back, every decline is reported as a decline
rather than dressed up as an answer, and every response echoes what the server actually
parsed. The model has no ego about being checked.

## Build and run

```sh
dotnet build -c Release src/AngouriMath.Mcp
./test/smoke.sh                      # end-to-end over real stdio JSON-RPC
```

Register it (Claude Code):

```sh
claude mcp add angourimath -- /abs/path/to/src/AngouriMath.Mcp/bin/Release/net10.0/angourimath-mcp
```

Speaks newline-delimited JSON-RPC 2.0 on stdin/stdout, protocol revision `2024-11-05`. No
external NuGet dependencies — only `System.Text.Json` and AngouriMath itself.

## Tools

| Tool | Notes |
|---|---|
| `am_parse` | Echoes the canonical parse, LaTeX, free variables, and warnings. |
| `am_simplify` | `alternatives: true` returns several candidate forms. |
| `am_solve` | Takes a *list* of constraints, combined with `and`. Handles inequalities. |
| `am_differentiate` | Any order. |
| `am_integrate` | Always verified by differentiating back; see `verified`. |
| `am_limit` | One-sided via `side`. Distinguishes "no limit" from failure. |
| `am_evaluate` | Exact form plus decimal, with optional substitutions. |
| `am_verify_equal` | Check your own algebra: did you change the meaning? |
| `am_truth_table` | Truth table plus satisfying assignments. |
| `am_solve_system` | Accepts `x + y = 3` or `x + y - 3`. |
| `am_to_sympy` | Runnable SymPy program, for cross-checking. |

Two resources are served: `angourimath://syntax` (including the parse traps below) and
`angourimath://reliability` (measured per-category pass rates from `work/coverage.md`).

Every tool is annotated `readOnlyHint` / `openWorldHint: false`, so clients can auto-approve.
A math tool that costs a permission click per call will not get used.

## The parts that matter

**Every response echoes the parse.** AngouriMath's parser is permissive in two ways that are
silent, and silence is the dangerous part — a valid parse of a *different* expression, with a
plausible answer:

- a trailing number is an **exponent**: `x2` is x², `2(g+e)3` is 2(g+e)³. A model naming a
  variable `x2` or `v1` gets it squared.
- an unknown identifier becomes **multiplication**: `pow(x,y)` lexes as `p*o*w(...)`,
  `arcsinh(x)` as the product `arcsinh * x`.

Both now raise a warning, and the `parsed` field always shows what was understood.

**Status is explicit.** `solved` / `unchanged` / `declined` / `suspect` / `timeout` / `failed`.
`declined` means AngouriMath left the expression unevaluated — it has no rule. That check runs
on the **raw** result before any simplification, because an unevaluated `limit(...)` simplifies
to `NaN`, which would otherwise be reported as a wrong answer instead of an honest decline.

**A NaN screen.** A printed `NaN` is almost never a legitimate answer; in `work/propcheck`
this one check caught two wrong integrals.

**Timeouts and stack-overflow isolation.** Each call runs under
`MathS.Multithreading.SetLocalCancellationToken` on a dedicated 64 MB-stack thread, abandoned
rather than killed on timeout. Cancellation cannot rescue a stack overflow — on upstream
master `∫ x*ln(x)` overflows inside `IntegrateByPartsPolynomial` and takes the process with
it — so the big stack is the second line of defence. Same approach as `work/casbench`.

## Two findings from building this

**`AreEqualNumerically` is exact, not tolerant.** Despite the name,
`MathS.UnsafeAndInternal.AreEqualNumerically` compares evaluated values with `!=` and no
tolerance. Any transcendental computed two mathematically equivalent ways disagrees in the
last digit, so the check reports equal expressions as different. `∫ x*ln(x)` returns a correct
antiderivative that differentiates back correctly and still failed this check. `Numeric.cs`
replaces it with a relative-tolerance comparator (1e-6) over positive real sample points —
positive because correct antiderivatives contain `ln`/`abs`, which are undefined or
non-holomorphic on the negatives.

**`provided` guards block numeric comparison.** AngouriMath tracks domains properly:
`(x^2-1)/(x-1)` simplifies to `x + 1 provided not x - 1 = 0` rather than an unconditional
`x + 1` that is wrong at x=1. That is a real strength, but a `Providedf` node does not compare
numerically against a bare expression, so guards are stripped for comparison only and
preserved in everything shown to the caller.

Both are why `am_integrate` reports `verified: true` for `∫ x*ln(x)` rather than a false alarm.

## Known limits

- **Requests are serialised, deliberately.** `MathS.Settings` stores values in a
  process-global `KeyStack` over a plain `List`, with no thread affinity — concurrent calls
  with different parse settings would interfere. One-at-a-time is the honest fix at this
  scale, and a stdio server sees one request at a time anyway.
- **The timeout path is not covered by the smoke suite.** Every case in the corpus completes
  in well under the budget on this branch, so the guard is exercised only by construction.
- **LaTeX is output only.** There is no LaTeX parser; convert `\frac{a}{b}` to `a/b` first.
- Nonlinear systems can return nothing even when a solution exists (upstream issue #629).
- `Simplify` on multivariate rational functions returns the input unreduced and silent —
  reported as `unchanged`, which means "no progress", not "already simplest".

## Why the local build

The project reference points at `../AngouriMath`, not the released NuGet package, and that is
load-bearing. On the corpus in `work/`, this branch scores **111/117 with 0 wrong answers and
0 hangs**; the released build scores 75/117 with 3 wrong answers and 3 hangs, `1e-20` parses
to `0`, and `FastExpression` was thread-unsafe until #637 (16 threads × 400k calls produced
one silently wrong number with no exception, and permanent corruption afterwards). A server
built on the published package would inherit all of it.
