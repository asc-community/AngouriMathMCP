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
./test/smoke.sh          # 36 cases over real stdio JSON-RPC, 29 with assertions
./test/scenarios.sh      # 20 realistic use-cases; read the output, it is not asserted
src/AngouriMath.Mcp/bin/Release/net10.0/angourimath-mcp --selftest
```

`--selftest` is the quickest signal: eleven identities that must hold, plus a re-check of
every documented defect.

## The trap that will catch you first

The build uses a **sibling AngouriMath checkout at `../AngouriMath` when one exists, and
silently falls back to the released 1.4.0 NuGet package when it does not** (it prints a
warning; read it). Those two builds behave differently, and `test/smoke.sh` asserts the
behaviour of the development branch.

Against the release, two assertions fail *legitimately*:

- `integrate x*ln(x)` times out — it overflows the stack there
- `limit (1+x)^(1/x) -> 0` answers `1` instead of `e`

**Do not "fix" those by weakening the assertions.** They are real coverage. Check which
AngouriMath you built against before concluding anything about a test failure. This is also
why CI is build-only.

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

**Requests stay serialized.** `MathS.Settings` stores values in a process-global `KeyStack`
with no thread affinity, so two concurrent calls with different parse settings interfere.
Parallelising the request loop is a correctness bug, not an optimisation.

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

Run `./test/smoke.sh` **and** `--selftest`, and say which AngouriMath you built against. A
green suite against the NuGet fallback is not the same claim as a green suite against the
development branch.
