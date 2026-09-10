#!/usr/bin/env bash
# Launch the WEB DELTA process only. Never points at raffa-process.yaml.
# Refuses --fresh (would wipe ADR-001…017 / epic-01…05).
# Refuses --slice (must not copy over slice.current.yaml).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARTIFACT="$HERE/raffa-web-process.yaml"
HELIX_BACKEND="${HELIX_BACKEND:-$HERE/../../../helix/src/backend}"

CHECK=0
O="raffa-web-design"
I="Raffa web delta: wave 6+ from existing R0-R4 plan"
ARGS=()

for a in "$@"; do
  case "$a" in
    --check|-Check) CHECK=1 ;;
    --fresh|-Fresh)
      echo "ERROR: run-web.sh refuses --fresh (would wipe the backend plan this delta sits on)" >&2
      exit 1
      ;;
    --slice|-Slice|--slice=*|--max|-Max)
      echo "ERROR: run-web.sh has no fan-out. After e06 exists and the live wave is idle:" >&2
      echo "  ./run.ps1 -Max -Slice e06 -o execution-fanout" >&2
      exit 1
      ;;
    *) ARGS+=("$a") ;;
  esac
done

if [ ! -f "$HERE/.env" ]; then
  echo "ERROR: missing $HERE/.env" >&2
  echo "  cp .env.example .env   # then fill real values" >&2
  exit 1
fi
if [ ! -f "$ARTIFACT" ]; then
  echo "ERROR: missing $ARTIFACT" >&2
  exit 1
fi

set -a
# shellcheck disable=SC1091
. "$HERE/.env"
set +a
export PYTHONUTF8=1

if [ "$CHECK" -eq 1 ]; then
  exec python "$HERE/scripts/validate-artifact.py" "$ARTIFACT" --helix-backend "$HELIX_BACKEND"
fi

if [ ! -d "$HELIX_BACKEND" ]; then
  echo "ERROR: Helix backend not found: $HELIX_BACKEND" >&2
  echo "  export HELIX_BACKEND=<path>/helix/src/backend" >&2
  exit 1
fi

PASS=(-o "$O")
if [ -n "$I" ]; then
  PASS+=(-i "$I")
fi
PASS+=("${ARGS[@]+"${ARGS[@]}"}")

cd "$HELIX_BACKEND"

if command -v uv >/dev/null 2>&1 && uv --version >/dev/null 2>&1; then
  exec uv run helix run "$ARTIFACT" "${PASS[@]}"
fi
if [ -x "$HELIX_BACKEND/.venv/bin/helix" ]; then
  exec "$HELIX_BACKEND/.venv/bin/helix" run "$ARTIFACT" "${PASS[@]}"
fi
if [ -x "$HELIX_BACKEND/.venv/Scripts/helix.exe" ]; then
  exec "$HELIX_BACKEND/.venv/Scripts/helix.exe" run "$ARTIFACT" "${PASS[@]}"
fi

echo "ERROR: neither a working 'uv' nor a backend venv is available." >&2
exit 127
