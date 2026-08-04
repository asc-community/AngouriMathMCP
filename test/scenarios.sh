#!/usr/bin/env bash
# Realistic use-cases, run end-to-end against the server.
#
# Each case is framed as the prompt an agent would actually receive, so the output shows
# what the tool contributes at the moment it matters — not a syntax demo.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BIN="$ROOT/src/AngouriMath.Mcp/bin/Release/net10.0/angourimath-mcp"

call() {
  printf '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"%s","arguments":%s}}\n' "$1" "$2"
}

{
  printf '{"jsonrpc":"2.0","id":0,"method":"initialize","params":{}}\n'

  # 1. "I worked out that the integral of x*e^x is e^x*(x-1). Check me."
  call am_verify_equal  '{"left":"derivative(e^x*(x-1), x)","right":"x*e^x"}'

  # 2. "Same thing, but I dropped the minus sign."
  call am_verify_equal  '{"left":"derivative(e^x*(x+1), x)","right":"x*e^x"}'

  # 3. EKF measurement Jacobian: range from a 2D position.
  call am_differentiate '{"expression":"sqrt(x^2 + y^2)","variable":"x"}'
  call am_differentiate '{"expression":"sqrt(x^2 + y^2)","variable":"y"}'

  # 4. A calibration polynomial expanded by hand in firmware, against its documented
  #    derivation. This is the code-review use case. Note the reference temperature is
  #    called `tref`, not `t0` — see case 12 for why that matters.
  call am_verify_equal  '{"left":"(a*(t - tref))^2 + b*(t - tref) + c","right":"a^2*t^2 - 2*a^2*t*tref + a^2*tref^2 + b*t - b*tref + c"}'

  # 5. The same review, with the sign error a human would actually make.
  call am_verify_equal  '{"left":"(a*(t - tref))^2 + b*(t - tref) + c","right":"a^2*t^2 - 2*a^2*t*tref + a^2*tref^2 + b*t + b*tref + c"}'

  # 6. "Solve the RC cutoff formula for R."
  call am_solve         '{"constraints":["f = 1/(2*pi*R*C)"],"variable":"R"}'

  # 7. Invert a calibration curve: given the reading, recover the input.
  call am_solve         '{"constraints":["v = k*d^2 + m*d"],"variable":"d"}'

  # 8. Firmware branch logic: when does this guard actually fire?
  call am_truth_table   '{"expression":"(ready and not fault) or override","variables":["ready","fault","override"]}'

  # 9. Exact expected value for a unit test, rather than a float the model guessed.
  call am_evaluate      '{"expression":"sin(pi/3) + cos(pi/6)"}'

  # 10. A small-signal linearisation: first-order Taylor term via the derivative at a point.
  call am_evaluate      '{"expression":"derivative(sqrt(1 + x), x)","substitutions":{"x":"0"}}'

  # 11. The trap, in the shape it actually appears: a reference temperature named `t0`.
  #     The parse silently becomes t^0 = 1. The warning is the only thing standing between
  #     the caller and a confident wrong answer.
  call am_parse         '{"expression":"a*(t - t0)^2 + c"}'

} | timeout 300 "$BIN" 2>/dev/null | python3 "$ROOT/test/scenarios.py"
