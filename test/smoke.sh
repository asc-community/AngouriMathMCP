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
  call am_parse         '{"expression":"exp(x)"}'
  call am_parse         '{"expression":"2x + 1","strict":true}'

  # The implicit-power check must read the grammar rather than ASCII. `1.5e3` is a single
  # NUMBER token — EXPONENT is a fragment of NUMBER — so the digits after `e` are not a
  # trailing power. `α2` is one, because VARIABLE accepts Greek and Cyrillic. And a bare
  # `e3` still is, because there is no numeric literal for that `e` to belong to; it is the
  # case that stops the exponent exclusion from being written too broadly.
  call am_parse         '{"expression":"1.5e3 + 2"}'
  call am_parse         '{"expression":"α2 + β"}'
  call am_parse         '{"expression":"e3"}'
  # Same alphabet gap on the other warning: `Γ(x+1)` is Γ multiplied by a bracket, exactly
  # the trap the unknown-function warning exists to catch.
  call am_parse         '{"expression":"Γ(x+1)"}'
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

  # Linear algebra. The two `notcontains:provided` checks guard a real defect: the
  # determinant is computed by division-based elimination, which leaves a pivot guard that
  # is not mathematics — det([[a,b],[c,d]]) is a*d-b*c for every a, and [[0,J],[J,0]] has
  # eigenvalues +/-J including at J=0.
  call am_matrix        '{"operation":"determinant","matrix":[["a","b"],["c","d"]]}'
  call am_matrix        '{"operation":"tensor_product","matrix":[["1/sqrt(2)","1/sqrt(2)"],["1/sqrt(2)","-1/sqrt(2)"]],"matrix_b":[["1","0"],["0","1"]]}'
  call am_eigenvalues   '{"matrix":[["0","J"],["J","0"]]}'

  # Definite integration, series, and integer facts. The first is the 22/7 > pi proof, which
  # used to take three calls and a hand-done polynomial division.
  call am_integrate     '{"expression":"x^6-4*x^5+5*x^4-4*x^2+4-4/(1+x^2)","variable":"x","from":"0","to":"1"}'
  call am_series        '{"expression":"sin(x)","variable":"x","degree":7}'
  call am_number_theory '{"operation":"factorize","value":"5040"}'

  call am_substitute    '{"expression":"a*x^2 + b*x + c","substitutions":{"x":"t - t_0"}}'
  call am_compare_numeric '{"reference":"sin(x)","approximation":"x - x^3/6","variable":"x","from":0,"to":1,"samples":50}'

  # Representations. The Q15 case pins the exact represented value, and the polar case pins
  # quadrant correction — plain arctan would give pi/4 for -1-i as well.
  call am_represent     '{"operation":"fixed_point","value":"1/sqrt(2)","fraction_bits":15,"total_bits":16}'
  call am_represent     '{"operation":"ieee754","value":"0.1"}'
  call am_represent     '{"operation":"polar","value":"-1 - i"}'

  printf '{"jsonrpc":"2.0","id":99,"method":"resources/list","params":{}}\n'
  printf '{"jsonrpc":"2.0","id":98,"method":"prompts/list","params":{}}\n'
} | timeout 300 "$BIN" 2>/dev/null | python3 "$ROOT/test/report.py"
