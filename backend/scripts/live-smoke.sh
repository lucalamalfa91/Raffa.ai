#!/usr/bin/env bash
# Live smoke test for the Foundry-backed document pipeline (ADR-004/008/017 amendments,
# 2026-09-09). Uploads a real two-page Italian supply contract to a running Contigo API and
# checks that the model actually read it: classification, extracted facts with page-numbered
# evidence, and an Ask answer with citations.
#
# Nothing here is mocked. Against a dev deployment whose Container Apps carry AiGateway__Endpoint,
# every assertion below is served by Azure AI Services: Document Intelligence prebuilt-read for
# the page text, gpt-5.4-nano-dev for classify/extract/answer, text-embedding-3-small-dev for the
# retrieval vectors.
#
#   ./live-smoke.sh https://ca-contigo-dev-api.<region>.azurecontainerapps.io
#
# Requires: bash, curl, python3 (builds the PDF), and jq.
set -euo pipefail

BASE_URL="${1:-${CONTIGO_API_URL:-http://localhost:5080}}"
TENANT_ID="${CONTIGO_TENANT_ID:-$(python3 -c 'import uuid; print(uuid.uuid4())')}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK_DIR="$(mktemp -d)"
PDF="$WORK_DIR/contratto-quadro-smoke.pdf"
trap 'rm -rf "$WORK_DIR"' EXIT

failures=0
pass() { printf '  \033[32mok\033[0m   %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m %s\n' "$1"; failures=$((failures + 1)); }
check() { if [ "$2" = "$3" ]; then pass "$1 = $3"; else fail "$1 = ${2:-<empty>}, expected $3"; fi; }
contains() { case "$2" in *"$3"*) pass "$1 mentions '$3'" ;; *) fail "$1 does not mention '$3': ${2:0:160}" ;; esac; }

api() { # api METHOD PATH [body-file]
  local method="$1" path="$2" body="${3:-}"
  if [ -n "$body" ]; then
    curl -sS -X "$method" "$BASE_URL$path" -H "X-Tenant-Id: $TENANT_ID" \
      -H 'Content-Type: application/json' --data-binary "@$body"
  else
    curl -sS -X "$method" "$BASE_URL$path" -H "X-Tenant-Id: $TENANT_ID"
  fi
}

echo "Contigo live smoke"
echo "  api    : $BASE_URL"
echo "  tenant : $TENANT_ID"

echo
echo "0. health"
health_status="$(curl -sS -o /dev/null -w '%{http_code}' "$BASE_URL/health")"
check "GET /health" "$health_status" "200"

python3 "$SCRIPT_DIR/make-smoke-pdf.py" "$PDF" >/dev/null
echo "  pdf    : $PDF ($(wc -c <"$PDF") bytes)"

echo
echo "1. upload  (Document Intelligence read -> classify -> 7 extraction stages)"
started="$(date +%s)"
upload="$(curl -sS -X POST "$BASE_URL/api/documents" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -F "file=@$PDF;type=application/pdf;filename=contratto-quadro.pdf")"
elapsed=$(($(date +%s) - started))
echo "  took   : ${elapsed}s"
echo "$upload" | jq -c '{id, documentType, processingStatus, pageCount, contractId}' 2>/dev/null || echo "$upload"

document_id="$(echo "$upload" | jq -r '.id // empty')"
contract_id="$(echo "$upload" | jq -r '.contractId // empty')"
if [ -z "$document_id" ]; then
  fail "POST /api/documents returned no document id: ${upload:0:400}"
  exit 1
fi
pass "POST /api/documents -> $document_id"
check "documentType" "$(echo "$upload" | jq -r '.documentType // empty')" "Msa"
check "pageCount" "$(echo "$upload" | jq -r '.pageCount // empty')" "2"
[ "$(echo "$upload" | jq -r '.processingStatus')" = "Failed" ] && fail "processingStatus = Failed" || pass "processingStatus = $(echo "$upload" | jq -r '.processingStatus')"

echo
echo "2. evidence  (facts the model read out of the Italian text, with page numbers)"
evidence="$(api GET "/api/contracts/$contract_id/evidence")"
echo "$evidence" | jq -r '.items[]? | "  \(.fieldName)=\(.value)  p\(.sourcePage)  conf=\(.confidence)"' 2>/dev/null | head -20

value_of() { echo "$evidence" | jq -r --arg f "$1" '.items[]? | select(.fieldName == $f) | .value' | head -1; }
page_of() { echo "$evidence" | jq -r --arg f "$1" '.items[]? | select(.fieldName == $f) | .sourcePage' | head -1; }

contains "supplier" "$(value_of supplier)" "Rossi Software"
check "currency" "$(value_of currency)" "EUR"
check "annualSpend" "$(value_of annualSpend)" "48000"
check "autoRenewal" "$(value_of autoRenewal)" "true"
contains "startDate" "$(value_of startDate)" "2026-01-01"
contains "endDate" "$(value_of endDate)" "2028-12-31"

# The renewal clause lives on page 2 of the PDF: proof the page map came from Document
# Intelligence's own pages[].spans, not from a single concatenated blob.
renewal_page="$(page_of noticePeriodDays)"
if [ "$renewal_page" = "2" ]; then
  pass "noticePeriodDays cites page 2"
else
  fail "noticePeriodDays cites page ${renewal_page:-<none>}, expected 2 (page map lost?)"
fi

echo
echo "3. clauses / obligations / risks"
for section in clauses obligations risks; do
  count="$(api GET "/api/contracts/$contract_id/$section" | jq -r '.items? | length // 0')"
  if [ "${count:-0}" -gt 0 ]; then pass "$section: $count"; else fail "$section: none extracted"; fi
done

echo
echo "4. ask  (answer in the language of the question, with citations)"
for question in "Qual e il canone annuo e quando scade il contratto?" "What is the notice period for termination?"; do
  printf '{"question":%s}' "$(python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$question")" >"$WORK_DIR/ask.json"
  answer="$(api POST "/api/chat/query" "$WORK_DIR/ask.json")"
  kind="$(echo "$answer" | jq -r '.kind // empty')"
  citations="$(echo "$answer" | jq -r '(.citations // []) | length')"
  echo "  Q: $question"
  echo "     kind=$kind citations=$citations"
  echo "$answer" | jq -r '.answerMarkdown // .answer // empty' | head -4 | sed 's/^/     /'
  check "  ask kind" "$kind" "answer"
  if [ "${citations:-0}" -gt 0 ]; then pass "  ask returned $citations citation(s)"; else fail "  ask returned no citations"; fi
done

echo
echo "5. reprocess  (the whole pipeline again on the stored blob)"
reprocess_status="$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE_URL/api/documents/$document_id/reprocess" -H "X-Tenant-Id: $TENANT_ID")"
check "POST /api/documents/{id}/reprocess" "$reprocess_status" "200"

echo
if [ "$failures" -eq 0 ]; then
  echo "All live-smoke checks passed."
else
  echo "$failures live-smoke check(s) failed."
  echo "Check the model ids the app actually used:"
  echo "  az containerapp show -n ca-contigo-dev-api -g rg-contigo-dev --query \"properties.template.containers[0].env[?starts_with(name,'AiGateway__')]\" -o table"
  echo "  az containerapp logs show -n ca-contigo-dev-api -g rg-contigo-dev --tail 100"
fi
exit "$failures"
