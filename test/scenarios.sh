#!/usr/bin/env bash
# Realistic use-cases, run end-to-end against the server.
#
# Each case is framed as the prompt an agent would actually receive, so the output shows
# what the tool contributes at the moment it matters — not a syntax demo. Unlike
# test/smoke.sh nothing here is asserted; read it.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BIN="$ROOT/src/AngouriMath.Mcp/bin/Release/net10.0/angourimath-mcp"

call() {
  printf '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"%s","arguments":%s}}\n' "$1" "$2"
}

{
  printf '{"jsonrpc":"2.0","id":0,"method":"initialize","params":{}}\n'

  # ---- checking work that has already been done ---------------------------------------
  call am_verify_equal  '{"left":"derivative(e^x*(x-1), x)","right":"x*e^x"}'
  call am_verify_equal  '{"left":"derivative(e^x*(x+1), x)","right":"x*e^x"}'
  call am_check_steps   '{"steps":["(x+1)^2 - 1","x^2 + 1 - 1","x^2"]}'

  # ---- reviewing code against its documentation ----------------------------------------
  call am_verify_equal  '{"left":"(a*(t - tref))^2 + b*(t - tref) + c","right":"a^2*t^2 - 2*a^2*t*tref + a^2*tref^2 + b*t - b*tref + c"}'
  call am_verify_equal  '{"left":"(a*(t - tref))^2 + b*(t - tref) + c","right":"a^2*t^2 - 2*a^2*t*tref + a^2*tref^2 + b*t + b*tref + c"}'
  call am_domain_check  '{"expression":"sqrt(v - vref) / (t - tref)"}'

  # ---- deriving something new -----------------------------------------------------------
  call am_differentiate '{"expression":"sqrt(x^2 + y^2)","variable":"x"}'
  call am_solve         '{"constraints":["f = 1/(2*pi*R*C)"],"variable":"R"}'
  call am_solve         '{"constraints":["v = k*d^2 + m*d","d > 0"],"variable":"d"}'
  call am_integrate     '{"expression":"x^6-4*x^5+5*x^4-4*x^2+4-4/(1+x^2)","variable":"x","from":"0","to":"1"}'

  # ---- approximating it, then putting it on a processor ----------------------------------
  call am_series        '{"expression":"sin(x)","variable":"x","degree":7}'
  call am_compare_numeric '{"reference":"sin(x)","approximation":"x - x^3/6 + x^5/120","variable":"x","from":0,"to":1.5,"samples":200}'
  call am_represent     '{"operation":"fixed_point","value":"1/6","fraction_bits":15,"total_bits":16}'
  call am_represent     '{"operation":"ieee754","value":"0.1"}'

  # ---- signals, and linear algebra --------------------------------------------------------
  call am_represent     '{"operation":"polar","value":"-1 - i"}'
  call am_eigenvalues   '{"matrix":[["0","J"],["J","0"]]}'
  call am_matrix        '{"operation":"tensor_product","matrix":[["1/sqrt(2)","1/sqrt(2)"],["1/sqrt(2)","-1/sqrt(2)"]],"matrix_b":[["1","0"],["0","1"]]}'

  # ---- everyday checks ---------------------------------------------------------------------
  call am_truth_table   '{"expression":"(ready and not fault) or override","variables":["ready","fault","override"]}'
  call am_evaluate      '{"expression":"sin(pi/3) + cos(pi/6)"}'

  # ---- the trap ---------------------------------------------------------------------------
  call am_parse         '{"expression":"a*(t - t0)^2 + c"}'

} | timeout 400 "$BIN" 2>/dev/null | python3 "$ROOT/test/scenarios.py"
