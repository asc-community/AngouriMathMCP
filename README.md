# AngouriMathMCP

An MCP server exposing [AngouriMath](https://github.com/asc-community/AngouriMath) — exact
symbolic algebra — to an LLM agent. Prototype.

> Several claims below cite a companion triage workspace (`work/casbench`, `work/coverage.md`,
> `work/TRIAGE.md`) where AngouriMath was measured against a 117-problem corpus. That
> workspace is **not part of this repository**; the references are there so the numbers are
> attributable rather than asserted.

The thesis is not "give the model a calculator". Models are confident they can do algebra,
so a tool that merely offers to do it for them goes unused. The thesis is **verification**:
every integral is checked by differentiating it back, every decline is reported as a decline
rather than dressed up as an answer, and every response echoes what the server actually
parsed. The model has no ego about being checked.

## Build and run

Needs the **.NET 10 SDK**. Nothing else — no external NuGet dependencies beyond
AngouriMath itself.

```sh
dotnet build -c Release src/AngouriMath.Mcp
./test/smoke.sh                      # end-to-end over real stdio JSON-RPC
./test/scenarios.sh                  # the use-cases below, run for real
```

**Which AngouriMath it builds against matters.** If a sibling checkout exists at
`../AngouriMath` relative to this repo, it is used automatically. Otherwise the build falls
back to the released **1.4.0** package and prints a warning — the server runs, but that
release scores 75/117 on the corpus with 3 wrong answers and 3 hangs, so parts of the
`angourimath://reliability` resource and several tests in `test/smoke.sh` will not hold.
See *Why the local build* at the end.

## Installing it in an agent

The build produces a single self-contained stdio executable at:

```
src/AngouriMath.Mcp/bin/Release/net10.0/angourimath-mcp
```

It speaks newline-delimited JSON-RPC 2.0 on stdin/stdout, protocol revision `2024-11-05`.
No network, no filesystem access, no configuration, no secrets — every tool is annotated
`readOnlyHint` and `openWorldHint: false`, so clients can auto-approve calls. That matters
in practice: a math tool that costs a permission click per call does not get used.

**Claude Code**

```sh
claude mcp add angourimath --scope user -- "$PWD/src/AngouriMath.Mcp/bin/Release/net10.0/angourimath-mcp"
claude mcp list          # expect: angourimath ... ✔ Connected
```

`--scope user` makes it available in every project; drop it to register for the current
project only. Remove with `claude mcp remove angourimath --scope user`.

**Claude Desktop** — add to `claude_desktop_config.json` (macOS:
`~/Library/Application Support/Claude/`, Windows: `%APPDATA%\Claude\`):

```json
{
  "mcpServers": {
    "angourimath": {
      "command": "/abs/path/to/AngouriMathMCP/src/AngouriMath.Mcp/bin/Release/net10.0/angourimath-mcp"
    }
  }
}
```

**Any other MCP client** (Cursor, Zed, Continue, VS Code agents, custom hosts) takes the
same shape — a `command` pointing at the executable, with no `args` or `env`. Use an
absolute path: stdio servers are launched from an unspecified working directory.

**Verify without a client**, which is often the fastest way to tell whether a problem is
yours or the host's:

```sh
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' \
  | src/AngouriMath.Mcp/bin/Release/net10.0/angourimath-mcp
```

Fifteen tools should come back. If a host reports the server as failed, run that first —
anything written to stdout other than protocol traffic corrupts the stream.

**Getting the model to actually use it.** Point it at `angourimath://reliability` and
`angourimath://syntax` once at the start of a session. Models are confident they can do
algebra unaided and will not reach for a calculator; they will, however, happily let their
own work be checked. Framing the server as a verifier rather than a replacement is what
makes it get called.

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
| `am_check_steps` | Checks a chain of working and says *which* step broke. |
| `am_domain_check` | Domain guards, structural hazards, and where it stops being real. |
| `am_base_convert` | Between bases 2–36. |
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

## Use cases

`./test/scenarios.sh` runs these for real. The ones that earn their keep:

**Checking the model's own algebra.** `∫ x·e^x dx = e^x(x−1)` — confirmed exactly. Change it
to `e^x(x+1)` and the answer comes back with `difference: 2 * e^x`, which names the error
rather than just rejecting the claim. This is the case models will actually accept a tool for,
because being checked costs them nothing.

**Firmware code review.** A calibration polynomial expanded by hand, versus the derivation in
the comment above it: `(a(t−t_ref))² + b(t−t_ref) + c` against the expanded form in the code.
Equal, exactly. Flip one sign — `+b·t_ref` instead of `−b·t_ref` — and the difference comes
back as `-2·b·t_ref`, pointing straight at the term. No reviewer catches that by eye.

**Jacobians for sensor fusion.** `∂/∂x √(x²+y²)` and `∂/∂y`, for an EKF measurement row.
Hand-derived Jacobians are where silent errors live for months.

**Solving design formulas.** `f = 1/(2πRC)` for `R` → `1/2 / (C·f·pi)`. Inverting a
calibration curve `v = k·d² + m·d` for `d` gives both quadratic branches.

**Branch logic.** `(ready and not fault) or override` — all five satisfying assignments
enumerated, which is how you find the case you didn't think about.

**Exact test oracles.** `sin(π/3) + cos(π/6)` → `sqrt(3)`, not a float the model guessed.

## Step-by-step, and knowing what to distrust

**There is no step engine in AngouriMath** — no derivation output anywhere in the library. So
`am_check_steps` inverts the problem: you write the steps, it checks each transition and names
the one that broke. Feed it `['(x+1)^2 - 1', 'x^2 + 1 - 1', 'x^2']` and it reports step 1 as
invalid with `difference: 2 * x` — the dropped cross term, located precisely. A model is good
at proposing a derivation and unreliable at executing one; this puts each side on the job it
can actually do.

**`am_domain_check`** answers "what should I watch out for here?". For `sqrt(x-2)/(x-5)` it
reports the division hazard, the principal-branch hazard, and the five sampled points where
the expression is not real. For `ln(x) + ln(x+1)` it shows the simplification to `ln(x*(1+x))`
— which is real at x = −2.7 while the original is not. That is the documented domain-widening
that produces extraneous roots, made visible.

**Correct but meaningless** is a constraint problem, not a math problem. The library cannot
know that a length must be positive, but you can say so: `am_solve` takes a list of
constraints, so `['v = k*d^2 + m*d', 'd > 0']` returns only the physical branch. Encode the
physics as mathematics and the solver enforces it.

## A soundness bug this found

`Simplify(sqrt(x^2))` returns **`x`**. It should be `abs(x)`. The library then contradicts
itself: evaluating `sqrt(x^2)` at `x = -2` correctly gives `2`, while the simplified form
gives `-2`. This is the same class of error that `work/comparison.md` credits AngouriMath for
*avoiding* relative to Math.NET.

Caveat on that claim: this is a work-in-progress branch with fixes in flight, so treat it as
an observation on the current build rather than a verdict on the project. It is not in
`work/TRIAGE.md` as of this commit, which is why it is written down here.

It also exposed a weakness in this server: `am_verify_equal`'s exact path trusts `Simplify`,
so it initially reported `sqrt(x^2) = x` as **equal**. It now cross-checks the original two
sides numerically across the real line whenever the exact path claims equality, and reports
`status: conflict` when they disagree — because a direct evaluation never passes through a
rewrite, so it is the better evidence. `sqrt(x^2)` vs `abs(x)` still returns equal, so the
check discriminates rather than just objecting.

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
- In `am_solve`, `solutions[]` is tidied per root but the raw `result` string is not, so the
  two can disagree cosmetically (`1/2 / (C*f*pi)` vs `--1/2 * 1/pi * 1/C/f`). Prefer
  `solutions[]`.
- `Simplify` on multivariate rational functions returns the input unreduced and silent —
  reported as `unchanged`, which means "no progress", not "already simplest".

## Why the local build

The project reference points at `../AngouriMath`, not the released NuGet package, and that is
load-bearing. On the corpus in `work/`, this branch scores **111/117 with 0 wrong answers and
0 hangs**; the released build scores 75/117 with 3 wrong answers and 3 hangs, `1e-20` parses
to `0`, and `FastExpression` was thread-unsafe until #637 (16 threads × 400k calls produced
one silently wrong number with no exception, and permanent corruption afterwards). A server
built on the published package would inherit all of it.

## Naming

The repo is `AngouriMathMCP`, matching `AngouriMath` and `AngouriMathCLI`. The lowercase
convention seen on most published MCP servers comes from npm — which forbids uppercase in
package names — and PyPI, which normalises to lowercase; it is a packaging constraint, not
an MCP one, and does not apply to a .NET repo. The executable stays lowercase
(`angourimath-mcp`), since that is what goes in a client config and what people type.

## License

MIT — see [LICENSE](LICENSE). AngouriMath itself is MIT; note that the separate
`AngouriMathCLI` project is GPL-3.0 and is **not** used here.
