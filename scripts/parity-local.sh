#!/usr/bin/env bash
# AskRaffa 4.0 parity stack, locally (docs/design/Raffa.ai/design_handoff_askraffa_v4/PARITY.md "Run locally").
#
#   scripts/parity-local.sh            start everything, stay in the foreground (Ctrl-C stops it all)
#   DETACH=1 scripts/parity-local.sh   start everything and return (PIDs in .parity-local/)
#   scripts/parity-local.sh down       stop what a detached run started
#
# What it starts (same shape as .github/workflows/parity.yml):
#   1. Postgres (pgvector/pgvector:pg16, docker container "raffa-parity-pg") on PG_PORT, with every
#      module's checked-in Migrations/Scripts/*.sql applied in ADR-021 order;
#   2. the API (ASPNETCORE_ENVIRONMENT=Parity: fixtures on, in-memory storage, no Azure) on API_PORT;
#   3. the web app (vite build with a fixtures:true config.json, then vite preview) on WEB_PORT;
#   4. a static server for the reference prototype on REF_PORT.
# Then: http://localhost:$WEB_PORT/today?fixture=zero  vs  http://localhost:$REF_PORT/AskRaffa%20V4.dc.html
#
# Env: PG_PORT (55432) API_PORT (5180) WEB_PORT (5173) REF_PORT (4173)
#      SKIP_WEB_BUILD=1 reuses web/dist; NO_WEB=1 / NO_REF=1 skip those servers; KEEP_DB=1 reuses the DB.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PG_PORT="${PG_PORT:-55432}"
API_PORT="${API_PORT:-5180}"
WEB_PORT="${WEB_PORT:-5173}"
REF_PORT="${REF_PORT:-4173}"
STATE="$ROOT/.parity-local"
PG_CONTAINER="${PG_CONTAINER:-raffa-parity-pg}"
REF_DIR="$ROOT/docs/design/Raffa.ai/design_handoff_askraffa_v4/reference"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"

SCRIPTS=(
  "backend/src/Raffa.Identity.Workspace/Migrations/Scripts/identity-workspace.sql"
  "backend/src/Raffa.Documents.Contracts/Migrations/Scripts/documents-contracts.sql"
  "backend/src/Raffa.Audit/Migrations/Scripts/audit.sql"
  "backend/src/Raffa.Renewals/Migrations/Scripts/renewals.sql"
  "backend/src/Raffa.Savings/Migrations/Scripts/savings.sql"
  "backend/src/Raffa.Quotes/Migrations/Scripts/quotes.sql"
  "backend/src/Raffa.Chat/Migrations/Scripts/chat.sql"
  "backend/src/Raffa.Suppliers.Products/Migrations/Scripts/suppliers.sql"
  "backend/src/Raffa.Market/Migrations/Scripts/market.sql"
)

stop_all() {
  if [ -d "$STATE" ]; then
    for f in "$STATE"/*.pid; do
      [ -f "$f" ] || continue
      pid="$(cat "$f")"
      kill "$pid" 2>/dev/null || true
      pkill -P "$pid" 2>/dev/null || true
      rm -f "$f"
    done
  fi
  if [ "${KEEP_DB:-0}" != "1" ]; then
    docker rm -f "$PG_CONTAINER" >/dev/null 2>&1 || true
  fi
}

if [ "${1:-}" = "down" ]; then
  stop_all
  echo "parity stack stopped"
  exit 0
fi

mkdir -p "$STATE"
wait_http() { # url, label, seconds
  for _ in $(seq 1 "$3"); do
    if curl -fsS -o /dev/null "$1" 2>/dev/null; then return 0; fi
    sleep 1
  done
  echo "timed out waiting for $2 ($1)" >&2
  return 1
}

# 1. Postgres ---------------------------------------------------------------------------------
if [ "${KEEP_DB:-0}" != "1" ] || ! docker inspect "$PG_CONTAINER" >/dev/null 2>&1; then
  docker rm -f "$PG_CONTAINER" >/dev/null 2>&1 || true
  docker run -d --name "$PG_CONTAINER" -p "$PG_PORT:5432" \
    -e POSTGRES_USER=raffa -e POSTGRES_PASSWORD=raffa -e POSTGRES_DB=raffa_parity \
    pgvector/pgvector:pg16 >/dev/null
  for _ in $(seq 1 60); do
    if docker exec "$PG_CONTAINER" pg_isready -U raffa -d raffa_parity >/dev/null 2>&1; then break; fi
    sleep 1
  done
  sleep 1
  for script in "${SCRIPTS[@]}"; do
    echo "applying $script"
    docker exec -i -e PGOPTIONS="-c client_min_messages=warning" "$PG_CONTAINER" psql -q -v ON_ERROR_STOP=1 -U raffa -d raffa_parity < "$ROOT/$script" >/dev/null
  done
fi

# 2. API --------------------------------------------------------------------------------------
CS="Host=localhost;Port=$PG_PORT;Database=raffa_parity;Username=raffa;Password=raffa;Include Error Detail=true"
for name in IdentityWorkspace DocumentsContracts Audit Renewals Savings Quotes Chat Suppliers; do
  export "ConnectionStrings__$name=$CS"
done
export ASPNETCORE_ENVIRONMENT=Parity
export ASPNETCORE_URLS="http://localhost:$API_PORT"
export Fixtures__CorsOrigins__0="http://localhost:$WEB_PORT"
export Fixtures__CorsOrigins__1="http://127.0.0.1:$WEB_PORT"
dotnet build "$ROOT/backend/src/Raffa.Api/Raffa.Api.csproj" -c Release -v q -nologo >/dev/null
( cd "$ROOT" && exec dotnet run --project backend/src/Raffa.Api -c Release --no-build --no-launch-profile ) \
  > "$STATE/api.log" 2>&1 &
echo $! > "$STATE/api.pid"
wait_http "http://localhost:$API_PORT/health" "API" 120
curl -fsS -X POST -H 'Content-Type: application/json' -d '{"scenario":"zero"}' \
  "http://localhost:$API_PORT/api/fixtures/reset" >/dev/null
echo "API      http://localhost:$API_PORT  (log: .parity-local/api.log)"

# 3. Web --------------------------------------------------------------------------------------
if [ "${NO_WEB:-0}" != "1" ]; then
  cd "$ROOT/web"
  if [ "${SKIP_WEB_BUILD:-0}" != "1" ] || [ ! -d dist ]; then
    [ -d node_modules ] || npm ci
    npx vite build >/dev/null
  fi
  cat > dist/config.json <<JSON
{
  "apiBaseUrl": "http://localhost:$API_PORT",
  "oidcAuthority": "https://login.microsoftonline.com/parity-unused",
  "oidcClientId": "parity-unused",
  "oidcRedirectUri": "http://localhost:$WEB_PORT",
  "oidcApiScopes": ["api://parity-unused/Raffa.Read"],
  "fixtures": true
}
JSON
  npx vite preview --port "$WEB_PORT" --strictPort > "$STATE/web.log" 2>&1 &
  echo $! > "$STATE/web.pid"
  wait_http "http://localhost:$WEB_PORT/" "web" 60
  echo "Web      http://localhost:$WEB_PORT/today?fixture=zero&now=2026-09-08"
fi

# 4. Reference prototype -------------------------------------------------------------------------
if [ "${NO_REF:-0}" != "1" ]; then
  ( cd "$REF_DIR" && exec python3 -m http.server "$REF_PORT" ) > "$STATE/ref.log" 2>&1 &
  echo $! > "$STATE/ref.pid"
  wait_http "http://localhost:$REF_PORT/" "reference" 30
  echo "Ref      http://localhost:$REF_PORT/AskRaffa%20V4.dc.html"
fi

echo
echo "Parity:  cd web/parity && REFERENCE_URL='http://localhost:$REF_PORT/AskRaffa%20V4.dc.html' npx playwright test -c playwright.config.ts reference.capture.spec.ts"
echo "         cd web/parity && APP_URL=http://localhost:$WEB_PORT npx playwright test -c playwright.config.ts app.parity.spec.ts geometry.spec.ts"

if [ "${DETACH:-0}" = "1" ]; then
  echo "detached; stop with: scripts/parity-local.sh down"
  exit 0
fi
trap 'stop_all; exit 0' INT TERM
echo "running; Ctrl-C stops everything"
wait
