"""Render the scenario run as a readable narrative. Nothing here is asserted — read it."""
import json
import sys

CASES = [
    None,
    ("Verify my own integration",
     "I derived  ∫ x·e^x dx = e^x(x-1).  Is that right?"),
    ("...with a sign slip",
     "Same, but written e^x(x+1) — the mistake a human actually makes."),
    ("Which step of my working broke?",
     "(x+1)² − 1  →  x² + 1 − 1  →  x²   — I expanded it in my head."),
    ("Code review: expanded polynomial",
     "Comment says (a(t−t_ref))² + b(t−t_ref) + c; the code has it expanded by hand."),
    ("...same review, sign error in the code",
     "+b·t_ref instead of −b·t_ref. Would a reviewer catch that by eye?"),
    ("What can go wrong before hardware?",
     "√(v − v_ref)/(t − t_ref) — where does this stop being a real number?"),
    ("EKF measurement Jacobian",
     "Range r = √(x²+y²). I need ∂r/∂x for the filter."),
    ("Solve a design formula for a part",
     "f = 1/(2πRC). I know f and C, I need R."),
    ("Invert a curve, physical branch only",
     "Reading v = k·d² + m·d, and d must be positive."),
    ("Exact area, antiderivative verified",
     "∫₀¹ of a rational function — and is the antiderivative even right?"),
    ("How many terms do I need?",
     "sin(x) as a series — no FPU, and a cycle budget."),
    ("...and what does truncating cost?",
     "Three terms of sin(x) across 0 to 1.5 rad. Worst error, and where?"),
    ("Coefficient as a fixed-point word",
     "1/6 into Q15. What does the processor hold, and what did I lose?"),
    ("Why 0.1 is not 0.1",
     "The bits of 0.1 as a double, and the exact value stored."),
    ("Phasor form, kept exact",
     "−1−i in polar form. Quadrant matters."),
    ("Two-state Hamiltonian",
     "[[0,J],[J,0]] — energy levels in terms of J, not numbers."),
    ("Two-qubit operator",
     "H ⊗ I, as exact amplitudes rather than 0.7071."),
    ("When does this guard actually fire?",
     "(ready and not fault) or override — enumerate every case I have to handle."),
    ("Exact value for a unit test",
     "sin(π/3) + cos(π/6) — the exact form, not a float I have to trust."),
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
        if payload.get("difference") and not payload["equal"]:
            print("        difference: %s" % payload["difference"])

    elif "steps" in payload:
        bad = payload.get("first_invalid_step")
        print("  ->    %s" % ("all steps sound" if bad is None else "step %d broke it" % bad))
        for s in payload["steps"]:
            print("        %s %-22s -> %-18s %s"
                  % ("ok " if s["equal"] else "BAD", s["from"][:22], s["to"][:18],
                     s.get("difference") or ""))

    elif "truth_table" in payload:
        print("  ->    satisfiable=%s; assignments (%s):"
              % (payload.get("satisfiable"), ", ".join(payload.get("variable_order", []))))
        for row in (payload.get("satisfying_assignments") or "").split("\n")[1:]:
            if row.strip():
                print("          %s" % row.strip())

    elif "hazards" in payload:
        print("  ->    simplified: %s" % payload["simplified"])
        for h in payload["hazards"][:3]:
            print("        hazard: %-20s %s" % (h["in"][:20], h["risk"][:50]))
        if payload.get("not_real_at"):
            print("        not real at: %s" % payload["not_real_at"][:3])

    elif "max_absolute_error" in payload:
        print("  ->    worst error %.3g at %s   (rms %.3g over %d samples)"
              % (payload["max_absolute_error"], payload["max_absolute_error_at"],
                 payload["rms_error"], payload["samples_used"]))

    elif payload.get("operation") == "fixed_point":
        print("  ->    raw %s   holds exactly %s   error %s%s"
              % (payload["raw"], payload["represented_value"],
                 str(payload["absolute_error"])[:10],
                 "   SATURATED" if payload["saturated"] else ""))

    elif payload.get("operation") == "ieee754":
        print("  ->    %s   exponent %s" % (payload["hex"], payload["exponent_unbiased"]))
        print("        stores exactly: %s" % payload["exact_value"][:46])

    elif payload.get("operation") == "polar":
        print("  ->    |z| = %s    arg = %s   (%s degrees)"
              % (payload["magnitude"], payload["phase_radians"], payload["phase_degrees"]))

    elif "eigenvalue_list" in payload:
        print("  ->    eigenvalues %s   from %s"
              % (payload["eigenvalue_list"], payload["characteristic_polynomial"]))

    elif "definite_value" in payload:
        print("  ->    %s   (antiderivative verified: %s)"
              % (payload["definite_value"], payload.get("verified")))

    else:
        shown = payload.get("result") or payload.get("error")
        if isinstance(shown, list):
            shown = json.dumps(shown)
        print("  ->    %s" % str(shown)[:66])
        if payload.get("approximate"):
            print("        approx: %s" % str(payload["approximate"])[:24])
        if payload.get("solutions"):
            print("        solutions: %s" % payload["solutions"])

    if status not in ("solved", None):
        print("        status: %s" % status)
    for w in payload.get("warnings", []):
        print("        WARNING: %s" % w[:68])
    print()
