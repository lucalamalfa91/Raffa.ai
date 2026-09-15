# Wave w16 "nothing the product knows lives only in a browser tab" — acceptance runbook (W16-A1, A16-1…A16-9)

Operator checklist for wave `w16` (`.helix/reports/context/waves/w16-requirements.md`;
decision record `.helix/reports/architecture/waves/w16.md`; ADR-028 server-side state,
ADR-012 w16 footers, ADR-016 w16 clauses 34–35, ADR-014 w16 clauses 1–2). One section per
acceptance item, each naming **the URL, the header posture and the expected status code**
plus the observable pass condition. Same shape as [`w15-acceptance.md`](w15-acceptance.md).

| | |
|---|---|
| Owner | task E19/F08/US01/T01 (`w16-integration`), story `us-01-final-integration` |
| Oracles | `w16-requirements.md` items NW-07, NW-08, NW-31, NW-32, NW-11, NW-12, NW-13, NW-21, W16-01 · ADR-028 · ADR-012 w16 · ADR-016 w16 · ADR-014 w16 · ADR-010 w16 · ADR-022 w16 |
| API contract | `web/openapi/raffa-api.v1.json` — **every route below is quoted from it** |
| Screens | `web/README.md` "Screens (ADR-024 V2 route map)" → `/renewals`, `/contracts/:contractId`, `/quotes/:quoteId`, `/savings`, `/ask` |
| Automated cover | `dotnet test backend/Raffa.slnx` (including Postgres / Testcontainers) · `cd web && npm run build && npm test` · `web/e2e/v2.spec.ts` A8 (capability catalog) plus the W16 reload case (runbook, not CI) |

Run this against **`dev`**. Promotion to `demo` is a separate operator act *after* this walk.
**w16 cuts no `demo-v*` tag** (ADR-014 w16 clause 2). One `integration → main` PR.

---

## 0. Before you start

### 0.1 What the wave is, in one paragraph

Conversation rows are keyed by the token `oid` and pre-w15 UPN-keyed history is retired,
never remapped. `GET /api/audit` is a membership-gated Admin read with `X-Tenant-Id`.
`X-Role` and `X-Workspace-Role` are gone; the capability catalog is served whole; the
mutating reprocess workflow is deleted and replaced by a report-only verification job.
Every write names its actor. Renewal actions, quote outcomes and Contract 360 step ticks
are server state: a reload or a second browser shows the same fact, and the three session
stores are gone.

### 0.2 How the wave was built

The Helix fan-out for w16 stalled on Windows worktree locks (`EmptyStreamExhausted`).
Remaining leaf tasks after `E19/F06` and `E18/F03` were implemented by hand on branch
`integration` on 2026-09-15. Nothing in this document relies on the fan-out's own
delivery claims.

### 0.3 The operator sequence

| # | What | How |
|---|---|---|
| 1 | Merge the wave PR to `main` | `dev` deploys on push (`backend.yml`, `web.yml`); the image tag is the merged sha — W16-A1 (a) |
| 2 | **Confirm the baseline's infra apply** (PR #118: `worker_max_replicas` 3→5 on `dev`, `MaxConcurrentCalls=4`) | HCP Terraform `raffa-dev`. Record applied-or-not as a **fact** in [Known gaps](#known-gaps-that-shape-acceptance-today). It **does not gate A16-3**. `ignore_changes` on both container apps means it cannot roll the running image back |
| 3 | **Do not apply anything this wave wrote to `infra/`** | `git diff --stat origin/main..HEAD -- infra` is empty. No HCP run is owed by w16 |
| 4 | One interactive sign-in on deployed `dev` | W16-A1 (f) |
| 5 | Walk W16-A1, then A16-1 … A16-9 | this document |

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
# CONTRACT, QUOTE, DOCUMENT: ids from the screens you walk
```

**Header posture.** Every tenant-scoped route below sends `Authorization: Bearer $TOKEN` and
`X-Tenant-Id: $TENANT`. **Never** `X-Role`, `X-Workspace-Role` or `X-User-Id`. Identity is
the bearer `oid`. `GET /api/capabilities` takes **no** tenant header.

---

## W16-A1 — the wave base (ADR-014 / ADR-016)

| Point | Check | Pass |
|---|---|---|
| (a) | merged sha on `main` after the PR | equals the image tag on `ca-raffa-dev-api` |
| (b) | `git diff --stat origin/main..HEAD -- infra` on the PR | **empty** (two-dot diff, never "no infra commits in the range") |
| (c) | `git diff --name-only origin/main..HEAD -- .github/workflows` | **exactly** `reprocess-tenant-documents.yml` (deleted) and `verify-tenant-corpus.yml` (added). `backend.yml` is not in the list |
| (d) | `backend.yml` and `web.yml` at the merge | both green, including Postgres / Testcontainers |
| (e) | one `dev` deploy after the merge | API + web + Worker revisions exist |
| (f) | one interactive sign-in on `WEB` | `GET /api/workspaces` is `200` |
| (g) | baseline PR #118 apply on `dev` | record applied / not applied in Known gaps; **does not gate A16-3** |

---

## A16-1 — conversation keying is the token `oid` (NW-07)

> NW-07 · ADR-010 w16 · task `E18/F02/US01/T01`

**Click path.** `WEB/ask` → ask any question → note the conversation URL `/ask/<id>` →
reload and open the same URL in a second browser, same account.

**Pass when:** both browsers resume the same thread. A conversation listed in the rail
opens. No surface counts a conversation it cannot open.

```bash
curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/conversations"
# 200
```

**psql** (only SQL proves retired UPN-keyed rows are not remapped):

```sql
SET app.tenant_id = '<tenant>';
SELECT user_id FROM conversation LIMIT 20;
-- every user_id is a GUID-shaped oid, never an email
```

**Automated:** `Raffa.Chat` conversation tests; `Raffa.Api.Tests` conversation endpoints;
`web/e2e/v2.spec.ts` A10.

---

## A16-2 — `GET /api/audit` for a live Admin (NW-08)

> NW-08 · ADR-026 w16 · task `E18/F02` audit half / OpenAPI in `E19/F06`

**Click path.** None — there is no SPA surface (ADR-018 / ADR-020 `none`).

**Pass when:** the ladder below holds. Spoofed `roles: Admin` on a token with no membership
is **404**, not 200.

```bash
# no token
curl -s -o /dev/null -w '%{http_code}' "$API/api/audit"
# 401

# missing / non-GUID tenant
curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" "$API/api/audit"
# 400

# well-formed tenant, no live membership
curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  "$API/api/audit"
# 404

# member who is not Admin
curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $MEMBER_TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/audit"
# 403

# live Admin of $TENANT
curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/audit"
# 200 — that tenant's rows only
```

**Automated:** `Raffa.Api.Tests` audit authorization tests (S-T25 / S-T26). No `ApiClient`
wrapper, no vitest, no e2e.

---

## A16-3 — role headers gone; Admin resubmits a document in the product (NW-31)

> NW-31 · ADR-016 w16 · ADR-022 w16 · task `E18/F03/US01/T01`

**Click path.** `WEB/documents` as a live Admin → open a document that needs a reprocess →
**Reprocess**. The row moves `Uploaded`/`Processing` → a terminal status with the browser
closed. Then Actions → **verify-tenant-corpus** → `target_environment: dev`, `tenant_id: $TENANT`.

**Pass when:**

1. `X-Role` / `X-Workspace-Role` match nothing under `backend/src`, `web/`, `web/openapi/`, `.github/`.
2. `X-Tenant-Id` is still on every tenant-scoped call (`web/src/api/client.ts`).
3. `GET /api/capabilities` returns the **same body** with no token, a member token and an Admin token (`200`).
4. `POST /api/documents/{id}/reprocess` as Admin is **`202`**, never `200` with `pagesParsed`.
5. The verification job reports; it does not POST. `%PDF` embeddings are 0 (R-DOC-07 AC-1) or the worklist names the remaining documents.

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$API/api/capabilities" | jq 'length'
# identical to:
curl -s "$API/api/capabilities" | jq 'length'

curl -s -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/documents/$DOCUMENT/reprocess"
# 202
```

**Automated:** `CapabilitiesEndpointTests`; `web/e2e/v2.spec.ts` A8 (`cosa sai fare?`).

---

## A16-4 — every write names its actor (NW-32)

> NW-32 · ADR-011 w16 · task `E18/F04` / `E19/F04`

**Click path.** Any write you already made in A16-3 (reprocess) or A16-5 (renewal action).

**Pass when:** `grep -rn unattributed backend/src` is empty (or only the council-allowed
doc-comment hits). New `audit_event` rows have a real `oid` or a reserved `system:<component>`
actor. Pre-w16 `"unattributed"` rows are **permanent** — do not UPDATE or DELETE them
(`audit.sql` rejects both).

```sql
SET app.tenant_id = '<tenant>';
SELECT actor, COUNT(*) FROM audit_event GROUP BY actor ORDER BY 2 DESC;
-- no new row with actor = 'unattributed'
```

**Automated:** `RagAnswerServiceTests`; audit-actor tests (S-T28 / S-T29).

---

## A16-5 — renewal action is a server fact (NW-11)

> NW-11 · ADR-012 w16 clause 22 · ADR-028 · task `E19/F07/US01/T01`

**Click path.** `WEB/renewals` → **Start negotiation** on a row → reload → second browser
on the same workspace → `WEB/contracts/<id>` (Open contract). Status is **In negotiation**
on Renewals, the 360 tracker, and a `GET /api/renewals` row's `savedAction`.

**Pass when:** clearing `sessionStorage` (keep the workspace hint) changes nothing.
`NotStarted` (including **Assign to me** / Undo) renders as **no action taken** — Status
**Open**, tracker hidden. Savings does **not** grow a "Not yet available" pseudo-row.

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/renewals" | jq '.items[] | {contractId, action, savedAction}'

curl -s -o /dev/null -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d "{\"owner\":\"$ME\",\"status\":\"InProgress\",\"action\":\"In negotiation\"}" \
  "$API/api/renewals/$CONTRACT/action"
# 200
```

There is **no** `DELETE` for the action. Undo is ticks `PUT` then `POST` `NotStarted`.

**Automated:** `RenewalsRoute.test.tsx` (no session write); `renewalPipelineViewModel.test.ts`;
`web/e2e/v2.spec.ts` W16 case.

---

## A16-6 — quote outcome is a server fact (NW-12)

> NW-12 · ADR-012 w16 clause 23 · task `E19/F02/US02/T01` (screen) after `E19/F02/US01/T01` (API)

**Click path.** `WEB/quotes/<id>` → Show target and levers → Build negotiation strategy.
A recorded outcome renders from `GET /api/quotes/{id}`. Reload and a second browser show
the same figures. Recording an outcome writes **nothing** to `sessionStorage`.

**Pass when:** a failed `GET /api/quotes/{id}` shows **The quote** + Retry (never the raw path).

```bash
curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/quotes/$QUOTE"
# 200 (outcomes newest first) or 404
```

**Automated:** `QuoteCheckRoute.test.tsx`.

---

## A16-7 — negotiation-step ticks are a server fact (NW-13)

> NW-13 · ADR-028 §D4 · ADR-012 w16 clause 26 · task `E19/F07/US01/T01`

**Click path.** On a contract already **In negotiation**, tick **Notify …**. Reload: still
ticked. A second contract is unaffected. A failed PUT reverts the tick and shows
**Negotiation steps unavailable** + Retry.

**Pass when:** Undo issues `PUT { "steps": [] }` **then** `POST NotStarted`. Unknown server
keys are ignored; a missing key is unticked. Wire keys are `Notify`,
`RequestRevisedPricing`, `CounterWithMarketBenchmark`, `SignOrSendNonRenewalNotice`.

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/contracts/$CONTRACT/negotiation-steps"
# 200 [] or 200 ["Notify", …]

curl -s -o /dev/null -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"steps":["Notify"]}' \
  "$API/api/contracts/$CONTRACT/negotiation-steps"
# 200

curl -s -o /dev/null -w '%{http_code}' -X PUT \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"steps":["InventedFifthStep"]}' \
  "$API/api/contracts/$CONTRACT/negotiation-steps"
# 400 — writes nothing
```

**Automated:** `Contract360Route.test.tsx`; `contract360ViewModel.test.ts` (`ticksFromServer`).

---

## A16-8 — captured outcome links to a savings opportunity (NW-21)

> NW-21 · task `E19/F05` (server) — the client still names **no** `savingsOpportunityId`

**Click path.** Record an outcome on Quote check → **See it in Savings →**. The realized
KPI **count** moves, never a money amount invented by the SPA.

**Pass when:** `grep -c savingsOpportunityId web/src/routes/quotes/index.tsx` is **0**.
`countOf(kpis.savingsRealized)` in `savingsViewModel.ts` is still a count.

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/savings/kpis" | jq '.savingsRealized'
```

**Automated:** `savingsViewModel.test.ts` (realized meta is `N realized`); capture tests in
`QuoteCheckRoute.test.tsx`.

---

## A16-9 — R0 in-process queue is gone (W16-01)

> W16-01 · ADR-027 · Worker host

**Click path.** None. This is a process/host assertion.

**Pass when:** `grep -rn IQueueConsumer backend/` is empty. The Worker container has no
`Queue__*` env var. Extraction is Service Bus (`extraction-events` /
`document-processing`), as w15 already deployed.

```bash
az containerapp show -n ca-raffa-dev-worker -g rg-raffa-dev \
  --query "properties.template.containers[0].env[?starts_with(name, 'Queue')]"
# []
```

**Automated:** `Raffa.Worker.Tests.DeployableWorkerTests`.

---

## Promotion sequence

w16 is **one** `integration → main` PR and **cuts no `demo-v*` tag**. `demo` keeps
`Invitations__Mail__Enabled` and `guest_provisioning_enabled` **false**. After merge,
`demo` inherits `MaxConcurrentCalls = 4` from the module (12 documents in flight) —
record that at the next promotion, it is not a w16 task.

---

## Known gaps that shape acceptance today

ADR-016 w16 clause 35. Recorded as of `integration` @ this PR.

| # | Gap | Effect on acceptance | Disposition |
|---|---|---|---|
| 1 | **Bulk whole-tenant reprocess is W17.** w16 deleted `reprocess-tenant-documents.yml` and added `verify-tenant-corpus.yml`, which reports and does not mutate. | A `demo` walker must not read the missing bulk job as a regression. Walk A16-3 by resubmitting **one** document as a live Admin; use the verification job to assert `%PDF` is gone. | **added this wave** |
| 2 | **Baseline infra apply (PR #118)** — `worker_max_replicas` 3→5 on `dev` + `MaxConcurrentCalls=4`. Not written by w16. | Record applied / not applied here: `__________`. **Does not gate A16-3.** Cannot roll the running image (`ignore_changes` on both container apps). | **added this wave** (fact, not assumption) |
| 3 | **`demo` invitation flags stay off** — `Invitations__Mail__Enabled` and `guest_provisioning_enabled` are both `false` on `demo`, deliberately. | Invitation mail / guest provisioning walks stay `dev`-only this wave. | **inherited from w15** |
| 4 | **Postgres / Testcontainers suites** need Docker on the runner (`backend.yml`). | A `127.0.0.1:5432 refused` on a developer machine is a fixture gap, never a flake. CI is the gate. | **inherited from w15** |
| 5 | **Playwright is runbook evidence, not CI** (ADR-012 §12). | `web/e2e/v2.spec.ts` A8 and the W16 reload case are walked by the operator, not a workflow. | **inherited from w15** |
| 6 | **Verified-domain guest refusal** (`docs/waves/w15-acceptance.md` known-gaps). | Unchanged. | **inherited from w15** |

**Closed this wave:** w15's row that `reprocess-tenant-documents.yml` is out of service
(401 on `X-User-Id`). Replaced by A16-3 + gap #1.

**Closed this wave:** w15 / Ask-V2 gap that `GET /api/audit` had no OpenAPI entry. The
path is in `web/openapi/raffa-api.v1.json`; there is still no SPA wrapper.
