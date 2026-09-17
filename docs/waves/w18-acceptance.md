# Wave w18 "the viewer draws the box, the cited phrase is editable, and the lists/Ask/Quote surfaces stop dead-ending" — acceptance runbook (N17r, A18-p, A18-s, A17-S3, A18-o, A18-ops, A18-e2e, N10, N11, N12, N13, N14)

Operator checklist for wave `w18` (`.helix/reports/context/waves/w18-requirements.md`;
decision record `.helix/reports/architecture/waves/w18.md`; ADR-029 w18 footer, ADR-017 w18,
ADR-003 w18, ADR-027, ADR-024, ADR-012, ADR-018, ADR-019/020, ADR-002, ADR-014/016). One
section per acceptance item, each naming **the URL, the header posture and the expected
status code** plus the observable pass condition. Same shape as
[`w17-acceptance.md`](w17-acceptance.md).

| | |
|---|---|
| Owner | task `E23/F05/US01/T01` (`w18-integration`), story `us-01-w18-final-integration` |
| Oracles | `w18-requirements.md` items NW-63r, NW-23, NW-25, NW-74, NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60 · ADR-029 · ADR-017 w18 · ADR-003 w18 · ADR-027 · ADR-024 · ADR-012 · ADR-018 · ADR-019/020 · ADR-002 · ADR-014/016 |
| API contract | `web/openapi/raffa-api.v1.json` — every route below is quoted from it, **except** the phrase-edit `PATCH` (N17r): a real, tested backend route with no OpenAPI entry at all |
| Screens | `web/README.md` "Screens (ADR-024 V2 route map)" → `/documents/:documentId/viewer`, `/contracts` (Portfolio), `/savings`, `/ask`, `/ask/:id`, `/quotes` |
| Automated cover | `dotnet test backend/Raffa.slnx` (per project below, including Postgres/Testcontainers — real containers, confirmed working in this harness) · `cd web && npm ci && node node_modules/typescript/bin/tsc --noEmit -p .` (typecheck; no `npm run typecheck` script exists) · `npm test` (vitest) · `web/e2e/day1.spec.ts` (runbook, not CI) |

Run this against **`dev`**. Promotion to `demo` is a separate operator act *after* this walk
(ADR-016). w18 is **one** `integration → main` PR: it writes no Terraform
(`git diff --stat origin/main..HEAD -- infra` is one `infra/README.md` paragraph — ADR-017
w18's `prebuilt-layout` note, no `.tf` file changed) and no workflow
(`git diff --stat origin/main..HEAD -- .github/workflows` is empty).

The engine's `reports/execution/wave-close.md` is a fan-out delivery report, **not** the wave
record. It is never cited as current. This document is the wave record.

---

## 0. Before you start

### 0.1 What the wave is, in one paragraph

Four epics land in one wave. **Epic-23** finishes NW-63's w18 remainder: the AI Gateway now
calls `prebuilt-layout` beside `prebuilt-read` for per-word geometry, `ExtractionEvidence`
gained nullable box + override columns, `PATCH /api/contracts/{id}/evidence/{fieldName}`
writes a corrected phrase beside the model's proposal without touching it, and the viewer
gained a `BoxOverlay` component that draws a box over a cited phrase when one is on the row
— except **no code path in this wave ever writes to those four box columns**, so the box
never actually appears on `dev` today, and the phrase-edit endpoint has **no web affordance**
at all (see **N17r** below, both confirmed by reading the current source, not assumed). **Epic-24** gives Portfolio a
`?category=` filter (host-joined on `Supplier.Category`, ADR-002) and Savings a client-side
supplier/status/currency filter. **Epic-25** rounds out Ask and Quote check: admin-gated
capability chips hide from a non-Admin, a tenant citation card shows a real page preview and
deep-links into the viewer, a `?scope=` Ask entry briefs the named contract instead of the
generic hello, Quote check leads with a market-benchmark position and a durable history list,
every reachable abstain reply carries a secondary recovery action (with one confirmed,
precisely-diagnosed bug — see **N13** below), and the global Ask bar no longer duplicates the on-screen composer on `/ask`. **Epic-26**
closes the wave's own residuals: both NW-30 claims were already closed by wave w16 (a
verified NIL diff — nothing to sweep), and `day1.spec.ts` is reconciled to the V2 shell.

### 0.2 How the wave was built

Eighteen live tasks in five phases (`.helix/reports/plan/slices/w18.yaml`), fanned out by
Helix onto `integration`. One partial delivery is on record in the branch's own history, not
discovered by inference: `E23/F04/US01/T01` (viewer-box-and-edit)'s own commit message states
it shipped only the box-overlay renderer (AC-1) because `E23/F03/US01/T01` (phrase-edit-write)
had committed only its backend half and never wrote the OpenAPI entry / `schema.ts` /
`client.ts` wrapper its own task file assigned it — implementing that contract from the
dependent task would have duplicated single-writer scope (ADR-012 §3), so it did not. That gap
is still open at this checkout, re-confirmed below by grep, not merely re-quoted from the
commit. `E26/F01/US01/T01` (openapi-sweep) is on record as a verified NIL diff: both NW-30
claims were already closed by wave w16's `E18/F02/US02/T01`, so there was nothing to sweep.
This checkout is task `E23/F05/US01/T01` itself, forked from `integration` at `86cc57d`
(merge of `wave/E23-F04-US01-T01`, the last of the eighteen to land). `origin/main` was
re-fetched from this checkout on 2026-09-17: `f9899004a3a9e470eec1972c0700824ae8039eaf`
(PR #143, w17's own merge, both `backend.yml`/`web.yml` green on that push). Nothing in this
document relies on the fan-out engine's own delivery claims — every fact below was re-read
from this checkout on 2026-09-17, including two genuine, reproducible defects this task found
while proving the wave (see **N13** below and [Known gaps](#known-gaps-that-shape-acceptance-today)
row 4) and one flake that did not reproduce (row 6).

### 0.3 The operator sequence

| # | What | How |
|---|---|---|
| 1 | Confirm `origin/main` has not moved past `f989900` | `git fetch origin main`. If it moved, merge and re-check before anything else |
| 2 | Merge the wave PR to `main` | `dev` deploys on push (`backend.yml`, `web.yml`); the image tag is the merged sha |
| 3 | One interactive sign-in on deployed `dev` | `GET /api/workspaces` is `200` |
| 4 | Walk N17r, then A18-p, A18-s, A17-S3, A18-o (desk check), N10, N11, N12, N13, N14 | this document |
| 5 | A18-ops — NW-40 confirm, then the NW-41 walk | this document; NW-41 may need `seed-market-intelligence.yml` dispatched first — read that section before assuming a `404` is a defect |
| 6 | A18-e2e — `day1.spec.ts` | operator's own Playwright harness, credentials required; never CI |

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
# TENANT: workspace id (GET $API/api/workspaces -> workspaces[].id)
# CONTRACT, DOCUMENT, QUOTE, CONVERSATION: ids from the screens you walk
```

**Header posture.** Every tenant-scoped route below sends `Authorization: Bearer $TOKEN` and
`X-Tenant-Id: $TENANT`. **Never** `X-Role`, `X-Workspace-Role` or `X-User-Id`. Identity is the
bearer `oid`. `GET /api/capabilities` and `GET /api/market/records/{id}` take **no** tenant
header.

---

## N17r — the viewer draws a box over the cited phrase, and the phrase is editable (NW-63r)

> NW-63r · ADR-029 (w17 split + w18 footer) · ADR-017 w18 · ADR-003 w18 · ADR-027 · ADR-012 §3
> tasks `E23/F01/US01/T01` (gateway wire) · `E23/F02/US01/T01` (schema + read) ·
> `E23/F03/US01/T01` (write endpoint) · `E23/F04/US01/T01` (viewer overlay)

**Read this before walking it — what actually ships vs. what does not.** Four pieces were
planned. Three of the four backend/plumbing pieces are real and independently tested; the box
itself never appears on `dev`, and the phrase-edit write has no way to be reached from the
product.

1. **OCR wire carries geometry.** `Raffa.AiGateway.Foundry.FoundryOcrClient` calls
   `prebuilt-layout` beside `prebuilt-read`; `DocumentIntelligencePage`/`AiOcrPage` carry
   `Words`/`Polygon`. Real, tested (`Raffa.AiGateway.Tests`, **156/156 green** this checkout).
2. **Evidence schema carries geometry + override.** `ExtractionEvidence` gained
   `BoxX`/`BoxY`/`BoxWidth`/`BoxHeight` (nullable, all four together or not at all) and
   `OverrideValue`; `GET /api/contracts/{id}/evidence` emits `box` beside the existing fields.
   Real, tested (`Raffa.Documents.Contracts.Tests`, **228/228 green**; `ContractEvidenceSchemaTests`).
3. **Phrase-edit write.** `PATCH /api/contracts/{id}/evidence/{fieldName}` writes
   `OverrideValue` beside `Value` (never mutating the proposal), audits `contract.phrase_edited`,
   and a reprocess cannot silently revert it (`ContractPhraseEditService.EditAsync`). Real,
   tested (`Raffa.Api.Tests`).
4. **Viewer box overlay.** `web/src/routes/documents/viewer/BoxOverlay.tsx` draws one
   positioned `<div>` per cited phrase on the page whose evidence carries a non-null `box`,
   scaled to the rendered image; a `null` box degrades to the existing `ClauseHighlight`
   text-level highlight. Real, tested (`BoxOverlay.test.tsx`, `DocumentViewerRoute.test.tsx`).

**What is missing, confirmed by reading the current source, not assumed:**

- **Nothing writes a box.** `Raffa.Documents.Contracts.Application.Extraction.StagedExtractionService`
  — the only place `ExtractionEvidence` rows are created during a real extraction — has **zero**
  references anywhere to `Raffa.AiGateway.Contracts.AiOcrWord`/geometry (grepped, this
  checkout). The gateway wire (#1) and the schema (#2) both landed, but no task in this wave
  connects them. `GET /api/contracts/{id}/evidence`'s `box` reads `null` for every field, on
  every contract, **indefinitely** — not "until epic-23 feature-04 ships" as `backend/README.md`
  said before this task corrected it (feature-04 was the *web viewer* task; it was never going
  to populate geometry, and it did not). Item 4's overlay is real, but with nothing ever
  supplying a non-null `box`, it has nothing to draw on `dev` today.
- **The phrase-edit write has no product affordance.** `web/openapi/raffa-api.v1.json`,
  `web/src/api/generated/schema.ts` and `web/src/api/client.ts` carry **zero** references to
  `evidence/{fieldName}` (grepped, zero hits in all three, this checkout). `EvidencePane.tsx`
  (`web/src/routes/contracts/review/`) has no phrase-edit input, button or call anywhere. Item
  3's endpoint is real and independently provable by `curl`/`psql` below; nothing shipped in
  the product can reach it.

**Click path (what you can actually observe today).** Open `WEB/documents/<id>/viewer` on a
validated document. The page renders exactly as w17 shipped it — the page image plus the
existing text-level highlight when a `?clause=` citation lands on the page. **No box appears
anywhere, on any document**, because nothing wrote one. This is the correct, honest state of
this checkout — do not re-open it as a viewer defect.

**Pass when (structural, not visual, given the above):**

1. `GET /api/contracts/{id}/evidence` returns `200` with a `box` member (possibly `null`) on
   every field row — the shape landed even though every value is null today.
2. The viewer renders with no error and no broken layer when `box` is null on every field
   (i.e. today, always) — confirmed by `BoxOverlay.test.tsx`'s own null-box case and by
   walking any validated document.
3. The phrase-edit endpoint, called directly, writes an override beside the proposal and a
   second read agrees — the only way to exercise the parent story's AC-1–AC-4 on `dev` today.

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT/evidence" \
  | jq '.fields[] | {fieldName, box}'
# every "box" is null today -- expected, not a bug, until a future task wires extraction to write it

FIELD=supplier   # any fieldName the GET above already lists

curl -s -o /dev/null -w '%{http_code}' -X PATCH \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"overrideValue":"Northwind Traders SA (corrected)"}' \
  "$API/api/contracts/$CONTRACT/evidence/$FIELD"
# 200

curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT/evidence" \
  | jq --arg f "$FIELD" '.fields[] | select(.fieldName==$f) | {fieldName, value, overrideValue}'
# value is unchanged (the model's original proposal); overrideValue is what you just wrote --
# proposal and override both present and distinguishable, never one silently replacing the other
```

**psql** (proves the write landed on the same row as the proposal, not a new one, and the box
columns travel untouched):

```sql
SET app.tenant_id = '<tenant>';
SELECT field_name, value, override_value, box_x, box_y, box_width, box_height
FROM extraction_evidence
WHERE contract_id = '<contract>' AND field_name = '<field>'
ORDER BY created_at DESC LIMIT 1;
-- override_value is what the curl above wrote; value is untouched; box_* are null (today, always)
```

**Automated:** `Raffa.AiGateway.Tests` (`FoundryOcrClient`/`MapPages` geometry cases,
156/156); `ContractEvidenceSchemaTests`, `ContractEvidenceQueryServiceTests`
(`Raffa.Documents.Contracts.Tests`, 228/228); the phrase-edit write + reprocess-preserves
cases in `Raffa.Api.Tests`; `web/tests/routes/documents/viewer/BoxOverlay.test.tsx`,
`DocumentViewerRoute.test.tsx`.

**Known gap, do not re-open as a new defect:** the geometry write (staged extraction →
`ExtractionEvidence.BoxX/Y/Width/Height`) and the phrase-edit web affordance (`client.ts`
wrapper + `EvidencePane.tsx` UI) are the two pieces a future wave must add before N17r's
box/edit promise is observable end to end on `dev`. See
[Known gaps](#known-gaps-that-shape-acceptance-today) rows 1–2.

---

## A18-p — Portfolio has a category filter (NW-23)

> NW-23 · ADR-002 (host composition) · ADR-020 (control) · tasks `E24/F01/US01/T01` (backend),
> `E24/F01/US02/T01` (web)

**Click path.** Open `WEB/contracts` (Portfolio). Under the header, a filter form
(`aria-label="Filter portfolio by supplier category"`) holds a text input labelled
**Category** (placeholder "Supplier category"), an **Apply** button and — once a category is
applied — a **Clear filter** button. Type a real supplier category and Apply: the table
narrows to matching rows. Type a category no supplier carries: the table shows zero rows,
the filter form stays visible and clearable. Click **Clear filter**: the full portfolio
returns.

**Pass when:**

1. `?category=` is a real query parameter on the request, never a client store.
2. A category no supplier carries narrows to an empty list — never a fabricated one, never a
   `500`.
3. Blank/absent `category` is the full tenant portfolio.
4. The "Contract" column's document-type label is unaffected — this is a supplier-category
   filter, not a document-type rename; §6's "Type" word still names the document type.

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts?category=SaaS" | jq '{totalCount, items: [.items[].supplierName]}'

curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts?category=NoSuchCategoryAnywhere"
# 200 -- { items: [], totalCount: 0 }, never an error
```

**Automated:** `PortfolioFilterTests.cs` (`Raffa.Documents.Contracts.Tests`);
`PortfolioEndpointTests.cs` (`Raffa.Api.Tests`); `PortfolioFilterControl.test.tsx`,
`PortfolioRoute.test.tsx`, `portfolioViewModel.test.ts`. **Test-coverage gap, not a
functional one:** `SupplierCategoryLookup` (the real EF-backed join) has no dedicated unit
test — every existing test exercises it through a stub; worth one extra manual check on `dev`
the first time this walk runs.

---

## A18-s — Savings opportunities are filterable (NW-25)

> NW-25 · ADR-020 · task `E24/F02/US01/T01` (web only — `GET /api/savings` takes no new
> parameter)

**Click path.** Open `WEB/savings` with at least one opportunity on the list. Above the
table, a filter bar (`role="group"`, "Filter opportunities") holds three selects —
**Supplier** ("All suppliers"), **Status** ("All statuses": Identified / In progress /
Realized), **Currency** ("All currencies") — populated only from suppliers/currencies
actually present in the loaded rows, plus a **Clear filters** button (disabled until a
filter is active). Pick any one: the table narrows instantly, no network call. Pick a
combination that matches nothing: the table is replaced by **No opportunities match the
selected filters** / "Clear a filter above to see the full list." Click **Clear filters**:
every row returns. "Estimate" stays a plain column, not a filter.

**Pass when:** the three filters AND-compose, clearing restores the full list, and no fetch
fires when a filter changes (open the network tab: filtering an already-loaded list is
silent — `GET /api/savings` is called once, on load).

**Automated:** `web/src/routes/savings/savingsFilters.test.ts` (thorough — matches-all, each
dimension alone, AND-composition, empties-never-falls-back, option derivation);
`savingsViewModel.test.ts`; `SavingsRoute.test.tsx`. **Test-coverage gap:**
`SavingsRoute.test.tsx` has no test that renders the route and actually drives the
Supplier/Status/Currency selects, or asserts the empty-state copy — everything interactive is
proven only by this runbook's own click path today, not by an automated DOM test.

---

## A17-S3 — admin-gated Ask chips hide from a non-Admin (NW-74)

> NW-74 · ADR-022 S16-11 · ADR-012 w17 cl 40 · task `E25/F01/US01/T01`

**Click path.** Sign in as a live **Procurement** (non-Admin) member. Open a screen whose
capability catalog entry is admin-gated (e.g. `WEB/workspace/members`) and look at the
global Ask bar's suggestion chips: only chips whose catalog entry is `roleGate: "any"`
render — the generic pair, never an admin-gated question. Sign in as **Admin** on the same
screen: the admin-gated chips appear. In both cases `GET /api/capabilities` itself is
byte-identical — this is presentation only, never a security boundary.

**Pass when:**

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$API/api/capabilities" | jq 'length'
curl -s "$API/api/capabilities" | jq 'length'
# identical body, no token vs a token -- the catalog is un-gated by rule
```

and the two signed-in walks above show the chip difference.

**Automated:** `askSuggestions.test.ts` (the actual role-gate predicate,
`suggestionsFromCapabilityCatalog`); `GlobalAskBar.test.tsx` (role flows through as a prop);
backend `CapabilitiesEndpointTests.Spoofed_role_headers_do_not_change_the_response`.
**Known gap, harmless today:** the Ask screen's own new-chat chips
(`askViewModel.ts#suggestionsFor`, used by `ask/index.tsx`) are never actually called with a
real `role` anywhere in the code — inert only because the `ask` capability itself is
catalog-hardcoded `roleGate: "any"`; untested for what happens the day a non-`any` capability
reaches that same code path.

---

## A18-o — OpenAPI / `client.ts` stale-prose sweep (NW-30)

> NW-30 · ADR-012 · ADR-026 · task `E26/F01/US01/T01`

**Not a click path — a documentation-correctness desk check**, already performed and
recorded by the task itself, re-audited here. Both halves of NW-30 were closed by wave w16
before this task ever ran:

1. `GET /api/audit` is fully documented (`web/openapi/raffa-api.v1.json:6576-6661`, response
   ladder `200`/`400`/`403`/`404`) — the route is real (`Program.cs`
   `app.MapAuditEndpoints()`), and the document's own provenance paragraph says so.
2. The conversations `requestBody` gap is recorded as a documented generator limitation (the
   generator only parses `responses`; bodies are hand-written in `client.ts`, confirmed
   wire-compatible against the real backend records this checkout).

`git diff --stat` on `web/openapi/raffa-api.v1.json` / `web/src/api/client.ts` for that
task's own branch is empty — a verified NIL diff, not an unattempted check. **Re-confirmed
this task:** `node scripts/generate-api-client.mjs` was re-run against the current
`raffa-api.v1.json` and produces a `schema.ts` **byte-identical** to the committed one
(34,010 bytes both sides; the only `git status` flag afterward was a Windows CRLF-normalization
marker, reverted, no content diff) — the contract and the generated client are in sync.

**Pass when:** `dotnet build backend/Raffa.slnx` exits `0` (sanity — no code was touched by
that task) and a case-insensitive grep of both files for stale "audit absent"/"no
requestBody" phrasing returns zero hits naming audit or requestBody specifically.

**Automated:** none new — a prose/documentation check, not a code path.

---

## A18-ops — NW-40 (HCP env + Foundry RBAC) and NW-41 (`market_record` served)

> NW-40, NW-41 · ADR-005 · ADR-016 · ADR-024 · confirm-only, no Terraform change in this wave

### NW-40 — confirm the HCP apply landed the Container App env vars + Foundry RBAC, on `dev` **and** `demo`

An operator walk, not a task with a `depends_on` — nothing here was written by w18; the step
is to **confirm**, not to deploy.

| Check | Where | Pass |
|---|---|---|
| `ConnectionStrings__Storage` present, non-blank, `secretRef: st-cs` | Azure Portal → `ca-raffa-<env>-api` and `ca-raffa-<env>-worker` → Container → Environment variables | present on both apps, both envs |
| `AiGateway__Endpoint`, `AiGateway__ProjectName`, `AiGateway__DocumentIntelligenceConnection`, `AiGateway__Models__*__ModelId`/`ModelVersion` | same, on `ca-raffa-<env>-api` and `-worker` | non-blank on `dev` (`ai_gateway_wired` defaults `true` there); **may be legitimately blank on `demo`** unless that workspace's variable was explicitly flipped — confirm the actual HCP value, the Terraform default alone is not evidence either way |
| `AzureAd__Authority`, `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__Audience` | `ca-raffa-<env>-api` only (the worker does not need them) | all four non-blank, both envs |
| Foundry RBAC: `Cognitive Services User` + `Cognitive Services OpenAI User` on `id-raffa-<env>-workload` | Azure Portal → `aisvc-raffa` (`rg-raffa-ai`) → Access control (IAM) | both roles held by the workload identity, both envs |

Record applied/not-applied for each row here: `__________`. None of these block the walks
above — they are the reason those walks would `401`/`500` with a config-shaped error if
unset, not a gate this document enforces itself.

### NW-41 — a seeded `market_record` row is served by `GET /api/market/records/{id}`

**The code side is landed:** `market_record`/`market_embedding` tables
(`backend/src/Raffa.Market/Migrations/Scripts/market.sql`), `MarketRecordQueryService`
registered via `AddMarketModule` (`Program.cs`), `GET /api/market/records/{id}` mapped
(`MarketEndpointExtensions.cs`). **The residual is the walk, plus a seed precondition this
runbook must call out:** `backend/scripts/demo-fixture-seed.sql` does **not** insert any
`market_record` row — that corpus is populated only by a separate, manual
`workflow_dispatch` job, `.github/workflows/seed-market-intelligence.yml` (choice
`dev`/`demo`, never triggered by push/PR), which ingests
`backend/fixtures/market-intelligence.mock.json` (≥60 records) and verifies the row/embedding
counts itself.

**Walk:**

1. Confirm `seed-market-intelligence.yml` has been dispatched for the target environment
   (Actions tab, or ask whoever last ran it). If it never ran, dispatch it now against `dev`
   (or `demo`) before continuing.
2.

```bash
curl -s -o /dev/null -w '%{http_code}' "$API/api/market/records/MKT-SFDC-US-01"
# 200

curl -s "$API/api/market/records/MKT-SFDC-US-01" | jq '.recordId'
# "MKT-SFDC-US-01"
```

(Any of `MKT-SFDC-US-01`, `MKT-SFDC-CH-01`, `MKT-SFDC-EU-01`, `MKT-MSFT-US-01`,
`MKT-AWS-US-01` from the fixture file works.) A `404` here means the seed job has not run
against this environment yet — dispatch it and retry; it is not a code defect.

**Automated:** `MarketRecordQueryServiceTests`, `MarketMigrationScriptTests`
(`Raffa.Market.Tests`); no `Raffa.Api.Tests` endpoint test walks this specific route today
(it predates w18 — task E13/F06/US01/T01) — the curl above is the only proof on a real
environment.

---

## A18-e2e — `day1.spec.ts` reconciled to the V2 shell (NW-50)

> NW-50 · ADR-016 (runbook spec, never CI) · ADR-012 · task `E26/F02/US01/T01`

**Not a click path — an operator-run Playwright walk**, `web/e2e/day1.spec.ts`, one
`test.describe` / one `test` carrying 15 named `test.step`s (sign in → workspace resolve →
hintless-reload lands on `/ask` → invite → upload → review → Contract 360 → Ask → renewal →
savings → quote check → savings again). Confirmed reconciled to the V2 shell by reading the
file end to end: Ask is asserted as home (`toHaveURL(/\/ask$/)`), no pre-V2 "Home" KPI
assertion survives, and the Savings KPI assertion (`assertSavingsKpis`) checks the real,
current **four** cells — **Contracts analyzed · Upcoming renewals · Savings identified ·
Savings verified** — matching `savingsViewModel.ts#buildKpiCells` exactly.

**Run it:**

```bash
cd web
RAFFA_E2E_BASE_URL=https://<demo-host> \
RAFFA_E2E_ENTRA_EMAIL=<test account> \
RAFFA_E2E_ENTRA_PASSWORD=<test account password> \
npx playwright test e2e/day1.spec.ts
```

Without those three env vars the one test reports **skipped**, with a message naming exactly
which var is missing and pointing at `web/README.md`'s own "End-to-end" section — a declared
prerequisite gate, confirmed to be the **only** `test.skip` in the file, not a silent
smoke-disguising one.

**Pass when:** the walk completes with zero unexplained failures, and every KPI cell name it
asserts still exists on the deployed Savings screen (four cells, as above).

**Automated:** none — by design (ADR-016; `grep -i playwright .github/workflows/*.yml` is
empty, confirmed this checkout, 11 workflow files). This spec is never a merge gate.

---

## N10 — Ask citation cards: a real preview or a CTA, never a placeholder (NW-55)

> NW-55 · ADR-024 · ADR-012 w14 cl 1 · ADR-018 w17 cl 9 · tasks `E25/F02/US01/T01`
> (backend), `E25/F02/US02/T01` (web)

**Click path.** Ask a question that resolves to a validated contract's clause or fact (e.g.
"What is our liability cap with `<supplier>`?"). The reply's citation card `[1]` shows a real
first-page thumbnail image; click it — the document viewer opens straight on the cited page
(`/documents/<id>/viewer?page=<n>&clause=<id>`). Ask a market-comparison or
general-capability question instead: its citation card shows **no image** — instead a `View
source →` button-styled CTA renders, never the old "No page preview available" text.

**Pass when:**

1. A tenant clause/fact citation whose evidence resolved a source page carries `previewUrl`
   (`/api/documents/{id}/preview?page=<n>`) and `href`
   (`/documents/<id>/viewer?page=<n>&clause=<id>`).
2. A `raffa`/`market` citation carries `previewUrl: null` and renders the CTA card, never a
   blank/placeholder slot.
3. A tenant fact with no resolved page still degrades to its bare `/contracts/<id>` href,
   `previewUrl: null` — honestly, not an error.

```bash
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"question":"What is our liability cap with <supplier>?"}' \
  "$API/api/conversations/$CONVERSATION/messages" \
  | jq '.citations[] | {corpus, previewUrl, href}'
# a tenant clause citation: previewUrl set, href = /documents/.../viewer?page=&clause=
# a market/raffa citation: previewUrl null, href its own CTA route
```

**Automated:** `AskCopilotServiceTests` (`Raffa.Api.Tests`) — clause-with-page,
no-clause-resolved, page-less-clause, raffa/market-null-preview cases; `CitationCard.test.tsx`
(image vs CTA rendering; "No page preview available" gone for good).

---

## N11 — the scoped Ask entry briefs the contract, never the generic hello (NW-56)

> NW-56 · ADR-024 · ADR-020 · tasks `E25/F03/US01/T01` (backend), `E25/F03/US02/T01` (web)

**Click path.** Open a validated Contract 360, click **Ask about it**. The new-chat screen
must show a kicker naming the supplier, the heading **Ask about {Supplier}**, and the line
**Answers cite this contract's pages.** — never the generic "What do you want to know?"
hello — plus two supplier-templated chips ("When must we give notice to {supplier}?", "What
is our liability cap with {supplier}?"). Ask a question that names no supplier itself; the
answer must still be about that one contract.

**Pass when:** the scoped brief renders instead of the generic hello, the chips are
supplier-templated, and a scoped question never falls through to the generic
`NeedsDocument` redirect. A scoped link into a tenant with **zero** validated contracts still
renders the ordinary off-state — never a briefed-but-off screen (by design).

```bash
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d "{\"scopeContractId\":\"$CONTRACT\"}" "$API/api/conversations" | jq '{id, scopeContractId}'

curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"question":"When must we give notice?"}' \
  "$API/api/conversations/$CONVERSATION/messages" | jq '.answerMarkdown'
# answers about the scoped contract's own supplier, not a portfolio-wide aggregate
```

**Automated:** `ScopedAskEndpointTests`, `ConversationsEndpointTests` (`Raffa.Api.Tests`);
`ConversationServiceTests` (`Raffa.Chat.Tests`, 221/221 green overall); `askViewModel.test.ts`
(`buildScopedBrief`, 5 cases); `AskRoute.test.tsx` (scope param reaches
`createConversation`). **Test-coverage gap:** no test renders `AskRoute` at `/ask?scope=` and
asserts the brief actually appears in the DOM — the render wiring is real (traced by hand,
`index.tsx:382-398`) but only unit-tested in isolation; walk the click path above on `dev`
rather than trust automated cover alone for this one.

---

## N12 — Quote check leads with a market benchmark and keeps a durable history (NW-57)

> NW-57 · ADR-024 · ADR-028 · ADR-001 (fixture-adapter fence) · tasks `E25/F04/US01/T01`
> (backend), `E25/F04/US02/T01` (web)

**Click path.** Open `WEB/quotes`. Below the upload form, a **Quote check history** table
lists every quote this workspace has ever checked, newest first, each with a position tag
(or an outline "First of its kind" tag for a cold start) — read back from the server, not a
client store. Upload a genuinely new supplier/product combination: the Assessment band shows
the honest cold-start sentence ("First of its kind in this workspace — Raffa.ai has no
market comparable yet for this quote…") — never a fabricated P25/P50/P75. Re-open a past
quote from the history table: its position recomputes live. **See it in Savings →** stays a
secondary action below the benchmark result — it never replaces it.

**Pass when:**

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/quotes/benchmark-history" | jq '.items | length'
# 200 always, even 0 items for a brand-new tenant -- never 404

curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/quotes/benchmark-history" | jq '[.items[].lines[].status] | unique'
# "InsufficientBenchmarkData" (PascalCase wire literal) appears for a first-of-type line;
# never a fabricated position where that literal should be
```

No adapter here ever calls a paid market-data API — `FixtureBenchmarkAdapter` is the only
adapter wired, confirmed by reading it (no `HttpClient` anywhere in the file).

**Automated:** `QuoteBenchmarkHistoryEndpointTests` (`Raffa.Api.Tests`);
`MarketAssessmentServiceTests`, `MarketAssessmentCalculatorTests` (`Raffa.Quotes.Tests`,
180/180 green); `AssessmentResult.test.tsx`, `QuoteHistoryList.test.tsx`,
`quotes/index.test.tsx`.

---

## N13 — Ask never dead-ends: every abstain carries a recovery action (NW-59)

> NW-59 · ADR-024 · ADR-020 · tasks `E25/F05/US01/T01` (backend), `E25/F05/US02/T01` (web)

**Click path.** Ask a question with no grounded answer in any validated contract. The reply
shows the accent-left **Cannot determine reliably.** block plus, as a **secondary** button
below it (never the primary CTA), a recovery action — **Upload a contract**-style action for
a tenant with zero validated contracts, or an Ask-capability hint action for a tenant that
has contracts. The block must never render with nothing actionable beneath it when a real
action exists.

```bash
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"question":"asdkjhasdkjh nonsense no contract could ever answer"}' \
  "$API/api/chat/query" | jq '{kind, actions}'
# kind: "abstain", actions: a non-empty array, a real catalog href
```

**Automated:** `CopilotReplyBuilderTests` (`Raffa.Chat.Tests`, 221/221 green — the
guard-downgraded abstain path is fully green here); `web/tests/routes/ask/reply/ReplyBody.test.tsx`
(secondary-never-primary; actionless abstain still renders the block); `askViewModel.test.ts`
(abstain actions map the same way every other reply kind's actions do).

**Confirmed failing test — reproduced, root-caused, not a flake.**
`backend/tests/Raffa.Api.Tests/AskAbstainRecoveryActionTests.Empty_pack_abstain_for_a_contract_free_tenant_offers_the_documents_upload_action`
fails on this checkout:

```
Assert.Equal() Failure: Strings differ
Expected: "upload"
Actual:   "navigate"
```

**Root cause**, read directly in `backend/src/Raffa.Api/AskCopilotService.cs`'s
`ResolveAbstainRecoveryActions`: the zero-validated-contracts branch calls
`CapabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)], ...)`,
and `CapabilityRouting.ResolveOne`'s generic `HowTo` case always resolves through `ForKey`,
which returns `CopilotActionKind.Navigate` (label "Open Documents") for any capability that
is not itself gated on `NeedsValidatedContract` — Documents is not (it is the very capability
that creates a validated contract, so gating it on having one would be circular). The
`Upload` kind + "Upload in Documents"/"Upload a contract" label this test (and
`ResolveAbstainRecoveryActions`'s own doc comment) both expect only comes from
`CapabilityIntent.UnknownSupplier` (→ `UploadInDocuments()`) or from the *replacement* path
`ForKey` takes for a **different**, `NeedsValidatedContract`-gated capability at zero
contracts. **Practical effect on `dev`:** a contract-free tenant's abstain-recovery button is
labelled "Open Documents" with `kind: "navigate"` instead of "Upload a contract"/"Upload in
Documents" with `kind: "upload"` — the `href` still correctly points at `/documents`, so the
link itself is not broken, only its kind/label. **Not fixed by this task** — `AskCopilotService.cs`
is outside this task's own `## Files to create or modify` (a README sweep only); the one-line
fix (swap the `HowTo(DocumentsKey)` intent for `CapabilityIntent.UnknownSupplier` on that one
branch) is recorded here, precisely, for whoever picks this gap up next. The other two
abstain paths (composer-failure, and the guard-downgraded path `Raffa.Chat.Tests` covers) are
unaffected and green.

---

## N14 — the global Ask bar is suppressed on Ask screens (NW-60)

> NW-60 · ADR-018 · ADR-020 · task `E25/F06/US01/T01`

**Click path.** Navigate to `/ask` or resume a conversation at `/ask/<id>` — only the
screen's own composer input is present, no second search bar above it. Navigate anywhere
else (Documents, Portfolio, Renewals, Workspace…): the global Ask bar returns. Press Ctrl+K
(Cmd+K on Mac) on a non-Ask screen: the global bar's input focuses. Press it on `/ask`: the
screen's own composer focuses instead.

**Pass when:** `isAskRoute(pathname)` suppresses exactly `pathname === "/ask"` and
`pathname.startsWith("/ask/")` — `/askew` or any other route still shows the bar (confirmed
by the test's own `/askew` near-miss case, and by proof of a true unmount, not a CSS hide:
`apiClient.getCapabilities` is never called on the suppressed routes).

**Automated:** `AppShell.test.tsx` (`describe("isAskRoute")`, 4 cases including the
`/askew` near-miss; `describe("AppShell global Ask bar suppression")`, AC-1 + AC-3).
**Test-coverage gap:** the Ctrl/Cmd+K → composer-focus behaviour on `/ask` itself
(`ask/index.tsx`) has no automated test anywhere (only the global bar's own shortcut is
tested) — walk it by hand on `dev`.

---

## Promotion sequence

w18 writes no Terraform (`git diff --stat origin/main..HEAD -- infra` is one
`infra/README.md` paragraph) and no workflow (`.github/workflows` diff is empty). It is
**one** `integration → main` PR. `git tag -l "demo-v*"` **read 2026-09-17 on this checkout:
`demo-v1`, `demo-v2`, `demo-v3`.** No `demo-v4` — w18 cuts no tag of its own; a `demo`
promotion, if the operator chooses it, is a separate act after this walk (ADR-016), gated by
NW-40's Foundry/env confirms on `demo` specifically, never inferred from `dev`'s own state.

---

## Known gaps that shape acceptance today

Recorded as of this checkout, 2026-09-17. Facts, not assumptions — every row below was
produced by reading the current source or running the current suite, this task, not copied
forward from an earlier wave document unless marked "inherited."

| # | Gap | Effect on acceptance | Disposition |
|---|---|---|---|
| 1 | **NW-63r geometry is never written.** OCR wire (feature-01) and schema (feature-02) both landed; nothing in `StagedExtractionService` (or anywhere else) maps `AiOcrPage` geometry onto `ExtractionEvidence.BoxX/Y/Width/Height`. | N17r's box never renders on `dev` — `box` is `null` on every field, on every contract, always. The viewer's own fallback (text highlight) is what a walker will actually see. | **added this wave** (found by grep; `backend/README.md`'s own prior claim — that feature-04 would close this — was wrong and is corrected by this task) |
| 2 | **NW-63r phrase-edit has no web affordance.** `E23/F03/US01/T01` committed only the backend endpoint; the OpenAPI entry, `schema.ts` type and `client.ts` wrapper it owned were never written, so `E23/F04/US01/T01` (dependent, single-writer-bound) could not wire the Review UI to it either. | The endpoint is real, tested, and reachable only by direct API call — no reviewer can correct a phrase from the product today. | **added this wave** (named on the dependent task's own commit message; re-confirmed here by grep across all three web contract files) |
| 3 | **`AskAbstainRecoveryActionTests.Empty_pack_abstain_for_a_contract_free_tenant_offers_the_documents_upload_action` fails**, reproducibly. Wrong `CapabilityIntent` used for the zero-contract abstain-recovery action — see N13 for the exact root cause and fix. | `dotnet test backend/tests/Raffa.Api.Tests` is 266 passed / **1 failed** / 2 skipped / 269 total, not a clean `0`. The recovery link still lands on `/documents`; only its `kind`/label are wrong. | **added this wave**, root cause identified, one-line fix recorded in N13, not applied (outside this task's own file scope) |
| 4 | **`tsc --noEmit` fails with 18 errors across 14 files** (no `npm run typecheck` script exists — ran the compiler directly, same as task `E26/F01/US01/T01` did). Two independent, pre-existing causes: (a) `getQuoteBenchmarkHistory` missing from 13 test files' own inline `ApiClient` mock literals (the interface grew this member in `E25/F04/US01/T01`; unrelated test files were never updated to match); (b) the evidence `box` member missing/optional in 5 Contract360/Review/Documents test fixtures (grew in `E23/F02/US01/T01`). Neither is new to this task — `E23/F04/US01/T01`'s own commit message already named both, unresolved, before this task ran. | `npm test` (vitest) does **not** type-check by default and is unaffected — **1085/1085 pass, 63/63 files**. Any future CI step that runs a real `tsc --noEmit` (or a stricter `npm run build`) would fail on this today. | **inherited, confirmed still open** — mechanical, additive-only fixes (stub the new method in each mock, add `box: null` to each fixture) are recorded here for a follow-up task; not applied by this task (touches ~18 files outside its own scope) |
| 5 | **No lint tooling exists in `web/` at all** — no `eslint` dependency, no config file, no `npm run lint` script. Not new to w18. | The parent story's own Definition of Done names `npm run lint`; there is nothing to run. `tsc --noEmit` is the only static check this repo has today (see row 4 for its current state). | **inherited** (re-confirmed this wave; `E23/F04/US01/T01` already recorded the same finding) |
| 6 | **One flaky vitest failure observed, not reproducible.** A full `npm test` run failed `ReviewRoute.test.tsx`'s "filling a missing row…" case on a `waitFor` timeout; the identical test filtered in isolation, and two subsequent full-suite runs, all passed cleanly (1085/1085 each time). | Treat a single red `ReviewRoute.test.tsx` result as suite-level flake (cross-file state bleed is the likely cause — nothing in that file itself uses fake timers), not a defect in the missing-field correction flow. Re-run once before escalating, exactly as it was re-run here. | **added this wave**, recorded with the actual evidence (1 fail, then isolated-pass, then 1085-clean) rather than asserted away |
| 7 | **NW-41's `market_record` corpus needs a manual seed job.** `demo-fixture-seed.sql` inserts none; `seed-market-intelligence.yml` (`workflow_dispatch` only) is the real seed path and is not wired into any deploy/promotion trigger. | A freshly-provisioned or never-seeded `dev`/`demo` 404s on every `GET /api/market/records/{id}` regardless of how correctly everything else is wired. | **added this wave** (the A18-ops walk names the dispatch as a precondition, with sample seeded ids) |
| 8 | **NW-40's `AiGateway__*` publication is conditional** on `ai_gateway_wired` (dev default `true`, demo default `false`), overridable per HCP workspace. | A blank `AiGateway__*` set on `demo` is not automatically a defect — confirm the actual HCP value; the Terraform default alone is not evidence either way. | **added this wave** |
| 9 | Minor, non-blocking test-coverage gaps surfaced while auditing this wave: `SupplierCategoryLookup` (A18-p) untested directly (stub-only elsewhere); `SavingsRoute.test.tsx` (A18-s) never drives the real filter controls; `askViewModel.ts#suggestionsFor` (A17-S3) never receives a real `role` (harmless — `ask` capability is catalog `roleGate: "any"`); Ctrl/Cmd+K on the Ask composer (N14) untested; the scoped-brief DOM render (N11) only unit-tested in isolation. | None of these are functional defects — each click path above still covers the real behaviour by hand. Listed so a follow-up task can close the automated-coverage gap without re-discovering it from scratch. | **added this wave** |
| 10 | **Postgres/Testcontainers suites ran green in this harness** (`Raffa.Api.Tests`, `Raffa.Documents.Contracts.Tests`, `Raffa.Chat.Tests`, `Raffa.Quotes.Tests` all pulled real containers and passed) — a `127.0.0.1:5432 refused`/Docker-unavailable result on a *different* developer machine is still a local-environment fact, never something to explain away as a flake; CI is the gate. | No effect on this checkout's own proof. Recorded so the next reader does not assume Docker is universally available everywhere this document is read. | **inherited from w15/w16/w17** |
| 11 | **Playwright is runbook evidence, not CI** (ADR-012 §12). `day1.spec.ts` (A18-e2e) is the operator's own walk; no workflow runs it. | Unchanged from every prior wave. | **inherited** |
| 12 | Invitation-mail / guest-provisioning flags stay `false` on `demo`; verified-domain guest refusal; w17's OQ-w17-cl-03 signed-out viewer deep-link repair — all still owed, all untouched by w18. | Unchanged. | **inherited from w15/w17, restated for continuity** |

**Closed this wave:** w17's known-gaps row 6 ("W18 remainder of NW-63") is half-closed — the
box overlay, the widened gateway/schema, and the phrase-edit write endpoint all landed — and
half-carried forward into rows 1–2 above, in more precise form (a specific missing write
path and a specific missing UI affordance, not "the whole remainder"). w17's known-gaps row
7 ("termination and price uplift have no correctable field") is unaffected — the phrase-edit
endpoint added this wave is keyed by whatever `fieldName` already has an `ExtractionEvidence`
row, the same closed set as before.
