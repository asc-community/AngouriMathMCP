# What belongs in AngouriMath, not here

This server is an adapter. Anything that is a genuine computer-algebra feature belongs in
the library, where `AngouriMathCLI`, `AngouriMath.Terminal` and the Jupyter integration get
it too. This file records where the line was drawn, so the adapter does not quietly grow a
second, worse copy of the library.

## Deliberately NOT built here

**A LaTeX input parser.** AngouriMath emits LaTeX via `Latexise()` and cannot read it back.
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

| Observed | Note |
|---|---|
| `Simplify(sqrt(x^2))` returns `x`, not `abs(x)` | The library then contradicts its own evaluator, which gives `2` at `x = -2`. A soundness bug. |
| `e^(pi*sqrt(163))` accurate to only ~23 significant digits | Stable at the wrong value, so it reads as converged. Every component — `pi`, `sqrt(163)`, `pi*sqrt(163)`, `e^pi`, `e^(30*sqrt(2))` — is correct to 50 digits, and the literal-exponent form `e^40.109...` is correct. Only the composed form fails. Mechanism not isolated. |
| `Factorize(x^2 - 1)` returns `(x - sqrt(1)) * (x + sqrt(1))` | Correct, but `sqrt(1)` should reduce to `1`. Cosmetic. |
| `MathS.Equations(...)` throws `FutureReleaseException` on an equality | It wants each equation in `= 0` form; passing an `Equalsf` throws rather than normalising. This server rewrites `a = b` to `a - b`. |
| `Integrate` declines `x^4*(1-x)^4/(1+x^2)` | It handles the same function once the polynomial division is done by hand, so the gap is dividing a rational function whose numerator outranks its denominator. |
| Unknown identifiers become implicit multiplication silently | `exp(x)` parses as `exp * x`. A parse that succeeds and means something else is the worst failure class; a warning or strict default would help every consumer. |

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
