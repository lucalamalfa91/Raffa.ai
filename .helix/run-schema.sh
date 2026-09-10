#!/usr/bin/env bash
# Launch the SCHEMA-APPLY process only. Refuses --fresh and --slice.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARTIFACT="$HERE/raffa-schema-process.yaml"
HELIX_BACKEND="${HELIX_BACKEND:-$HERE/../../../helix/src/backend}"

CHECK=0
O="raffa-schema-design"
I="Raffa schema-apply: epic-09 / e09 from existing R0-R4 plan"
ARGS=()

for a in "$@"; do
  case "$a" in
    --check|-Check) CHECK=1 ;;
    --fresh|-Fresh)
      echo "ERROR: run-schema.sh refuses --fresh" >&2
      exit 1
      ;;
    --slice|-Slice|--slice=*|--max|-Max)
      echo "ERROR: run-schema.sh has no fan-out. After e05 HITL:" >&2
      echo "  ./run.ps1 -Max -Slice e09 -o execution-fanout" >&2
      exit 1
      ;;
    *) ARGS+=("$a") ;;
  esac
done

if [ ! -f "$HERE/.env" ]; then
  echo "ERROR: missing $HERE/.env" >&2
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
