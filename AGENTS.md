# Working on this repo

An MCP server exposing AngouriMath to LLM agents. The design thesis is **verification, not
calculation**: models are confident they can do algebra, so a tool that merely offers to do
it for them goes unused. Everything here is shaped by that — integrals are checked by
differentiating them back, declines are reported as declines, and every response echoes what
was actually parsed.

Read `README.md` for what it does and `UPSTREAM.md` for what deliberately isn't here.

## Build and test

```sh
dotnet build -c Release src/AngouriMath.Mcp
./test/smoke.sh          # 37 cases over real stdio JSON-RPC, 30 with assertions
./test/scenarios.sh      # 20 realistic use-cases; read the output, it is not asserted
src/AngouriMath.Mcp/bin/Release/net10.0/angourimath-mcp --selftest
```

`--selftest` is the quickest signal: eleven identities that must hold, plus a re-check of
every documented defect.

## Which AngouriMath you built against

The build uses a **sibling AngouriMath checkout at `../AngouriMath` when one exists, and the
released 2.1.0 NuGet package when it does not**. It prints which; read the line.

This used to be the first trap in the repo — the fallback was 1.4.0, which behaves
differently enough that two assertions in `test/smoke.sh` failed against it legitimately
(`integrate x*ln(x)` overflowed the stack, `limit (1+x)^(1/x) -> 0` answered `1` instead of
`e`), and CI was build-only because of it. 2.0.0 is the branch those tests were written
against, so the two builds now agree, the suite runs in CI, and a red result means a defect
here.

**The two diverge whenever the sibling checkout is ahead of the package, and it usually is.**
At the 2.1.0 bump the sibling here still sat at 2.0.0 while the package had moved on, which is
the reverse of the usual direction and just as misleading — the selftest for that upgrade was
therefore run with `-p:UseLocalAngouriMath=false`, against the published package, so that what
it measured was what a consumer gets.

When a test starts failing, check which build you have before concluding anything — and **do not
weaken an assertion to make it pass**. Several assertions in that file exist because the answers
they pin were once wrong.

Upgrading the library is not a version bump. `Latexise` became `Latexize` and the target
frameworks moved off `net7.0` (a `ProjectReference` pinning it stops resolving) — but the
part that needs judgement is `Parsing.KnownFunctions`. Ten names became real functions in
2.0.0 and every one of them would have made the unknown-function warning fire on correct
input. **Re-probe the grammar after an upgrade**, by parsing `name(x)` for each entry and
reading what came back; the release notes describe that surface in two passes and taking the
first one is wrong.

## Invariants

Each of these was learned by breaking it. They are not stylistic.

**Simplify before stripping `provided` guards, never after.** The guard is what licenses the
cancellation. Strip `provided not a = 0` from the raw Gaussian determinant first and Simplify
can no longer reduce `a*(d*a - c*b)/a`, because `a` might be zero. See `Matrices.Clean`.

**Run decline detection on the raw result, before any Simplify.** An unevaluated `limit(...)`
simplifies to `NaN`. Simplify first and an honest "I have no rule for this" is reported as a
wrong answer. See `Guard.IsDeclined`.

**Nothing but JSON-RPC goes to stdout.** Diagnostics go to stderr. One stray `Console.Write`
corrupts the stream and the host reports the server as failed.

**Requests stay serialized — but no longer because they must.** `MathS.Settings` kept its
values in `[ThreadStatic]` fields until AngouriMath 2.0.0, so two concurrent calls with
different parse settings interfered and parallelising was a correctness bug. 2.0.0 moved
them to an `AsyncLocal`: a scope follows the call, including onto the worker thread `Guard`
starts, and a sibling call cannot see it. That was verified here, not taken from the release
notes. So concurrency is unblocked — and it is still not done, because a stdio server sees
one request at a time and the change deserves its own measurement rather than arriving as a
side effect of a library upgrade. If you do it, that is the invariant to re-verify first.

**Every AngouriMath call goes through `Guard.Run`.** Not defensive habit: some inputs
overflow the stack inside the library and take the process down. `Guard` runs work on a
dedicated 64 MB-stack thread and abandons it on timeout. This has been observed containing a
real overflow, not just theorised.

**A warning that fires on correct input is worse than no warning.** Both `pow(` and `t_0`
started as false positives and had to be narrowed. Before adding a heuristic warning, check
it against the *correct* spelling as well as the broken one — false positives teach callers
to ignore the channel.

**Re-verify defect claims; they drift.** `UPSTREAM.md` and the `angourimath://reliability`
resource assert specific library misbehaviour. One entry (`Factorize(x^2-1)` emitting
`sqrt(1)`) was true of the release and false of the branch, and went stale unnoticed.
`--selftest` now checks these automatically and reports drift — run it after touching
anything in that area.

The 2.0.0 upgrade is what that check is for: it caught `exp(x)` and four further claims that
had become false, including a "known to be WRONG" figure for Ramanujan's constant that the
release had fixed to 60 correct digits. A stale defect claim is worse than none — it tells a
caller to distrust a correct answer, and it wastes whoever re-reports it upstream.

## Scope

This is an adapter, not a second computer-algebra system. Anything that is a genuine CAS
feature belongs in AngouriMath, where `AngouriMathCLI`, `AngouriMath.Terminal` and the
Jupyter integration also get it. `UPSTREAM.md` records the line and why each item sits on the
side it does — a LaTeX parser and a C emitter were deliberately declined.

If you are about to implement mathematics here rather than presentation, stop and ask whether
it belongs upstream.

## Adding tools

There are 23. Each one costs context and dilutes the descriptions the model routes on, so a
new tool needs to earn its place against that. Prefer:

1. a parameter on an existing tool,
2. an MCP **prompt** (they cost nothing in tool-list context),
3. a new tool, last.

The 2.0.0 upgrade is a worked example. Three new library features reached callers, and none
of them became a tool:

- **`mod`** needed no code at all. The parser gained the keyword, so `17 mod 5` works through
  every tool that parses an expression. Note it is *floored*: `-7 mod 3` is `2`. Same for
  `floor`, `ceil`, `round`, `min`, `max` and `gcd` — `am_solve` will now do
  `floor(x) - 3 = 0`, and that reach came from correcting the parse whitelist.
- **Numeric definite integration** (`Entity.DefiniteIntegral`) became a field on an existing
  response, `numeric_definite_value` on `am_integrate`.
- **Matrix range slicing** (`a[1.., ..]`) was **declined**. It is ergonomics for a C# caller;
  here the matrix arrives as JSON rows from the caller, who can already slice it locally, so
  the operation would cost description context and buy nothing.

Tool descriptions are routing prompts, not documentation. Say *when* to call it, and where a
model would wrongly trust itself, say so explicitly — that is what `CallEvenIfConfident` is
for.

Every response carries a `status` (`solved` / `unchanged` / `declined` / `suspect` /
`timeout` / `failed` / `conflict`) and echoes `parsed`. Keep both.

## Conventions

- C#, nullable enabled, no external NuGet dependencies beyond AngouriMath itself. Keep it
  that way — `System.Text.Json` is enough for the protocol.
- Comments explain *why*, especially where the code looks wrong but isn't. The ordering
  constraints above are the reason several functions are shaped as they are.
- Tests are shell + Python driving the real binary over stdio. There is no unit-test project,
  deliberately: the thing being tested is protocol behaviour end to end.

## Before claiming done

Run `./test/smoke.sh` **and** `--selftest`, and say which AngouriMath you built against. The
two agree at 2.0.0 and both suites were confirmed green against each; they will not agree
once anything lands upstream, so name the build rather than assuming it does not matter.
