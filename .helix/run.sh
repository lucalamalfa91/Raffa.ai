#!/usr/bin/env bash
# =============================================================================
# raffa-flow -- launch wrapper
# =============================================================================
# helix loads only the FIRST .env among [helix-repo/.env, $PWD/.env]. We export
# this artifact's .env first so secrets stay co-located and win (override=False).
#
#   ./run.sh --check
#   ./run.sh -o raffa-design -i "Raffa V1 design pass"
#   ./run.sh -o raffa-execution -i "reports/workitems/.../task-....md"
#   ./run.sh --max --slice r0-a -o execution-fanout
#     (worktrees of the local clone; on_orchestration_stop opens the GitHub PR)
#   ./run.sh --fresh -o raffa-design -i "..."
# =============================================================================
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARTIFACT="$HERE/raffa-process.yaml"
HELIX_BACKEND="${HELIX_BACKEND:-$HERE/../../../helix/src/backend}"

CHECK=0
FRESH=0
USE_MAX=0
SLICE=""
ARGS=()
expect_slice=0
for a in "$@"; do
  if [ "$expect_slice" -eq 1 ]; then
    SLICE="$a"
    expect_slice=0
    continue
  fi
  case "$a" in
    --check) CHECK=1 ;;
    --fresh) FRESH=1 ;;
    --max) USE_MAX=1 ;;
    --slice=*) SLICE="${a#--slice=}" ;;
    --slice|-Slice) expect_slice=1 ;;
    *.yaml)  ARTIFACT="$HERE/$a" ;;
    *)       ARGS+=("$a") ;;
  esac
done

if [ ! -f "$HERE/.env" ]; then
  echo "ERROR: missing $HERE/.env" >&2
  echo "  cp .env.example .env   # then fill real values" >&2
  exit 1
fi
set -a
# shellcheck disable=SC1091
. "$HERE/.env"
set +a

# Windows cp1252 cannot decode some UTF-8 bytes in YAML/markdown comments.
export PYTHONUTF8=1

MISSING=()
for v in DEEPSEEK_BASE_URL DEEPSEEK_API_KEY DEEPSEEK_REASONING_MODEL \
         DEEPSEEK_FAST_MODEL \
         ANTHROPIC_DEFAULT_SONNET_MODEL; do
  [ -n "${!v:-}" ] || MISSING+=("$v")
done
if [ "${#MISSING[@]}" -gt 0 ]; then
  echo "ERROR: unset variables in $HERE/.env:" >&2
  printf '  - %s\n' "${MISSING[@]}" >&2
  exit 1
fi

if [ "$USE_MAX" -eq 1 ]; then
  # Present-but-empty: Helix load_dotenv(override=False) will not refill Hub
  # URL/token from this .env. unset lets the file win again.
  echo "[run.sh] --max: blanking Hub URL/token so Claude Code uses Max login"
  export ANTHROPIC_API_KEY=
  export ANTHROPIC_AUTH_TOKEN=
  export ANTHROPIC_BASE_URL=
elif [ -n "${ANTHROPIC_API_KEY:-}" ]; then
  echo "ERROR: ANTHROPIC_API_KEY is set. Passata 2 bills Claude Code Max" >&2
  echo "  (claude login), not Console API. Unset it or pass --max." >&2
  exit 1
fi

if [ -n "$SLICE" ]; then
  SLICE="$(echo "$SLICE" | tr '[:upper:]' '[:lower:]')"
  has_o=0
  for a in "${ARGS[@]+"${ARGS[@]}"}"; do
    [ "$a" = "-o" ] && has_o=1
  done
  if [ "$has_o" -eq 0 ]; then
    ARGS+=(-o execution-fanout)
  fi
fi

want_fanout=0
prev=""
for a in "${ARGS[@]+"${ARGS[@]}"}"; do
  if [ "$prev" = "-o" ] && [ "$a" = "execution-fanout" ]; then
    want_fanout=1
  fi
  prev="$a"
done
if [ "$want_fanout" -eq 1 ]; then
  if [ -z "$SLICE" ]; then
    echo "ERROR: execution-fanout needs --slice <id> (one slice wave-spec)." >&2
    echo "  See reports/plan/slices/INDEX.md" >&2
    echo "  e.g.  ./run.sh --max --slice r0-a -o execution-fanout" >&2
    exit 1
  fi
  src="$HERE/reports/plan/slices/${SLICE}.yaml"
  dst="$HERE/reports/plan/slice.current.yaml"
  if [ ! -f "$src" ]; then
    echo "ERROR: unknown slice '$SLICE' (missing $src)" >&2
    exit 1
  fi
  cp "$src" "$dst"
  echo "[run.sh] slice $SLICE -> slice.current.yaml"
  if [ "$CHECK" -eq 0 ]; then
    echo "[run.sh] ensure local clone is a git toplevel (fan-out worktrees)"
    python "$HERE/scripts/ensure_artifact_git.py" || exit $?
  fi
fi

if [ "$FRESH" -eq 1 ]; then
  echo "[run.sh] --fresh: clearing design outputs under $HERE/reports" >&2
  rm -rf "$HERE/reports/context" "$HERE/reports/architecture" \
         "$HERE/reports/workitems" "$HERE/reports/costs" "$HERE/reports/briefing" \
         "$HERE/reports/audit" "$HERE/reports/open-questions.md"
  mkdir -p "$HERE/reports"/{context,architecture/draft,workitems,plan,costs,briefing,audit}
  for d in context architecture workitems plan costs briefing audit; do
    touch "$HERE/reports/$d/.gitkeep"
  done
  printf 'waveId: placeholder\nstatus: planned\nphases: []\nforks: []\n' \
    > "$HERE/reports/plan/wave-spec.execution.yaml"
  printf 'waveId: slice-unset\nstatus: planned\nphases: []\nforks: []\n' \
    > "$HERE/reports/plan/slice.current.yaml"
fi

if [ "$CHECK" -eq 1 ]; then
  exec python "$HERE/scripts/validate-artifact.py" "$ARTIFACT" --helix-backend "$HELIX_BACKEND"
fi

if [ ! -d "$HELIX_BACKEND" ]; then
  echo "ERROR: Helix backend not found: $HELIX_BACKEND" >&2
  echo "  export HELIX_BACKEND=<path>/helix/src/backend" >&2
  exit 1
fi

cd "$HELIX_BACKEND"

uv_ok=0
if command -v uv >/dev/null 2>&1 && uv --version >/dev/null 2>&1; then
  uv_ok=1
elif command -v uv >/dev/null 2>&1; then
  echo "[run.sh] uv shim is present but broken; using helix from .venv"
fi

helix_run() {
  if [ "$uv_ok" -eq 1 ]; then
    uv run helix run "$ARTIFACT" ${ARGS[@]+"${ARGS[@]}"}
  elif [ -x "$HELIX_BACKEND/.venv/bin/helix" ]; then
    "$HELIX_BACKEND/.venv/bin/helix" run "$ARTIFACT" ${ARGS[@]+"${ARGS[@]}"}
  elif [ -x "$HELIX_BACKEND/.venv/Scripts/helix.exe" ]; then
    "$HELIX_BACKEND/.venv/Scripts/helix.exe" run "$ARTIFACT" ${ARGS[@]+"${ARGS[@]}"}
  else
    echo "ERROR: neither a working 'uv' nor a backend venv is available." >&2
    echo "  './run.sh --check' still validates the artifact." >&2
    return 127
  fi
}

set +e
helix_run
rc=$?
set -e
exit "$rc"
