# Ask Contigo V2 — acceptance runbook (A1–A14)

Operator checklist for `inputs/requirements.md` §10 ("Acceptance on `demo`,
observable") and ADR-024. One row per acceptance item, each with **the exact
command or click-path** and **the observable pass condition** — nothing here
is "check that it looks right".

| | |
|---|---|
| Owner | task E13/F11/US01/T01 (`v2-integration`), story `us-01-integration` AC-5 |
| Oracles | `inputs/requirements.md` §5, §6, §10 · ADR-024 · `inputs/design/prototypes/Contigo V2 Prototype.html` (unpacked `contigo-v2/ia-v2.md`, `screens-v2.md`) |
| API contract | `web/openapi/contigo-api.v1.json` — **every route below is quoted from it**; the two exceptions are named in [Known gaps](#known-gaps-that-shape-acceptance-today) |
| Screens | `web/README.md` "Screens (ADR-024 V2 route map)" |
| Automated cover | `web/e2e/v2.spec.ts` (A1, A3, A4, A8, A9, A10, A14 always; A2/A5/A6/A7 under `E2E_LIVE_FOUNDRY=1`), `backend/tests/Contigo.AiEval` (A13), `Contigo.IntegrationTests` (A11), `Contigo.AiGateway.Tests` (A12) |

Run this against `dev` first (that is what the wave's Definition of Done asks
for), then again against `demo` after a `demo-v*` promotion. The steps are
identical; only `<env>` changes.

---

## 0. Before you start

### 0.1 The operator sequence

Run these **in order**. Each one is an explicit, dispatchable job — none of
them is a side effect of a push (ADR-021 / ADR-022).

| # | What | How |
|---|---|---|
| 1 | Deploy + apply the schema | merge to `main` (auto `dev`), or `git tag demo-v<N> <sha> && git push origin demo-v<N>` for `demo` (`.github/workflows/demo-promote.yml`; runbook: `infra/README.md` "Promotion to `demo`" and `.helix/reports/execution/demo-v-promotion-runbook.md`) |
| 2 | Seed the demo fixture (savings/benchmark rows) | Actions → **seed-demo-fixture** → `target_environment: <env>` |
| 3 | Seed the market corpus | Actions → **seed-market-intelligence** → `target_environment: <env>` |
| 4 | Re-OCR / re-embed the tenant and back-fill suppliers | Actions → **reprocess-tenant-documents** → `target_environment: <env>`, `tenant_id: <tenant>` |
| 5 | Walk A1–A14 | this document |

Step 3 asserts its own idempotency (a second ingestion pass must report
`0 inserted, 0 updated`) and that `market_record` / `market_embedding` carry no
`tenant_id`. Step 4 fails the run if any embedding of that tenant still starts
with `%PDF`, and reports the contracts that still have no supplier.

### 0.2 The three values every command below needs

```bash
# 1. API origin — the same value web.yml bakes into the SPA's config.json.
ENV=dev                      # or: demo
RG="rg-contigo-${ENV}"
API=$(az resource show --resource-group "$RG" --name "ca-contigo-${ENV}-api" \
        --resource-type Microsoft.App/containerApps \
        --query "properties.configuration.ingress.fqdn" -o tsv)
API="https://${API}"

# 2. SPA origin — where you click.
WEB=$(az resource show --resource-group "$RG" --name "swa-contigo-${ENV}" \
        --resource-type Microsoft.Web/staticSites \
        --query "properties.defaultHostname" -o tsv)
WEB="https://${WEB}"

# 3. Tenant — the workspace id the SPA sends as X-Tenant-Id. The ADR-022
#    demo fixture tenant is 00000000-0000-0000-0000-000000000001.
TENANT=00000000-0000-0000-0000-000000000001
USER=acceptance@contigo.test          # X-User-Id, non-authoritative (ADR-022)

curl -sS "$API/health" | jq .
```

Every API call in this document carries `-H "X-Tenant-Id: $TENANT"`. The
conversation endpoints also need `-H "X-User-Id: $USER"` (R-CONV-03); the
Admin-gated document endpoints additionally take a role header (see
[Known gaps](#known-gaps-that-shape-acceptance-today)); `GET /api/capabilities`
takes `X-Role` and no tenant; `GET /api/market/records/{id}` takes neither.

### 0.3 Direct database access (only where the UI cannot show it)

A1, A11 and A12 need to observe *absence* (no blob, no row, no other tenant's
data), which no screen can prove. Use the same connection the workflows use:

```bash
KV=$(az keyvault list --resource-group "$RG" --query "[0].name" -o tsv)
eval "$(az keyvault secret show --vault-name "$KV" --name postgres-connection \
        --query value -o tsv | python3 scripts/pg_connection_string_env.py)"

# RLS stays on. Set the same session claim the app sets (ADR-009); never
# disable a policy.
psql -Atqc "SET app.tenant_id = '$TENANT'; SELECT count(*) FROM document;"
```

---

## A1 — a recipe and an unreadable photo are refused; the MSA lands

> Drop `carbonara.pdf`, `nonna.jpg`, `Salesforce_MSA_2024.pdf` together → first
> two **Not added** with reasons, nothing stored for them (blob + DB + no
> embeddings); the MSA reaches `needs_review` or `completed`.
> (R-DOC-01, R-DOC-03, R-DOC-04)

**Click path.** `$WEB/documents` → **Upload contracts** (or drop onto the
dropzone) → select all three files at once.

**Pass when:**

1. Two `Not added` cards appear, one per refused file, each with its own
   reason sentence — `"Not added: this looks like a recipe, not a contract…"`
   for the recipe, `"Not added: Contigo could not read any contract text in
   this file…"` for the photo.
2. A third row appears for the MSA and reaches **Needs review** or
   **Completed** (never stays on a spinner). A refused file never hides it
   (R-DOC-01 AC-2).
3. Switching to **All documents ·** N shows the MSA and **not** the two
   refused files — they are session-only and are never counted.

**Same thing over the API** (one file per request — `POST /api/documents` is
single-file by contract; the client batches):

```bash
# 422, rejected: nothing is persisted.
curl -sS -o /tmp/rejected.json -w '%{http_code}\n' -X POST "$API/api/documents" \
  -H "X-Tenant-Id: $TENANT" -F "file=@carbonara.pdf;type=application/pdf"
jq . /tmp/rejected.json
# => 422 and {"rejected":true,"detectedType":"Other","confidence":…,
#             "reason":"not_a_contract","hint":"Contigo only keeps contracts, …"}

# 422 with the *other* reason for an unreadable image.
curl -sS -o /tmp/photo.json -w '%{http_code}\n' -X POST "$API/api/documents" \
  -H "X-Tenant-Id: $TENANT" -F "file=@nonna.jpg;type=image/jpeg"
jq -r .reason /tmp/photo.json          # => no_readable_text

# 415 before any model call: a .zip renamed .pdf (R-DOC-02 AC-1).
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$API/api/documents" \
  -H "X-Tenant-Id: $TENANT" -F "file=@notreally.pdf;type=application/pdf"   # => 415

# 413 over Documents:MaxFileBytes (50 MB).
# 201 for the MSA.
curl -sS -o /tmp/admitted.json -w '%{http_code}\n' -X POST "$API/api/documents" \
  -H "X-Tenant-Id: $TENANT" -F "file=@Salesforce_MSA_2024.pdf;type=application/pdf"
jq '{id, contractId, processingStatus}' /tmp/admitted.json    # => 201
```

**"Nothing stored" — the part only SQL can prove** (R-DOC-03: no blob, no
`document` row, no embedding; one audit event):

```bash
psql -Atqc "SET app.tenant_id = '$TENANT';
  SELECT count(*) FROM document WHERE file_name IN ('carbonara.pdf','nonna.jpg');"   # => 0
psql -Atqc "SET app.tenant_id = '$TENANT';
  SELECT action, resource_type, occurred_at FROM audit_event
   WHERE action = 'document.rejected' ORDER BY occurred_at DESC LIMIT 5;"
# => one row per refused file; `detail` carries the file-name hash, detected
#    type, confidence and reason — never the content (ADR-011).
```

**Automated:** `web/e2e/v2.spec.ts` → *"A1 — a recipe and an unreadable image
are refused together, and nothing is stored"* (always) and *"A1 — the MSA
dropped alongside them still lands"* (needs `CONTIGO_E2E_MSA_PATH`: no real
contract PDF is checked into this repo).

---

## A2 — a scanned order form is OCR'd, admitted, and shows its supplier

> Drop a PNG scan of an order form → OCR → admitted → shows supplier name in
> Documents. (R-DOC-02 AC-2, ADR-017, R-SUP-04)

**Requires live Foundry** (`AiGateway__Endpoint` set on
`ca-contigo-<env>-api`). On a fixture-gateway environment the scan is refused
with `no_readable_text`, which is the honest fixture behaviour, not a defect —
see [Known gaps](#known-gaps-that-shape-acceptance-today).

**Click path.** `$WEB/documents` → upload the PNG scan → wait for the row's
stage text (`Classifying` → `OCR / text` → `Sections & tables` → …).

**Pass when:** no **Not added** card appears; the row reaches **Needs review**
or **Completed**; its *Supplier · type* cell shows a **name** (e.g.
`Salesforce · Order form`), never a guid.

```bash
curl -sS "$API/api/documents?pageSize=100" -H "X-Tenant-Id: $TENANT" \
  | jq -r '.items[] | [.fileName, .documentType, .processingStatus, (.supplierName // "-")] | @tsv'
```

**Automated:** `v2.spec.ts` → *"A2 …"*, gated on `E2E_LIVE_FOUNDRY=1` **and**
`CONTIGO_E2E_ORDER_FORM_PNG` (a synthetic image has no text to OCR).

---

## A3 — "ciao" / "carbonara" is a warm decline with a real portfolio hook

> Warm decline + a real contract hook of this tenant; **no retrieval**.
> (R-ASK-02, R-ASK-07)

**Click path.** `$WEB/ask` → type `ciao` → **Ask**. Repeat with
`ricetta della carbonara`.

**Pass when:**

- The reply is *redirect* prose naming a real contract or saving of **this**
  tenant (or, on an empty workspace, an upload invite).
- There is **no** abstain block — the words *"Cannot determine reliably."* must
  not appear.
- Exactly one call-to-action button.
- No citation to a tenant document (nothing was retrieved).

```bash
CONV=$(curl -sS -X POST "$API/api/conversations" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $USER" \
  -H 'Content-Type: application/json' -d '{}' | jq -r .id)

curl -sS -X POST "$API/api/conversations/$CONV/messages" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $USER" \
  -H 'Content-Type: application/json' -d '{"question":"ciao"}' \
  | jq '{kind, answerMarkdown, citations: (.citations | length), actions: [.actions[].href]}'
# => kind == "redirect", citations == 0, one action
```

**Automated:** `v2.spec.ts` → *"A3 …"*.

---

## A4 — "Posso fare causa a Salesforce?" is refused, with the commercial analogue

> Refusal + commercial analogue + Contract 360 action. (R-ASK-02 AC-3)

**Click path.** `$WEB/ask` → `Posso fare causa a Salesforce?` → **Ask**.

**Pass when:** the reply is a *refusal* — it declines the legal reading, offers
the commercial analogue ("I can tell you whether the liability cap sits above
the market band…"), and carries an action whose href starts with `/contracts`.
No abstain block, no legal opinion, no case-law reference.

```bash
curl -sS -X POST "$API/api/conversations/$CONV/messages" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $USER" \
  -H 'Content-Type: application/json' \
  -d '{"question":"Posso fare causa a Salesforce?"}' \
  | jq '{kind, actions: [.actions[].href]}'
# => kind == "refusal", at least one action under /contracts
```

**Automated:** `v2.spec.ts` → *"A4 …"*.

---

## A5 — "Is my Allianz contract above market?"

> below / in line / above from P25–P75 **or** insufficient data; provenance
> *representative · mock feed · updated …*; citation to the contract page and
> to the market record. (R-CMP-01, R-MKT-04)

**Requires live Foundry.** Also requires step 3 of the operator sequence
(`seed-market-intelligence`) and a validated Allianz-class contract in the
tenant.

**Click path.** `$WEB/ask` → `Is my Allianz contract above market?` → **Ask**.

**Pass when** *either*:

- **answer** — the prose says *below* / *in line* / *above*, and there are two
  citation cards: one badged **Validated contract** with a page/section
  subtitle, one badged **Market · representative** whose subtitle carries the
  feed date. Clicking the market card opens the record side panel
  (`GET /api/market/records/{id}`); clicking the contract card lands on
  `/contracts/<id>?clause=…` with the clause highlighted (R-EVD-02); **or**
- **abstain** — "insufficient market data" for that line, with no number
  invented. A bare number without provenance is a **defect** (ADR-001).

```bash
# The band itself, deterministically, without the model:
curl -sS "$API/api/contracts/<contractId>/strategy" -H "X-Tenant-Id: $TENANT" | jq .
# One market record behind a citation (no tenant header — the corpus is shared):
curl -sS "$API/api/market/records/<recordId>" \
  | jq '{recordId, title, category, geography, band, provenance, updatedAt}'
# `provenance` is the label the UI must show ("representative · mock feed · …").
```

**Automated:** `v2.spec.ts` → *"A5 …"*, gated on `E2E_LIVE_FOUNDRY=1`.

---

## A6 — "Come dovrei affrontare il rinnovo Salesforce?"

> dates = renewal engine, targets = calculator, levers cited, actions to
> Contract 360 and Renewals. (R-STR-01, R-STR-02)

**Requires live Foundry.**

**Click path.** `$WEB/ask` → `Come dovrei affrontare il rinnovo Salesforce?`
→ **Ask**. (Or from `$WEB/contracts/<id>` → **Ask about it**, which opens
`/ask?scope=<id>`.)

**Pass when:** the reply follows *When you must move* → *Where you can push* →
*Targets* → *Next steps*, carries actions to `/contracts/…` **and**
`/renewals…`, and **every number matches the calculator to the cent**:

```bash
curl -sS "$API/api/contracts/<contractId>/strategy" -H "X-Tenant-Id: $TENANT" | jq .
curl -sS "$API/api/renewals/<contractId>/priority" -H "X-Tenant-Id: $TENANT" | jq .
```

Compare the opening target / acceptable range / walk-away and the notice and
end dates in the reply against those two responses. A mismatch is a numeric
guard failure, not a wording difference. A deadline already passed must be
stated as passed, never hidden (R-STR-01 AC-2).

**Automated:** `v2.spec.ts` → *"A6 …"*, gated on `E2E_LIVE_FOUNDRY=1`.

---

## A7 — new chat: "Quali sono i contratti più critici e dove posso risparmiare?"

> top-5 with component explanations, totals = calculator sums, actions per row.
> (R-PORT-01, R-PORT-02)

**Requires live Foundry.**

**Click path.** `$WEB/ask` → **+ New chat** in the rail (a portfolio question
starts its own conversation, R-CONV-02) → ask the question.

**Pass when:** at most five contracts are ranked, each row names the component
that drives its rank and offers an action; totals equal the calculator sums:

```bash
curl -sS "$API/api/insights/criticality" -H "X-Tenant-Id: $TENANT" \
  | jq '.items | sort_by(-.totalScore) | .[:5] | .[] | {contractId, totalScore, components}'
curl -sS "$API/api/savings" -H "X-Tenant-Id: $TENANT" \
  | jq '{low: ([.items[].estimatedSavingsLow] | add), high: ([.items[].estimatedSavingsHigh] | add)}'
```

The ranking must be **stable** across two identical runs (R-PORT-01 AC-1), and
each item's `components` must sum to its `totalScore`. An empty portfolio must
produce an upload invite, never a ranking (R-PORT-02 AC-2).

**Automated:** `v2.spec.ts` → *"A7 …"*, gated on `E2E_LIVE_FOUNDRY=1`.

---

## A8 — "Cosa sai fare?" / "Come faccio a rivedere i campi deboli?"

> module list / how-to with feature cards and working links. (R-SYS-01…03)

**Click path.** `$WEB/ask` → `Cosa sai fare?` → **Ask**. Then, in the same
chat, `Come faccio a rivedere i campi deboli?`.

**Pass when:**

- The reply lists Contigo's modules, each with a citation card badged
  **Contigo** (a *feature* card, not a tenant chunk).
- Every action href is an **in-app route** (`/documents`, `/renewals`,
  `/savings`, `/quotes`, `/contracts/…`) — never an absolute URL, never a
  model-authored link (R-ASK-06 guard 3).
- Clicking each one actually lands on that screen.
- The weak-fields answer routes to `/documents?filter=attention` and names the
  **Documents › Review** capability (R-SYS-03 AC-1).

```bash
curl -sS "$API/api/capabilities" -H "X-Role: Admin" \
  | jq -r '.capabilities[] | [.key, .routePattern, .roleGate] | @tsv'
# Every action href a reply returns must match one of these routePatterns.
```

**Automated:** `v2.spec.ts` → *"A8 …"* (follows the first action link and
asserts the app renders it).

---

## A9 — every reply cites, acts, and shows no engineer chrome

> markdown + ≥ 1 citation card with page/section or record id + ≥ 1 action; no
> guid, no route line. (R-ASK-08, R-WEB-04)

**Click path.** Any of A3–A8 above. Read the reply body, not the HTML.

**Pass when, for every reply on screen:**

| Must be true | Must never appear |
|---|---|
| Markdown prose with inline `[n]` markers | a guid (`3f2a…-…`) in the visible text |
| An `answer` carries ≥ 1 citation card whose title is human (`Salesforce · MSA 2024`) and whose subtitle is `p.12 §8.4` or the market record's provenance | `Document:<guid>` chips |
| A first-page preview image or the honest `No page preview available` placeholder | the route line `Structured query…` / `Clause retrieval…` |
| ≥ 1 action button (a `redirect` / `refusal` carries exactly one) | `not wired`, `chunk 3`, raw `%PDF` text |

```bash
curl -sS -X POST "$API/api/conversations/$CONV/messages" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $USER" \
  -H 'Content-Type: application/json' \
  -d '{"question":"When does our Salesforce contract expire?"}' \
  | jq '{kind, citations: [.citations[] | {n, corpus, title, subtitle, page, section, recordId}],
         actions: [.actions[].href], provenance}'
```

`provenance.promptVersion` and `provenance.modelId` must be present on every
reply (R-ASK-09). A citation card's preview is fetched from
`GET /api/documents/{id}/preview`, which returns `image/png` — never a raw
blob URL (R-DOC-08, ADR-009):

```bash
curl -sS -o /tmp/preview.png -w '%{http_code} %{content_type}\n' \
  "$API/api/documents/<documentId>/preview" -H "X-Tenant-Id: $TENANT"
# => 200 image/png
```

**Automated:** `v2.spec.ts` → *"A9 …"*.

---

## A10 — resume a conversation from another browser

> Same turns, cards and actions work. (R-CONV-01, R-CONV-02 AC-1)

**Click path.** Ask something at `$WEB/ask`; the URL becomes
`$WEB/ask/<conversationId>`. Open that URL in a **different browser** (or a
private window) signed in as the **same** user, on the same workspace.

**Pass when:** every past turn renders oldest-first, with the same citation
cards and clickable actions; the rail lists the conversation under **Ask
Contigo**. Signed in as a *different* user of the same workspace, the same URL
shows *"Conversation not found — This conversation does not exist, or is not
yours."* (R-CONV-01 AC-1).

```bash
curl -sS "$API/api/conversations?take=5" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $USER" \
  | jq -r '.[] | [.id, .title, (.scopeContractId // "-"), .updatedAt] | @tsv'

curl -sS "$API/api/conversations/$CONV" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $USER" | jq '.messages | length'

# Another user, same tenant, same id => 404 (never another member's chat).
curl -sS -o /dev/null -w '%{http_code}\n' "$API/api/conversations/$CONV" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: someone.else@contigo.test"   # => 404
```

**Automated:** `v2.spec.ts` → *"A10 …"* (reload plus a second tab).

---

## A11 — another tenant's contracts never appear

> Existing isolation tests extended to conversations and to the market index.
> (ADR-009, ADR-011)

**Not a browser check.** A browser cannot hold two tenants at once without
fabricating an identity. Two proofs:

```bash
# 1. The automated one, and the authoritative one:
dotnet test backend/Contigo.slnx --filter "FullyQualifiedName~Contigo.IntegrationTests"

# 2. On the live environment: a guessed id under the wrong tenant is a 404,
#    not another tenant's row.
curl -sS -o /dev/null -w '%{http_code}\n' "$API/api/documents/<other-tenant-document-id>" \
  -H "X-Tenant-Id: $TENANT"                                              # => 404
curl -sS -o /dev/null -w '%{http_code}\n' "$API/api/conversations/<other-tenant-conversation-id>" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $USER"                        # => 404
```

And the market corpus must stay tenant-free — asserted by
`seed-market-intelligence.yml`'s own final step, re-checkable by hand:

```bash
psql -Atqc "SELECT count(*) FROM information_schema.columns
  WHERE table_name IN ('market_record','market_embedding') AND column_name = 'tenant_id';"  # => 0
```

---

## A12 — Foundry requests carry no tools/grounding; every AI call is logged

> (R-AI-03, R-ASK-09, ADR-011)

**Not a browser check** — a request body is not observable from a screen.

```bash
# The request-shape compliance test (the fake HTTP handler asserts the body).
dotnet test backend/Contigo.slnx --filter "FullyQualifiedName~Contigo.AiGateway.Tests"

# The AI log, live: LoggingAiGateway writes one audit row per model call with
# resource_type 'ai_call' (model, version, prompt version, input hash — never
# the prompt, the pack or the answer).
psql -Atqc "SET app.tenant_id = '$TENANT';
  SELECT action, resource_type, occurred_at, left(detail, 160) FROM audit_event
   WHERE resource_type = 'ai_call' ORDER BY occurred_at DESC LIMIT 10;"
```

**Pass when:** every reply you produced in A3–A9 has matching `ai_call` rows
carrying a prompt version, and no row contains raw prompt/answer text. One
`chat.answered` / `chat.redirected` / `chat.refused` / `chat.abstained` audit
row exists per turn.

---

## A13 — golden set: 0 numeric-guard interventions, kinds match

> (R-EVD-03, spec §15.3)

**Not an environment check** — the golden set runs against the **fixture**
gateway so it is reproducible.

```bash
# In CI: no extra step exists or is needed. `.github/workflows/backend.yml`'s
# "dotnet test" job runs `dotnet test Contigo.slnx`, and
# `backend/tests/Contigo.AiEval/Contigo.AiEval.csproj` is a member of that
# solution — a guard intervention fails the build there.
cd backend && dotnet test Contigo.slnx --configuration Release

# Locally, only the golden set (project-scoped; works whatever traits the
# suite carries):
dotnet test backend/tests/Contigo.AiEval/Contigo.AiEval.csproj

# Or, from the solution, by trait — the golden set marks its cases
# [Trait("Category","AiEval")] (task E13/F06/US01/T02):
dotnet test backend/Contigo.slnx --filter "Category=AiEval"

# Everything *except* the golden set (a fast inner loop):
dotnet test backend/Contigo.slnx --filter "Category!=AiEval"

# The on-demand Foundry run (manual, never CI — it costs tokens):
AiEval__UseFoundry=true AiGateway__Endpoint=<foundry endpoint> \
  dotnet test backend/tests/Contigo.AiEval/Contigo.AiEval.csproj
```

**Pass when:** the suite is green — ≥ 40 cases, expected `kind` per case, 0
`NumericGuard` / `GroundingGuard` interventions, no forbidden substring, every
action href resolving to a catalog route. The per-case verdict report is
written to `backend/tests/Contigo.AiEval/reports/last-run.md` (git-ignored).

---

## A14 — `/` lands on `/ask`; the rail is two-tier; the secondary tier is greyed

> (R-WEB-01, R-WEB-02, R-SYS-04)

**Click path.** Open `$WEB/` signed in.

**Pass when:**

1. The browser ends on `$WEB/ask` — there is no Home screen and no **Home**
   rail item.
2. The rail reads, top to bottom: **Ask Contigo** (⌘K badge, last 5
   conversations nested, **+ New chat**), **Documents** ("N to review" badge),
   the section kicker **From your contracts**, then **Portfolio**,
   **Renewals**, **Quote check**; **Workspace & members** in the footer for an
   Admin.
3. On a workspace with **no validated contract**, the three secondary items are
   greyed, and `/ask` shows the off state: *"Ask needs at least one validated
   contract."* with a single CTA to `/documents`.
4. After the first contract is validated, the same three items are no longer
   greyed and Ask switches on (reload the shell — the validated count is read
   once per mount).
5. `⌘K` / `Ctrl+K` focuses the global Ask bar on every screen; pressing Enter
   there always opens a **new** chat on `/ask` (R-WEB-03), never appends to the
   open conversation.
6. `$WEB/review` redirects to `$WEB/documents?filter=attention` — Review is a
   state of Documents in V2, not its own destination.

**Automated:** `v2.spec.ts` → the three *"A14 …"* tests.

---

## Running the automated walk

```bash
cd web
npm ci
npx playwright install --with-deps chromium      # one-time

CONTIGO_E2E_BASE_URL="$WEB" \
CONTIGO_E2E_ENTRA_EMAIL=<test account UPN> \
CONTIGO_E2E_ENTRA_PASSWORD=<that account's password> \
CONTIGO_E2E_TENANT_ID="$TENANT" \
  npx playwright test v2.spec.ts

# Add the live-Foundry rows (A2, A5, A6, A7) once Foundry is wired:
E2E_LIVE_FOUNDRY=1 CONTIGO_E2E_ORDER_FORM_PNG=/path/to/scan.png … \
  npx playwright test v2.spec.ts

npx playwright show-report                       # trace / video / screenshot on failure
```

With none of those variables set the suite reports every row as **skipped**
with the reason, and exits `0` — it never silently passes and never attempts a
real sign-in. `web/e2e/day1.spec.ts` is the V1 walk and is red against the V2
shell by design (`web/README.md` → "Known regression"); `v2.spec.ts` replaces
it.

---

## Known gaps that shape acceptance today

Recorded as of `integration` @ `1ca7888`. None of them is fixed by this
document; each one changes what a given row can honestly prove.

| # | Gap | Effect on acceptance |
|---|---|---|
| 1 | **`ConnectionStrings__Suppliers` is not injected into the API Container App.** `backend/src/Contigo.Api/Program.cs` fail-fasts on it; `infra/modules/containerapps/main.tf` sets `IdentityWorkspace`, `DocumentsContracts`, `Audit`, `Renewals`, `Savings`, `Quotes`, `Chat`, `Storage` — not `Suppliers`. | The deployed API does not boot. **Blocks every row.** Fix in `infra/modules/containerapps/main.tf` (same `pg-cs` secret as its neighbours) before the first V2 promotion. |
| 2 | **The API composes `AddMarketModule()` without a connection string.** The market module then keeps its in-memory mock projection, so the API never reads the `market_record` / `market_embedding` rows `seed-market-intelligence.yml` writes. | A5's numbers come from the in-process mock, not from the seeded corpus. The seed job is still the right pre-step (it is what R-MKT-03 specifies and what the live provider will feed), but "the API reads the seeded corpus" is not yet true. |
| 3 | **No live Foundry unless `AiGateway__Endpoint` is set** (ADR-008 leaves the Foundry account portal-only; `infra/README.md` "Known gaps"). | A2 and A5–A7 cannot be walked. `reprocess-tenant-documents.yml` detects this and downgrades its OCR-placeholder check to a warning; `v2.spec.ts` skips those rows with a named reason. |
| 4 | **`POST /api/conversations` and `POST /api/conversations/{id}/messages` have no `requestBody` in `web/openapi/contigo-api.v1.json`.** The real bodies are `{"scopeContractId": "<uuid>"}` (optional) and `{"question": "…"}` — verified against `Contigo.Api.ConversationsEndpointExtensions` and `web/src/api/client.ts`. | The `curl` commands above are correct; the OpenAPI is incomplete. Owned by the task that owns that file, not by this runbook. |
| 5 | **The role signal for the Admin-gated document endpoints is not a declared parameter.** `X-Role` is what `GET /api/capabilities` parses; `X-Workspace-Role` is what the OpenAPI's `deleteDocument` / `reprocessDocument` descriptions name. | `reprocess-tenant-documents.yml` sends **both**. When testing `DELETE`/`reprocess` by hand, send both too — and expect both to disappear when ADR-010's API JWT lands. |
| 6 | **`GET /api/audit` needs an authenticated `ClaimsPrincipal`** (`Contigo.Api.AuditEndpointExtensions` authorizes a real Workspace Admin identity), which the ADR-022 header posture does not provide — and it is the one mapped route with no entry in `web/openapi/contigo-api.v1.json`. | Audit checks in A1, A9 and A12 are SQL against `audit_event`, not API calls. It is the only `/api/...` path in this document that does not resolve to a documented operation; every other one does. |
