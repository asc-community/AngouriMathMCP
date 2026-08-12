# What belongs in AngouriMath, not here

This server is an adapter. Anything that is a genuine computer-algebra feature belongs in
the library, where `AngouriMathCLI`, `AngouriMath.Terminal` and the Jupyter integration get
it too. This file records where the line was drawn, so the adapter does not quietly grow a
second, worse copy of the library.

## Deliberately NOT built here

**A LaTeX input parser.** AngouriMath emits LaTeX via `Latexize()` and cannot read it back.
That asymmetry is a library gap, and a parser is a grammar change — it belongs next to the
existing ANTLR grammar, not in a regex shim here. It is the most likely first-contact
failure for an LLM caller, since models emit LaTeX constantly, so it is worth requesting
upstream.

**A C / C99 code emitter.** `MathS.ToSympyCode` already establishes the pattern and the
place (`Functions/Output/`). A C emitter is the same category of feature and would serve
the embedded use case — derive a Jacobian symbolically, emit it as code. Writing it here
would duplicate a facility the library has a slot for.

## Built here as a workaround — should move upstream

**Eigenvalues.** `am_eigenvalues` computes `det(A - lambda*I)` and hands it to the solver.
That is a general `Matrix.Eigenvalues` feature, not an agent concern. GenericTensor has no
eigen support and, since there is no closed form beyond 4x4 (Abel-Ruffini), the
characteristic-polynomial route is the correct design for a symbolic library rather than a
compromise. It only lives here because the library does not offer it.

**Tolerance-based numeric equality.** `MathS.UnsafeAndInternal.AreEqualNumerically` compares
with `!=` and no tolerance, so any transcendental computed two mathematically equivalent
ways disagrees in the last digit. A correct antiderivative of `x*ln(x)` fails it.
`Numeric.cs` reimplements the comparison with a relative tolerance over positive sample
points; the library should offer this itself.

**Division-free determinant for symbolic entries.** `Entity.Matrix.Determinant` calls
`DeterminantGaussianSafeDivision`, which divides by pivots and leaves a `provided` guard per
pivot. Those guards are wrong as mathematics: `det([[a,b],[c,d]])` is `a*d - b*c` for every
`a`, and `[[0,J],[J,0]]` has eigenvalues `+/-J` including at `J = 0`. GenericTensor already
ships `DeterminantLaplace`, which is division-free and emits none of them. Selecting it when
the entries are non-numeric is a small upstream change; this server strips the guards and
reports them under `dropped_guards` in the meantime.

## Defects worth reporting upstream

Re-verified against AngouriMath 2.1.0 — a claim measured on an older build is not worth
reporting. `--selftest` re-checks each row on every run; three entries were dropped at the
2.0.0 upgrade because the release fixed them, which is the whole reason that check exists.

**Nothing was dropped at 2.1.0**, and that is a measurement rather than an assumption: the
build was made against the *published* 2.1.0 package rather than the sibling checkout, which
still sat at 2.0.0 while this was written. All eleven identities hold and all five rows below
still reproduce. 2.1.0 is a correctness release, and none of what it fixed is on this list.

| Observed | Note |
|---|---|
| `Simplify(sqrt(x^2))` is left as written, not reduced to `abs(x)` | No longer the soundness bug it was — 2.0.0 stopped answering `x`, which was wrong for every negative. What remains is a gap: writing `abs` needs to know the expression is real, which the codomain of [#719](https://github.com/asc-community/AngouriMath/issues/719) can now say and the simplifier does not yet read. |
| `MathS.Equations(...)` throws on an equality | It wants each equation in `= 0` form; passing an `Equalsf` raises `NotSufficientlySupportedException` rather than normalising `a = b` to `a - b`, which is a rewrite it could do itself. This server does it instead. Re-checked on 2.0.0 by `--selftest`; the exception type changed with the release, the behaviour did not. |
| `Integrate` declines `x^4*(1-x)^4/(1+x^2)` | It handles the same function once the polynomial division is done by hand, so the gap is dividing a rational function whose numerator outranks its denominator. |
| `Entity.DefiniteIntegral` is a first-order rule | New in 2.0.0, and it is a rectangle rule: the error halves per doubling of the step count, so 4000 steps buy about four digits of `∫[0,1] e^(x^2)` at ~150 ms. Simpson's rule is the same amount of code and would give roughly eight. It also samples both endpoints, so a convergent integral with a singular endpoint — `∫[0,1] sin(x)/x`, `∫[0,1] ln(x)` — returns `NaN` rather than a value. Both are worth raising; this server runs it twice and reports only the agreed digits in the meantime. |
| An unknown identifier still becomes implicit multiplication silently | 2.0.0 closed most of this: `exp`, `log10`, `log2`, `pow`, `floor`, `ceil`, `round`, `min`, `max` and `gcd` became real functions, and eleven names the library does not have are now refused by name. The general case remains — `im(z)` is `im * z` — and cannot be closed without refusing `a(b + c)`, so a warning or a strict default is still the only answer. This server warns. |

## Fixed upstream, kept here as a record

Each of these was on the list above and reproduced no longer at the 2.0.0 upgrade. Listed
so that nobody re-reports them, and so the cost of not re-measuring is visible.

- **`Factorize(x^2 - 1)` emitting `sqrt(1)`.** Dropped before 2.0.0: true of 1.4.0, false
  of the branch, and it went stale unnoticed. This is why `--selftest` exists.
- **`exp(x)` parsing as `exp * x`.** `exp` is the exponential as of 2.0.0.
- **`e^(pi*sqrt(163))` accurate to only ~23 significant digits.** Ramanujan's constant now
  evaluates to `262537412640768743.999999999999250072597...`, correct to 60 digits and on
  the right side of the integer, so the near-miss the number is famous for reproduces.
- **`Simplify` leaving a multivariate rational function uncancelled.**
  `(x^2+2xy+y^2)/(x^2-y^2)` now reduces to `(x + y)/(x - y) provided not x + y = 0`.
- **Limits needing factorial asymptotics.** `lim x→∞ (x!/x^x)^(1/x)` is `1/e`, by Stirling.

## Correctly belongs here

Adapter concerns, which a library should not carry:

- Parse echoing and the implicit-power / unknown-function warnings — presentation for a
  caller that cannot see the tree.
- The status taxonomy (`solved` / `unchanged` / `declined` / `suspect` / `timeout`) and
  decline detection before simplification.
- Per-call cancellation and the 64 MB-stack worker. The library correctly offers the
  cancellation token; deciding a budget and surviving a stack overflow is the host's job.
- The NaN screen, and verifying integrals by differentiating them back.
- `am_check_steps`, `am_domain_check`, `am_classify` — agent-facing framing, not algebra.
- The `angourimath://` resources.
