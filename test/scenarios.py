"""Render the scenario run as a readable narrative."""
import json
import sys

CASES = [
    None,
    ("Verify my own integration",
     "I derived  ∫ x·e^x dx = e^x(x-1).  Is that right?"),
    ("...with a sign slip",
     "Same, but written e^x(x+1) — the mistake a human actually makes."),
    ("EKF measurement Jacobian  ∂/∂x",
     "Range r = √(x²+y²) from a 2D position. I need the Jacobian row."),
    ("EKF measurement Jacobian  ∂/∂y", ""),
    ("Firmware code review: expanded polynomial",
     "The comment says (a(t-t₀))² + b(t-t₀) + c; the code has it expanded by hand."),
    ("...same review, sign error in the code",
     "+b·t₀ instead of −b·t₀. Would a reviewer catch this by eye?"),
    ("Solve a design formula for a component",
     "f = 1/(2πRC). I know f and C, I need R."),
    ("Invert a calibration curve",
     "Reading v = k·d² + m·d. Given v, recover d."),
    ("Firmware branch logic",
     "(ready and not fault) or override — when does this guard fire?"),
    ("Exact value for a unit test",
     "sin(π/3) + cos(π/6) — I want the exact form, not a float."),
    ("Small-signal linearisation",
     "d/dx √(1+x) at x=0, for a first-order approximation."),
    ("The parse trap, caught",
     "a·(t − t0)² + c  — a reference temperature named t0, as anyone would write it."),
]

replies = [json.loads(l) for l in sys.stdin if l.strip()]

for i, reply in enumerate(replies):
    if i == 0 or i >= len(CASES):
        continue
    title, prompt = CASES[i]
    payload = json.loads(reply["result"]["content"][0]["text"])

    print("─" * 78)
    print("USE CASE %d  —  %s" % (i, title))
    if prompt:
        print("  ask:  %s" % prompt)

    status = payload.get("status")
    if "equal" in payload:
        verdict = "EQUAL ✓" if payload["equal"] else "NOT EQUAL ✗"
        print("  ->    %s   (%s)" % (verdict, payload.get("method", "")))
        if not payload["equal"]:
            print("        difference: %s" % payload.get("difference"))
    elif "truth_table" in payload:
        print("  ->    satisfiable=%s" % payload.get("satisfiable"))
        print("        satisfying assignments (%s):"
              % ", ".join(payload.get("variable_order", [])))
        for row in (payload.get("satisfying_assignments") or "").split("\n")[1:]:
            if row.strip():
                print("          %s" % row.strip())
    else:
        print("  ->    %s" % payload.get("result", payload.get("error")))
        if payload.get("approximate"):
            print("        approx: %s" % payload["approximate"][:24])
        if payload.get("solutions"):
            print("        solutions: %s" % payload["solutions"])

    if status not in ("solved", None):
        print("        status: %s" % status)
    for w in payload.get("warnings", []):
        print("        WARNING: %s" % w[:70])
    print()
