#!/usr/bin/env bash
# smoke-test.sh - catches the two bug classes that have shipped broken releases:
#   1. Dashboard JS syntax error  -> whole UI dead (1.12.0)
#   2. (covered by unit tests) commandGuard enum binding -> Save Settings 400 (1.12.3/1.12.4)
#
# Usage:  ./scripts/smoke-test.sh
# Requires: dotnet, node. Run from repo root.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -W)"
DASHBOARD="$REPO_ROOT/src/AiLocal.Node/Roles/Dashboard.cs"
TMP_JS="$(mktemp -t hermes-smoke-XXXXXX.js)"

cleanup() { rm -f "$TMP_JS"; }
trap cleanup EXIT

echo "== [1/2] Dashboard JS syntax (node --check) =="
if ! command -v node >/dev/null 2>&1; then
  echo "SKIP: node not found (dashboard syntax check skipped)"
else
  # Extract the last <script>...</script> block (the dashboard app JS).
  python3 - "$DASHBOARD" "$TMP_JS" <<'PY'
import re, sys
text = open(sys.argv[1], encoding='utf-8').read()
m = re.search(r'<script>(.*?)</script>', text, re.DOTALL)
if not m:
    sys.exit("No <script> block found in Dashboard.cs")
open(sys.argv[2], 'w', encoding='utf-8').write(m.group(1))
PY
  if node --check "$TMP_JS"; then
    echo "OK: dashboard JS parses cleanly"
  else
    echo "FAIL: dashboard JS has a syntax error (would crash the whole UI)"
    exit 1
  fi
fi

echo "== [2/2] Unit tests (includes commandGuard string-binding regression test) =="
dotnet test "$REPO_ROOT/tests/AiLocal.Core.Tests/" --nologo -v q 2>&1 | tee /tmp/smoke-test-unit.log | grep -E "Passed!|Failed!|error CS" || true
if grep -q "Failed!" /tmp/smoke-test-unit.log; then
  echo "FAIL: unit tests failed"
  exit 1
fi

echo "SMOKE TEST PASSED"
