# Wave w17 "the product officializes what it knows, shows the page it read it from, and answers where you can save" — acceptance runbook (W17-A1, A17-1…A17-5, A17-S1, A17-S2, N16–N20)

Operator checklist for wave `w17` (`.helix/reports/context/waves/w17-requirements.md`;
decision record `.helix/reports/architecture/waves/w17.md`; ADR-016 w17 clauses 41–45,
ADR-014 w17 clauses 1–8, ADR-005 w17 §19 / §24, ADR-029, ADR-024 w17). One section per
acceptance item, each naming **the URL, the header posture and the expected status code**
plus the observable pass condition. Same shape as [`w16-acceptance.md`](w16-acceptance.md).

| | |
|---|---|
| Owner | task E22/F06/US01/T01 (`w17-integration`), story `us-01-final-integration` |
| Oracles | `w17-requirements.md` items NW-72, NW-73, NW-71, NW-20, NW-22, NW-62, NW-26, NW-63, NW-64, NW-65, NW-66 · ADR-016 w17 · ADR-014 w17 · ADR-029 · ADR-024 w17 · ADR-012 w17 · ADR-019 w17 · ADR-020 w17 · ADR-001 w17 |
| API contract | `web/openapi/raffa-api.v1.json` — **every route below is quoted from it** |
| Screens | `web/README.md` "Screens (ADR-024 V2 route map)" → `/savings`, `/contracts/:contractId`, `/contracts/:contractId/review`, `/documents/:documentId/viewer` |
| Automated cover | `dotnet test backend/Raffa.slnx` (including Postgres / Testcontainers **and** `AuthenticationSeamAbsenceTests` over `.github/workflows/**`) · `cd web && npm run build && npm test` · `web/e2e/w17-*.spec.ts` (runbook, not CI — no workflow runs Playwright, ADR-012 §12 / w17 clause 31) |

Run this against **`dev`**. Promotion to `demo` is a separate operator act *after* this walk.
w17 is **two PRs** (ADR-014 w17 clause 2): PR 1 is the Service Bus Send grant
([#141](https://github.com/lucalamalfa91/contigo/pull/141), merged); PR 2 is the rest of the
wave. **A17-S2 cannot pass before the `raffa-dev` HCP apply of PR 1 is confirmed in the HCP
UI.** Dispatching the console against a missing Send grant deletes and commits chunks, requeues
nothing and writes no audit row (ADR-016 w17 clause 44 / ADR-011 w17 clause 26). That is a
data-destruction guard, not etiquette.

The engine's `reports/execution/wave-close.md` is a fan-out delivery report, **not** the wave
record (ADR-014 w17 clause 5). It is never cited as current. This document plus
`reports/audit/w17-hitl.md` is the wave record.

---

## 0. Before you start

### 0.1 What the wave is, in one paragraph

The server officializes extraction: every field at or above a raw `0.90` is
`auto_accepted` on first open of Review, including a critical one; everything below is
`review_required`; a human acceptance is a third persisted state, `human_accepted`. The
web renders those decisions and never recomputes them. Contract 360 answers
**Where you can save** and **When you must move** from the existing `/strategy` pack,
shows only officialized facts, and drops confidence from that screen. Opening a
validated Northwind PDF shows **that file's page**. Savings verified money is grouped
by currency from `RealizedSavings` rows. A CI console re-enqueues a whole tenant
through the same worklist `verify-tenant-corpus.yml` already computes.

### 0.2 How the wave was built

Fourteen live tasks in five phases (`.helix/reports/plan/slices/w17.yaml`). PR 1
(`E20/F02/US01/T01`, [#141](https://github.com/lucalamalfa91/contigo/pull/141)) merged the
four Terraform files of ADR-016 w17 clause 37 plus `infra/README.md`. The remaining
thirteen product tasks landed locally on this checkout and are not merged at the time this
runbook was written. Nothing in this document relies on the fan-out engine's own delivery
claims.

### 0.3 The operator sequence

Run these **in order**. The order is an acceptance rule, not a build dependency
(ADR-005 w17 §24, ADR-016 w17 clause 44).

| # | What | How |
|---|---|---|
| 1 | **Confirm the `raffa-dev` HCP VCS apply of PR 1 has landed** | HCP Terraform workspace `raffa-dev`. Record applied-or-not as a **fact** in [Known gaps](#known-gaps-that-shape-acceptance-today). **Do not dispatch `reprocess-tenant-documents.yml` before this confirm.** A premature dispatch is destructive (clause 44) |
| 2 | Confirm the `raffa-demo` HCP VCS apply queued by the same merge | HCP workspace `raffa-demo`. PR 1's merge queues **two** VCS runs (`scripts/hcp_vcs_wiring.py` tracks both workspaces to `main` on `infra/`). This confirm is a **promotion precondition**, not a destruction guard. Record applied-or-not |
| 3 | Confirm the inherited PR #118 apply (`worker_max_replicas` 3→5 + `MaxConcurrentCalls=4`) | HCP `raffa-dev`. Record applied-or-not. It does not gate the walk except as throughput |
| 4 | Merge the wave PR (PR 2) to `main` | `dev` deploys on push (`backend.yml`, `web.yml`); the image tag is the merged sha — W17-A1 (a) |
| 5 | One interactive sign-in on deployed `dev` | W17-A1 (f) |
| 6 | Walk W17-A1, then A17-1…A17-5, A17-S1, N16–N20 | this document. **N17 includes the 20-file render measurement** |
| 7 | **Only after step 6's 20-file batch completes with zero Worker restarts**: A17-S2 | verify-tenant-corpus → console dispatch → verify again. A bulk run is not a substitute for the 20-file measurement — `MaxConcurrentCalls = 4` with `min_replicas = 0` scales replicas out and multiplies concurrent bitmaps |

### 0.4 The values every command below needs

```bash
ENV=dev
RG="rg-raffa-${ENV}"
API=$(az resource show --resource-group "$RG" --name "ca-raffa-${ENV}-api" \
        --resource-type Microsoft.App/containerApps \
        --query "properties.configuration.ingress.fqdn" -o tsv)
API="https://${API}"
WEB=$(az resource show --resource-group "$RG" --name "swa-raffa-${ENV}" \
        --resource-type Microsoft.Web/staticSites \
        --query "properties.defaultHostname" -o tsv)
WEB="https://${WEB}"
# TOKEN: access token the SPA acquires (api://raffa-dev-api/Raffa.Read Raffa.Write)
# TENANT: workspace id (GET $API/api/workspaces → workspaces[].id)
# CONTRACT, DOCUMENT, QUOTE: ids from the screens you walk
```

**Header posture.** Every tenant-scoped route below sends `Authorization: Bearer $TOKEN` and
`X-Tenant-Id: $TENANT`. **Never** `X-Role`, `X-Workspace-Role` or `X-User-Id`. Identity is
the bearer `oid`. `GET /api/capabilities` takes **no** tenant header.

---

## W17-A1 — the wave base (ADR-014 w17 clauses 4 and 8)

This is the **operator's** gate checklist, not a task. Record each with its value; the wave
is not closed until all eight are written down.

| Point | Check | Pass |
|---|---|---|
| (a) | `git fetch origin`; the base SHA is **read at the gate**, never quoted from a wave document | expect `origin/main == helix/w17 == d3d2d24` at launch; if it moved, merge and re-check. At the time this runbook was written, `origin/main` had already moved past `d3d2d24` (PR #141 and post-w16 CI fixes) |
| (b) | `git diff --stat origin/main..HEAD -- backend web infra .github docs scripts` on `main` after PR 2 merges | **empty** — stated as a two-dot diff |
| (c) | `integration` re-created from the wave base | never merged into |
| (d) | `backend.yml` **build + test** at the base commit | green, including Postgres / Testcontainers |
| (e) | `python scripts/check_slice_prereqs.py --record-hitl w16` | stamp exists so the next slice's prereq check does not fail |
| (f) | `git tag -l "demo-v*"` **read and written down** | **read 2026-09-16 on this checkout: `demo-v1`, `demo-v2`, `demo-v3`.** No `demo-v4`. w17 cuts no tag of its own |
| (g) | three HCP runs across two workspaces, written separately | (i) `raffa-dev` PR 1 / clause 37 — **destruction guard**, blocks the first console dispatch; (ii) `raffa-demo` PR 1 — **promotion precondition**, blocks the tag; (iii) PR #118 — inherited unknown. A single tick against "the applies" satisfies none of the three. Record each in Known gaps. Then check the **running image tag against `main`** |
| (h) | `raffa-dev` HCP VCS apply of clause 37 **confirmed landed in the HCP UI before the NW-73 workflow is dispatched even once** | same confirm as sequence step 1 |

---

## A17-1 … A17-4 — the server decides at 90 %, including a critical field (NW-71)

> NW-71 · ADR-024 w17 clause A · ADR-003 w17 clause 1 · ADR-019 w17 clauses 7–8 · task `E22/F01/US01/T01` (server) / `E22/F01/US02/T01` (web)

**Click path.** Upload a document whose extraction produces fields **on both sides of the
bar** (at least one ≥ 0.90, at least one below, **including a critical field** —
`annualSpend`, `totalContractValue`, `cancellationDeadline`, `endDate` or
`renewalTermMonths`). Open `WEB/contracts/<id>/review` **once**. Do not click Accept on the
high-confidence rows. Reload. Open the same review URL in a second browser, same account.

**Pass when:**

1. Every field whose stored confidence is `>= 0.90` (raw double, no rounding before the
   compare) already reads **Accepted automatically · NN%** on first open, **including a
   critical one**. No click.
2. Every field below the bar is queued as **Review · NN%**. The title is
   **N facts need you — you decide** — it does not name 90 or 80.
3. Reload and the second browser agree. There is no `acceptedThisSession` store.
4. One audit row per document per extraction run, actor `system:extraction`, `detail`
   shaped `contractId=<guid>; fields=<name>:<confidence>,…` — **field names and confidence
   numbers, never a field value**.
5. No screen shows a percentage that contradicts its own decision state. A
   `review_required` field at `0.895` must not read **Review · 90 %** (floor, never round
   up across the bar). The legend is server-fed from `autoAcceptThreshold` (never a
   hardcoded 90 in the web).

```bash
curl -s -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT/evidence"
# 200 — { autoAcceptThreshold: 0.9, fields: [{ fieldName, decision, confidence, … }] }

curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT/evidence" \
  | jq '{autoAcceptThreshold, fields: [.fields[] | {fieldName, decision, confidence}]}'
# every decision is auto_accepted | human_accepted | review_required
# auto_accepted rows have confidence >= 0.90; review_required rows have confidence < 0.90 or null
```

A client-supplied `decision` on validate or PATCH is **400 and never persisted**.

```bash
curl -s -o /dev/null -w '%{http_code}' -X PATCH \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"decision":"auto_accepted"}' \
  "$API/api/contracts/$CONTRACT"
# 400 — writes nothing
```

**psql** (only SQL proves the trail names fields, never values):

```sql
SET app.tenant_id = '<tenant>';
SELECT actor, action, detail
FROM audit_event
WHERE resource_type = 'document'
  AND actor = 'system:extraction'
ORDER BY timestamp DESC
LIMIT 5;
-- detail contains field names and confidence numbers
-- detail does not contain extracted amounts, dates or supplier strings
```

```sql
SET app.tenant_id = '<tenant>';
SELECT field_name, confidence, decision, decided_at
FROM extraction_evidence
WHERE contract_id = '<contract>'
ORDER BY field_name;
-- decision in (auto_accepted, human_accepted, review_required)
```

**Automated:** `ExtractionConfidencePolicyTests`; `StagedExtractionServiceTests`;
`ContractEvidenceSchemaTests`; `DocumentQueryServiceTests`; `DocumentsV2EndpointTests`;
`semantics.test.ts`; `reviewViewModel.test.ts`; `ReviewRoute.test.tsx`.

---

## A17-5 — auto-accepted and human-accepted are told apart without hovering (NW-71)

> NW-71 · ADR-001 w17 clause 8 · ADR-019 w17 clause 7 · task `E22/F01/US02/T01`

**Click path.** On the same Review screen: one field the rule accepted (leave it) and one
field you accept yourself. Do not hover.

**Pass when:** the first tag reads **Accepted automatically · NN%**; the second reads
**Accepted by you** with **no percentage**. Both use `.tag-neutral` — the text carries the
difference, never a third colour. Nothing claims the user decided a field the rule decided.
The word `officialized` does not appear on any screen.

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT/evidence" \
  | jq '[.fields[] | select(.decision=="auto_accepted" or .decision=="human_accepted") | {fieldName, decision, confidence}]'
# both states present; human_accepted is the field you accepted
```

**Automated:** `semantics.test.ts` (label-only `getConfidenceTag`); `reviewViewModel.test.ts`;
`ReviewRoute.test.tsx`.

---

## A17-S1 — Savings verified is realized money, one line per currency (NW-72)

> NW-72 · ADR-001 w17 clause 1 · ADR-012 w17 clause 37 · ADR-020 w17 §15 · task `E20/F01/US01/T01`

**Click path.** Record an outcome that realizes an opportunity (`WEB/quotes/<id>` → record
outcome linked to a savings opportunity, or `PATCH /api/savings/{id}` with
`realizedAmount`). Open `WEB/savings`. Reload. Open the same workspace in a second browser.

**Pass when:**

1. The fourth cell is labelled **Savings verified** (not "Verified", not "Realized").
2. The cell shows **money for realized**, **one formatted line per currency**, never a
   cross-currency total. Empty → `—` with meta **no verified savings recorded yet**, never a
   fabricated `0`.
3. Reload and the second browser agree. The SPA does not sum.
4. An unlinked outcome (`savingsPropagated: null`) moves **no** figure.
5. The pre-negotiation estimate (`savingsIdentified` range) appears **nowhere** in that cell.

```bash
curl -s -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/savings/kpis"
# 200

curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/savings/kpis" \
  | jq '{savingsRealized, savingsIdentified}'
# savingsRealized: [{ currency, amount, count }]  — a number, not { low, high }
# savingsIdentified: [{ currency, low, high, count, averageConfidence }]  — still a band
```

**psql** (only SQL proves the cell is reading `realized_savings`, not the estimate):

```sql
SET app.tenant_id = '<tenant>';
SELECT currency, SUM(amount) AS amount, COUNT(*) AS count
FROM realized_savings
GROUP BY currency
ORDER BY currency;
-- these rows are what savingsRealized must match; an opportunity's estimate range is not in this table
```

**Automated:** `SavingsKpiCalculatorTests`; `SavingsKpiQueryServiceTests`;
`SavingsKpiEndpointTests`; `savingsViewModel.test.ts`; `SavingsRoute.test.tsx`;
`web/e2e/w17-savings.spec.ts` (runbook).

---

## N16 — Where you can save and When you must move are answers (NW-20, NW-22, NW-62)

> NW-20 · NW-22 · NW-62 · ADR-024 w17 clauses 6–11 · ADR-012 w17 clause 35 · ADR-020 w17 §14(f)–(g) · tasks `E21/F01/US01/T01`, `E21/F02/US01/T01`, `E21/F03/US01/T01`, `E21/F03/US02/T01`

**Click path.** Open `WEB/contracts/<id>` on a validated contract. Read **Where you can
save** and **When you must move**. Open the Activity / provenance list on the same screen.

**Pass when:**

1. Both answers are concrete with citations — a figure and its lever, or a **representative**
   band whose provenance rides the detail line (`adapter …, n = …`), never a bare "market".
2. `GET /api/contracts/{id}` `tabs.benchmark` is **non-empty or** carries the explicit
   `insufficient_data` entry. `[]` is today's defect and cannot be told apart from "never wired".
3. Every `tabs.activity` entry traces to a persisted audit event (`occurredAt`, `action`,
   `actorLabel`). `AuditEvent.Detail` is never projected.
4. No market claim without its source. Neither answer reads **Not yet available** while the
   facts it needs are in the file. "Not yet available" survives **only** as the
   absent-or-failed state of a source that was really called.
5. `OpenWeakFacts` renders **no** review statement and no copy on this screen uses the word
   **weak**.

```bash
curl -s -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT"
# 200

curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT" \
  | jq '{benchmark: .tabs.benchmark, activity: .tabs.activity}'
# benchmark: [] is a fail; [{ metric, status: "insufficient_data", … }] is a pass
# activity entries: { occurredAt, action, actorLabel } only

curl -s -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT/strategy"
# 200 — WhenYouMustMove + WhereYouCanPush; this is the answer the band maps, never a second computation
```

**psql** (only SQL proves activity is a projection of persisted events, not a fabricated timeline):

```sql
SET app.tenant_id = '<tenant>';
SELECT timestamp, action, actor
FROM audit_event
WHERE resource_id = '<contract>'
ORDER BY timestamp DESC
LIMIT 20;
-- every activity.action on the 360 is in this list (allow-listed actions only)
```

**Automated:** `Contract360EndpointTests`; `Contract360QueryServiceTests`;
`ContractActivityProjectionTests`; `RenewalPipelineBuilderTests`; `RenewalsEndpointTests`;
`StrategyPackBuilderTests`; `InsightsEndpointCompositionTests`; `ContractStrategyEndpointTests`;
`contract360ViewModel.test.ts`; `Contract360Route.test.tsx`; `web/e2e/w17-answers.spec.ts`
(runbook).

---

## N17 — the viewer shows the page it read, and `?page=N` is never clamped (NW-26, NW-63)

> NW-26 · NW-63 · ADR-029 · ADR-018 w17 clauses 9–14 · ADR-012 w17 clauses 32–33, 47 · tasks `E22/F02/US01/T01`, `E22/F03/US01/T01`

**Click path.** From a validated Northwind (or any real) PDF, open
`WEB/documents/<id>/viewer`. Then `?page=2`. Then a deep link beyond `pageCount`
(`?page=N` where `N > pageCount`).

**Pass when:**

1. The first open shows **that file's page**, not `PREVIEW NOT RENDERED`.
2. `?page=2` shows page 2. The current page lives in the URL.
3. A deep link beyond `pageCount` renders **not found** with the URL **still reading
   `?page=N`** — never clamped, never rewritten, never redirected to `/`.
4. The rail gains **no** Documents row. The viewer is a citation-reached route.

```bash
curl -s -o /dev/null -w '%{http_code}' -D - \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/documents/$DOCUMENT/preview?page=1" | head
# 200
# content-type: image/png

curl -s -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/documents/$DOCUMENT/preview?page=2"
# 200 (when pageCount >= 2) or 404 (when pageCount is 1) — never a silent page 1

curl -s -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/documents/$DOCUMENT/preview?page=99999"
# 404 — the SPA keeps ?page=99999 in the address bar
```

**20-file render measurement (ADR-005 w17 §19 / §24) — before A17-S2.** On deployed `dev`,
upload **20 PDFs** (or reprocess 20 already-stored PDFs **one at a time via the product**,
not the console). Confirm **zero Worker replica restarts** while they rasterise:

```bash
az containerapp replica list -n ca-raffa-dev-worker -g rg-raffa-dev -o table
az monitor activity-log list -g rg-raffa-dev \
  --resource-id "$(az containerapp show -n ca-raffa-dev-worker -g rg-raffa-dev --query id -o tsv)" \
  --max-events 50 --query "[?contains(operationName.value, 'restart')]"
# no restart during the batch
```

A bulk whole-tenant reprocess is **not** a substitute for this measurement. It runs
**after** this row, never before.

**OQ-w17-cl-03 (recorded, not repaired here).** A signed-out
`/documents/:id/viewer?page=3&clause=…` deep link through the OIDC round trip is the
viewer's check. If the post-login return URL **drops the query**, the repair is the **auth
landing** (ADR-012 w14 footer `:189-285`), never the viewer and never a `sessionStorage`
stash. The viewer still shows page 1 in the ordinary page state and says the **citation
could not be restored** — that copy is owed whether or not the query survives. See
[Known gaps](#known-gaps-that-shape-acceptance-today) row 8.

**Automated:** `DocumentPreviewRenderingTests`; `DocumentStoragePathTests`;
`DocumentsV2EndpointTests`; `client.test.ts`; `web/e2e/w17-viewer.spec.ts` (runbook; carries
N17, N20 and OQ-w17-cl-03's check).

---

## N18 — unrecovered fields stay as empty fillable rows (NW-64)

> NW-64 · ADR-001 w17 clause 5 · ADR-012 w17 clause 38 · ADR-020 w17 §13 · task `E22/F05/US01/T01`

**Click path.** Open Review on a document whose OCR missed **end date** and **cancellation
deadline**. Fill one. Reload. Open the same review URL in a second browser.

**Pass when:**

1. Both missed fields render as **empty fillable rows** under **Not found in the document**,
   with the sentence *"Raffa could not find these in the file. Type the value if you have
   it — it is saved as your correction."*
2. A missing field has **no** confidence tag.
3. Filling one persists across a reload and a second browser (`PATCH /api/contracts/{id}`).
4. The section does not render at all when every canonical field was recovered. Termination
   and price uplift have **no** correctable field this wave and do not appear as inputs.

```bash
curl -s -o /dev/null -w '%{http_code}' -X PATCH \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"corrections":{"endDate":"2027-12-31"}}' \
  "$API/api/contracts/$CONTRACT"
# 200

curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT" \
  | jq '.header.endDate'
# "2027-12-31" after reload
```

**Automated:** `reviewViewModel.test.ts` (the skip at the missing-field row is **rewritten**,
not deleted); `ReviewRoute.test.tsx`; `web/e2e/w17-review.spec.ts` (runbook).

---

## N19 — Details carries no "facts still to decide" list (NW-65)

> NW-65 · ADR-020 w17 §14(a)–(d) · ADR-012 w17 clause 36 · task `E22/F04/US01/T01`

**Click path.** Open `WEB/contracts/<id>`. Read the Details column. Repeat on a contract
with zero `review_required` facts.

**Pass when:**

1. There is no "facts still to decide" list and no sentence claiming a fact was
   "signed off by you".
2. **Review all →** stays as the column's trailing line, carrying a count:
   **N facts still need you — Review all →**.
3. At **zero** undecided facts the trailing line **does not render**.
4. Unofficialized values keep their row and show the em-dash placeholder. Products /
   Obligations / Risks follow the same officialized gate (`FactTable.tsx`). No confidence
   tag renders on Contract 360.

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT/evidence" \
  | jq '[.fields[] | select(.decision=="review_required")] | length'
# that number is N on the trailing line; 0 → the line is absent
```

**Automated:** `contract360ViewModel.test.ts` (`computeNeedsAttention` reads `decision`,
never a tag variant); `Contract360Route.test.tsx`.

---

## N20 — a Why row is type, value, page · section, one leverage tag (NW-66)

> NW-66 · ADR-001 w17 clause 6 · ADR-019 w17 clause 9 · ADR-018 w17 clause 10 · task `E22/F04/US01/T01`

**Click path.** On Contract 360, open a Why clause row. Click **Open in document viewer**.

**Pass when:**

1. The row shows **type**, **normalized value**, **page · section** and **exactly one
   leverage tag** in the words **Push to change** / **Worth raising** / **Standard terms**.
2. **No percentage and no raw enum** (`Critical`/`High`/`Medium`/`Low`) anywhere on
   Contract 360.
3. The quote is one click away (the existing `ClauseHighlight`, not a new component).
4. The viewer link is `/documents/:documentId/viewer?page=<sourcePage>&clause=<clauseId>`
   and **lands on the cited page**.

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT" \
  | jq '.tabs.clauses[0] | {type, normalizedValue, sourcePage, riskLevel}'
# riskLevel may exist on the wire; the screen never prints the raw enum
```

**Automated:** `contract360ViewModel.test.ts` (`getClauseRiskTag`); `Contract360Route.test.tsx`;
`web/e2e/w17-viewer.spec.ts` (N20 case, runbook).

---

## A17-S2 — whole-tenant reprocess empties the worklist (NW-73)

> NW-73 · ADR-016 w17 clauses 36–45 · ADR-014 w17 clause 2 · ADR-022 w17 clause 6b · task `E20/F02/US02/T01`

⚠ **Step 0 — before any dispatch.** Confirm the `raffa-dev` HCP apply of PR 1 has landed
**in the HCP UI** (sequence step 1 / W17-A1 (h)). If it has not, **stop**. Do not dispatch.
A run against a missing Send grant is destructive, not merely failed.

⚠ **Ordering.** This section runs **after** N17's 20-file render measurement (sequence
step 6). Do not invert them.

**Click path.** Actions → **verify-tenant-corpus** → `target_environment: dev`,
`tenant_id: $TENANT` (worklist N). Then Actions → **reprocess-tenant-documents** →
`target_environment: dev`, `tenant_id: $TENANT`. Then verify again.

The console workflow's `environment: ${{ inputs.target_environment }}` binds the Key Vault
secret read on the job that holds `id-token: write`. With `options: [dev]` the expression
can only resolve to `dev`. Anyone with repository write access who **explicitly dispatches**
this job can trigger a bulk reprocess. That is the whole `dev` gate.

**Pass when:**

1. Second verify reports worklist **0** and `%PDF` gone.
2. Every document reaches a terminal state (`Completed`, `NeedsReview`, `Failed`,
   `Rejected`).
3. A16-3's single-document Admin path stays green:
   `POST /api/documents/{id}/reprocess` as Admin is **`202`**.
4. Actor on run-scoped audit rows is the fixed literal **`system:bulk-reprocess`**. No
   CI-controlled string is interpolated into `AuditEvent.Actor`. Human attribution rides
   in `Detail`.
5. Zero rows under a valid tenant exits **non-zero**. Re-runs are safe (`MessageId`
   collapses duplicates).

```bash
# A16-3 still holds — one document, in the product
curl -s -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/documents/$DOCUMENT/reprocess"
# 202
```

**psql** (only SQL proves `%PDF` is gone and the actor is the reserved principal):

```sql
SET app.tenant_id = '<tenant>';
SELECT COUNT(*) FROM embedding WHERE left(chunk_text, 4) = '%PDF';
-- 0

SELECT actor, COUNT(*)
FROM audit_event
WHERE actor = 'system:bulk-reprocess'
GROUP BY actor;
-- actor is exactly 'system:bulk-reprocess'
```

**Automated:** `BulkReprocessTenantBindingTests`; `BulkReprocessStopAtFirstFailureTests`;
`BulkReprocessAuditActorTests`; `AuthenticationSeamAbsenceTests` (scans
`.github/workflows/**`, including the new console workflow); `DependencyDirectionTests`
(`Raffa.Tools` is in `AllRaffaProjects`, not in the domain-module array).

Playwright does **not** cover A17-S2. No workflow runs Playwright.

---

## Promotion sequence

w17 is **two** PRs. PR 1 (#141) already merged the grant. PR 2 is the wave. **w17 cuts no
`demo-v*` tag** as part of the wave itself.

`git tag -l "demo-v*"` **read 2026-09-16 on this checkout: `demo-v1`, `demo-v2`,
`demo-v3`.** Clearing the promotion backlog — if the operator chooses to — is three acts
in order (ADR-016 w17 clause 43): (a) PR 1's merge has already queued VCS runs on both
workspaces; (b) **both confirmed in the HCP UI** (`raffa-dev` before the first console
dispatch, `raffa-demo` before the tag); (c) the tag push, its number **read**, never
assumed.

Clearing that backlog **does not** clear the invitation walk. The three `demo` flags
(`invitation_mail_enabled`, `guest_provisioning_enabled`,
`guest_role_assignment_managed`) stay `false` and w17 flips none.

From PR 1's merge, **`demo` holds a Send grant nothing can target**: the console workflow
offers `dev` only. The grant exists because the variable is **required** and
`raffa-demo`'s plan must stay green, not because `demo` has a consumer.

`demo-promote.yml` → `infra.yml`, whose apply job writes a step summary and does not apply.
A green `promote-infra` is **not** evidence that `demo` has the role assignment. Confirm
the HCP VCS run on `raffa-demo` in the HCP UI.

---

## Known gaps that shape acceptance today

ADR-016 w17 clause 42. Recorded as of this checkout on 2026-09-16. Facts, not assumptions.

| # | Gap | Effect on acceptance | Disposition |
|---|---|---|---|
| 1 | **`raffa-dev` HCP VCS apply of PR 1 (#141, clause 37 — CI principal `Azure Service Bus Data Sender` on `extraction-events`).** HCP UI state is not readable from this checkout. | **Record applied / not applied here: `__________`.** Blocks the first `reprocess-tenant-documents.yml` dispatch (clause 44). A premature dispatch is destructive | **added this wave** (unread until the operator fills the blank in the HCP UI) |
| 2 | **`raffa-demo` HCP VCS apply of the same PR 1 merge.** Queued at merge because both workspaces track `main` on `infra/`. | **Record applied / not applied here: `__________`.** Promotion precondition (clause 43). Blocks a `demo-v*` tag, not the `dev` walk. From this merge `demo` holds a Send grant nothing can target (`options: [dev]`) | **added this wave** (unread until the operator fills the blank in the HCP UI) |
| 3 | **Inherited PR #118 apply** — `worker_max_replicas` 3→5 on `dev` + `MaxConcurrentCalls=4`. Not written by w17. | **Record applied / not applied here: `__________`.** Does not gate A17-1…A17-5 / N16–N20. Throughput only. Cannot roll the running image (`ignore_changes` on both container apps) | **inherited from w16** (OQ-w17-dm-03, still unread from this checkout) |
| 4 | **`demo` promotion outcome.** `git tag -l "demo-v*"` **read 2026-09-16: `demo-v1`, `demo-v2`, `demo-v3`.** No `demo-v4`. w17 cuts no tag. | A walker must not infer a w17 promotion from the grant existing on `demo` | **added this wave** (tag **read**, not assumed) |
| 5 | **Invitation walk stays owed even if the promotion happens.** `invitation_mail_enabled`, `guest_provisioning_enabled` and `guest_role_assignment_managed` stay `false` on `demo`. w17 flips none. | Invitation mail / guest provisioning walks stay `dev`-only. Clearing the promotion backlog does not clear this walk | **inherited from w15** (restated this wave) |
| 6 | **W18 remainder of NW-63** — bounding-box overlay, `prebuilt-layout` + widened gateway contract / `AiOcrPage`, phrase-edit write path. w17 ships page image + text-level highlight. The affordance must not promise a box drawn on the page | Viewer citations land on the page; they do not draw geometry. Do not file that as a w17 regression | **added this wave** (OQ-w17-001 split, ratified) |
| 7 | **Termination and price uplift have no correctable field** (NW-64's bounded gap). An input that writes nowhere is worse than an absent row | N18's "Not found" section lists only fields with a PATCH target. Do not invent those two fields this wave | **added this wave** (W18 candidate) |
| 8 | **OQ-w17-cl-03 — signed-out viewer deep link.** The check belongs to `E22/F03/US01/T01` (`w17-viewer.spec.ts`). If the OIDC return URL drops `?page=`/`clause=`, the repair is **ADR-012's w14 auth-landing footer**, never the viewer and never a browser store. The "citation could not be restored" copy is owed unconditionally | A dropped query is not a viewer defect and is not repaired in this wave | **added this wave** (check recorded; repair deferred to the auth landing) |
| 9 | **w17 ships a page renderer and a bulk whole-tenant re-render trigger in the same wave** (ADR-005 w17 §24). The console is the largest concurrent render this product will have run. Storage has no lifecycle policy; a non-deterministic page key would permanently double the container | Sequence step 6 (20-file measurement, zero Worker restarts) **before** sequence step 7 (A17-S2). A bulk run is not a substitute | **added this wave** |
| 10 | **Postgres / Testcontainers suites** need Docker on the runner (`backend.yml`). | A `127.0.0.1:5432 refused` on a developer machine is a fixture gap, never a flake. CI is the gate | **inherited from w15** |
| 11 | **Playwright is runbook evidence, not CI** (ADR-012 §12, w17 clause 31). | `day1.spec.ts`, `v2.spec.ts`, `w17-savings.spec.ts`, `w17-answers.spec.ts`, `w17-review.spec.ts`, `w17-viewer.spec.ts` are walked by the operator. `invite.spec.ts` reports **skipped** with its passcode reason. No workflow runs Playwright | **inherited from w15** |
| 12 | **Verified-domain guest refusal** (`docs/waves/w15-acceptance.md` known-gaps). | Unchanged | **inherited from w15** |

**Closed this wave:** w16's known-gaps row that bulk whole-tenant reprocess is W17. NW-73
lands: `reprocess-tenant-documents.yml` is added, `Raffa.Tools` calls
`DocumentReprocessService.ReprocessAsync`, A17-S2 is the walk. A16-3's single-document
Admin path is unchanged.

**Closed this wave:** w16's money-fence row that realized KPI is a count, never money.
A17-S1 reads `RealizedSavings` grouped by currency under **Savings verified**.
