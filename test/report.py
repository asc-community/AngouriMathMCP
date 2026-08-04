"""Render the server's JSON-RPC replies as a readable pass/fail table."""
import json
import sys

LABELS = [
    ("initialize", None),
    ("parse 'x2 + 1'            (implicit-power trap)", "warn:implicit-power"),
    ("parse 'exp(x)'            (unknown-function trap)", "warn:unknown-function"),
    ("parse '2x + 1' strict     (should refuse)", "status:failed"),
    ("parse '1.5e3 + 2'         (exponent, not a power)", "nowarn:implicit-power"),
    ("parse 'α2 + β'            (Greek IS a power)", "warn:implicit-power"),
    ("parse 'e3'                (bare e IS a power)", "warn:implicit-power"),
    ("parse 'Γ(x+1)'            (Greek variable*bracket)", "warn:unknown-function"),
    ("simplify (x^2-1)/(x-1)    (domain condition kept)", "contains:provided"),
    ("simplify multivariate     (known: no progress)", None),
    ("solve x^2 = 4", "contains:-2"),
    ("solve x^2 = 4 and x > 0   (constraint narrowing)", "notcontains:-2"),
    ("differentiate sin*cos", None),
    ("integrate x*ln(x)         (overflows on upstream)", "verified:True"),
    ("integrate e^(x^2)         (no elementary form)", "status:declined"),
    ("limit sin(x)/x -> 0", "contains:1"),
    ("limit (1+x)^(1/x) -> 0    (wrong on upstream)", "contains:e"),
    ("evaluate sqrt(2)+sqrt(8)", None),
    ("verify sin^2+cos^2 == 1", "equal:True"),
    ("verify (x+1)^2 == x^2+1", "equal:False"),
    ("truth table a and (b or not c)", None),
    ("solve system x+y=3, x-y=1", "status:solved"),
    ("to sympy sin(x)/x", "contains:sympy"),
    ("det [[a,b],[c,d]]         (no bogus guard)", "notcontains:provided"),
    ("H (x) I                   (tensor product)", "status:solved"),
    ("eig [[0,J],[J,0]]         (valid at J=0)", "notcontains:provided"),
    ("definite integral         (22/7 > pi proof)", "contains:22/7"),
    ("Maclaurin sin(x) deg 7", "contains:120"),
    ("factorize 5040", "contains:2^4"),
    ("substitute x := t - t_0     (structural)", "contains:t - t_0"),
    ("compare sin(x) vs 2-term Taylor", "status:solved"),
    ("Q15 of 1/sqrt(2)            (exact repr)", "contains:11585/16384"),
    ("IEEE 754 of 0.1            (exact bits)", "contains:0.100000000000000005"),
    ("polar of -1-i        (quadrant-correct)", "contains:-3/4 * pi"),
    ("resources/list", None),
    ("prompts/list", None),
]

replies = []
for line in sys.stdin:
    line = line.strip()
    if line:
        replies.append(json.loads(line))

failures = 0
for i, reply in enumerate(replies):
    label, check = LABELS[i] if i < len(LABELS) else (f"reply {i}", None)
    result = reply.get("result", {})

    if "content" in result:
        payload = json.loads(result["content"][0]["text"])
    else:
        payload = result

    status = payload.get("status", "-")
    shown = (
        # definite_value first: an integral with limits also carries the antiderivative in
        # `result`, and the definite value is what the case is actually asserting.
        payload.get("definite_value")
        or payload.get("represented_value")
        or payload.get("exact_value")
        or payload.get("phase_radians")
        or payload.get("result")
        or payload.get("eigenvalues")
        or payload.get("solutions")
        or payload.get("sympy", "")[:40].replace("\n", " ")
        or payload.get("truth_table", "").replace("\n", " ")[:40]
        or ("equal=" + str(payload.get("equal")) if "equal" in payload else "")
        or payload.get("error")
        or ("%d resources" % len(payload["resources"]) if "resources" in payload else "")
        or ("%d prompts" % len(payload["prompts"]) if "prompts" in payload else "ok")
    )

    verdict = "    "
    if check:
        kind, _, want = check.partition(":")
        blob = json.dumps(payload)
        ok = {
            "warn": any(want in w for w in payload.get("warnings", [])),
            "nowarn": not any(want in w for w in payload.get("warnings", [])),
            "status": status == want,
            "contains": want in str(shown),
            "notcontains": want not in str(shown),
            "verified": str(payload.get("verified")) == want,
            "equal": str(payload.get("equal")) == want,
        }[kind]
        verdict = " ok " if ok else "FAIL"
        if not ok:
            failures += 1

    extra = ""
    if payload.get("verified") is not None:
        extra = "  verified=%s" % payload["verified"]
    if payload.get("warnings"):
        extra += "  [%d warning(s)]" % len(payload["warnings"])

    print("[%s] %-42s %-10s %s%s" % (verdict, label, status, str(shown)[:52], extra))

print()
print("FAILURES: %d" % failures if failures else "all checks passed")
sys.exit(1 if failures else 0)
