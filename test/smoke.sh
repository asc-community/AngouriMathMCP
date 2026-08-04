#!/usr/bin/env bash
# End-to-end smoke test: drives the server over real stdio JSON-RPC and checks the answers.
#
# Cases are chosen to cover the behaviours that motivated the design, not just the happy
# path — the two silent-misparse traps, a decline, a verified integral, and the integral
# that stack-overflows on upstream master.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BIN="$ROOT/src/AngouriMath.Mcp/bin/Release/net10.0/angourimath-mcp"

if [[ ! -x "$BIN" ]]; then
  echo "building..."
  dotnet build -c Release -v q --nologo "$ROOT/src/AngouriMath.Mcp" >/dev/null || exit 1
fi

call() { # name, json-args
  printf '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"%s","arguments":%s}}\n' "$1" "$2"
}

{
  printf '{"jsonrpc":"2.0","id":0,"method":"initialize","params":{}}\n'
  call am_parse         '{"expression":"x2 + 1"}'
  call am_parse         '{"expression":"pow(x,y)"}'
  call am_parse         '{"expression":"2x + 1","strict":true}'
  call am_simplify      '{"expression":"(x^2-1)/(x-1)"}'
  call am_simplify      '{"expression":"(x^2+2*x*y+y^2)/(x^2-y^2)"}'
  call am_solve         '{"constraints":["x^2 = 4"],"variable":"x"}'
  call am_solve         '{"constraints":["x^2 = 4","x > 0"],"variable":"x"}'
  call am_differentiate '{"expression":"sin(x)*cos(x)","variable":"x"}'
  call am_integrate     '{"expression":"x*ln(x)","variable":"x"}'
  call am_integrate     '{"expression":"e^(x^2)","variable":"x"}'
  call am_limit         '{"expression":"sin(x)/x","variable":"x","to":"0"}'
  call am_limit         '{"expression":"(1+x)^(1/x)","variable":"x","to":"0"}'
  call am_evaluate      '{"expression":"sqrt(2)+sqrt(8)"}'
  call am_verify_equal  '{"left":"sin(x)^2+cos(x)^2","right":"1"}'
  call am_verify_equal  '{"left":"(x+1)^2","right":"x^2+1"}'
  call am_truth_table   '{"expression":"a and (b or not c)"}'
  call am_solve_system  '{"equations":["x + y = 3","x - y = 1"],"variables":["x","y"]}'
  call am_to_sympy      '{"expression":"sin(x)/x"}'
  printf '{"jsonrpc":"2.0","id":99,"method":"resources/list","params":{}}\n'
} | timeout 300 "$BIN" 2>/dev/null | python3 "$ROOT/test/report.py"
