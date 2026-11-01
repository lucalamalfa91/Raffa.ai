# AskRaffa 4.0 — implementation plan (shared contract for all slices)

Source of truth: `reference/AskRaffa V4.dc.html`. Parity is proved by `web/parity/` (copied from `parity/`).

## 1. Screen → repo map (no parallel routes)

| Prototype screen (`data-screen-label`) | Route | Files |
|---|---|---|
| Shell: top nav + Guida Raffa | all | `web/src/components/shell/AppShell.tsx` (rewritten: TopNav + GuidePanel; RailNav retired), `shell/TopNav.tsx`, `shell/GuidePanel.tsx`, `shell/guide.ts` (GuideContext), `shell/shell4.css` |
| Demo bar (fixture mode only, `data-no-i18n`) | all | `web/src/fixtures/DemoBar.tsx` (never rendered unless runtime config `fixtures: true` AND `?fixture=` / remembered scenario) |
| Oggi / Punto zero / Focus | `/today` (index `/` → `/today`) | `web/src/routes/today/*` |
| Pratiche (=Rinnovi) | `/renewals` | `web/src/routes/renewals/index.tsx` (rewritten) |
| Pratica / Scheda pratica / Passo corrente | `/renewals/:caseId` | `web/src/routes/renewals/case/*` (rewritten; engine.ts/view.ts/mockServices.ts removed — the engine is server-side) |
| Contratti | `/contracts` | `web/src/routes/contracts/index.tsx` |
| Contratto / Decisione | `/contracts/:contractId` | `web/src/routes/contracts/contract360/*` |
| Revisione | `/contracts/review/:documentId` (old `/contracts/:id/review` kept as redirect) | `web/src/routes/contracts/review/*` |
| Risultati / Obiettivo / Verso l'obiettivo / KPI / grafici | `/savings` | `web/src/routes/savings/*` |
| Chiedi / Conversazioni | `/ask`, `/ask/:conversationId` | `web/src/routes/ask/*` |
| Offerta | `/quotes/:quoteId` | `web/src/routes/quotes/*` |

`/documents`, `/workspace/members` stay reachable (not in the top nav — the prototype has 5 tabs only).

## 2. Cross-cutting decisions

- **Engine on the server.** `renewal-case-v4.js` is ported to C# in `Raffa.Renewals` (`Application/Cases/*`). The server returns the **view** (header, summary, done steps, current card, buttons, todo items) as data whose texts are `Msg {id, vars}` — `id` = English text (the `B(en,…)` side), translated client-side by the Italian catalogue (`web/src/i18n/it/*.ts`, gettext style already in the repo). Person-entered text and contract data are plain strings.
- **i18n.** Language key `raffa4.lang` (localStorage; read by `I18nProvider`, `?lang=` still wins). Default `it` in fixture mode, `en` otherwise. Catalogues per area: `it/shell.ts`, `it/today.ts`, `it/case4.ts`, `it/contracts4.ts`, `it/results.ts`, `it/ask4.ts`, `it/quote4.ts` merged in `it/index.ts`. Message ids = the prototype's EN strings (`i18n-en.js`, `COPY-CATALOGUE.md`), so IT output equals the prototype text.
- **Styling.** Each screen reproduces the prototype's markup and inline styles 1:1 as CSS classes (prefix per screen, e.g. `.o4-*`, `.r4-*`, `.k4-*`). Values from `STYLE-INVENTORY.md`. Page ground `#fff`, `--color-surface:#f3f2f2` inside the v4 shell (`.v4` root class). DS component classes (`.btn .btn-primary .btn-secondary .btn-ghost .input .tag`, `h2`, `h6`) come from `web/src/styles/ds-modernist.css` = the rules of `reference/_ds/.../styles.css` (tokens already identical in `tokens.css`). Radius 0 everywhere.
- **Fixture mode (test/demo only, never production).**
  - Backend: `Fixtures:Enabled` (default false; startup throws if true in `Production`). When on: auth scheme `Fixture` reads `X-Raffa-Fixture-User: luca|marta`; clock = `X-Raffa-Now` header (else `Fixtures:Now`, default 2026-09-08); `POST /api/fixtures/reset {scenario:"zero"|"full"}` wipes and reseeds the fixture tenant **Helvetia Foods AG** via every registered `IFixtureScenarioSeeder` (one per module, ordered). Fixture data lives only in `backend/fixtures/askraffa4/*.json` (copied from `raffa-data.js`, `savings-quote-v4.js`, the prototype `seed()` and conversations). Users: Luca Lamalfa (Head of Procurement, approver/CPO), Marta Keller (Buyer).
  - Frontend: runtime config `fixtures: true` + URL `?fixture=zero|full&now=YYYY-MM-DD` → skip MSAL, call reset, send the two headers, show the demo bar (Scenario Da zero/Con dati, Vedi come Luca/Marta, IT/EN, Reimposta). Scenario/viewer remembered in `sessionStorage` (`raffa4.mode`, `raffa4.viewer`).
  - Scripted parts of the prototype (supplier replies, draft contract, signed file, extraction, example address, goal parser) are server-side **fixture services** behind interfaces whose production implementations call the real pipeline/LLM, or return `notAvailable` so the UI hides the button (e.g. "Usa indirizzo di esempio" exists only in fixture mode).
- **Contract facts.** `SharedKernel/Contracts/ContractFacts.cs` (= `RD.CONTRACTS[id]` + `RD.DEEP[id]` + evidence) and `IContractFactsReader` (by tenant, contract id; list validated). Implemented in `Raffa.Documents.Contracts` from validated contract fields + `contract_facts` table (jsonb profile written at validation / by the fixture seeder). Renewals, Savings, Quotes, Chat read contracts only through this interface.
- **Renewed terms.** Closing a case as *renewed* writes a `contract_term_version`; `IContractFactsReader` returns the current version, so Oggi/Rinnovi/Contratti/Risultati read new dates.
- **API conventions.** Existing `/api/...` minimal-API style, endpoints in `Raffa.Api/*EndpointExtensions.cs`, contract in `web/openapi/raffa-api.v1.json` (hand-maintained) → `npm run generate:api` regenerates `schema.ts`. Every mutation returns the updated aggregate/view.
- **Feature flags.** `/api/capabilities` gains `askraffa4.guidedFlow`, `askraffa4.noBenchmark`, `savings.results` (on). With `noBenchmark` on, market/benchmark surfaces are not called.

## 3. Slices and ownership

| Slice | Backend | Frontend |
|---|---|---|
| 1 Foundation | Fixtures plumbing, boot with Postgres only (in-memory storage, inline queue, FixtureAiGateway) in env `Parity`, ContractFacts + reader + `contract_facts` table, fixture JSON, capabilities flags | Fixture bootstrap, DemoBar, AppShell (TopNav/GuidePanel), DS css, route table, i18n key | 
| 2+3 Oggi, Rinnovi, Pratica | `renewal_case`(+state jsonb, version), `case_email`, `case_delegation`, `case_event`, `case_vendor_message`, `user_reminder`, `today_snooze`; C# engine; `/api/renewals*`, `/api/today*`, `/api/reminders` | today, renewals, renewals/case |
| 4 Contratti, Contratto, Revisione | decision points (`renewal_negotiation_todo` + decision cols or `contract_point_decision`), document reading stages + flagged fields (flag reason, page, quote), dedup 409, `contract_term_version` | contracts, contract360, review |
| 5 Risultati | `saving_result`, `invoice`, `saving_realization`, `savings_goal`, `case_objective`; `/api/savings/results`, goals parse/confirm/progress; `estimated_savings_*` no longer exposed | savings |
| 6 Chiedi, Offerta | `conversation.archived_at`, `case_id`; archive/unarchive; sourced answers incl. honest market refusal; quote `kind/contract_id/case_id/status/stated_terms/missing`; no-benchmark assessment (SQ.assess) + decision | ask, quotes |

## 4. Parity in CI

Workflow `.github/workflows/parity.yml` (PR + push to main): Postgres (pgvector) service → apply all module SQL scripts → `dotnet run` API (`ASPNETCORE_ENVIRONMENT=Parity`, fixtures on) → `vite build` with fixture config + `vite preview` → static server for `reference/` → `reference.capture.spec.ts` (goldens captured in the same runner, so fonts/rasterisation match) → `app.parity.spec.ts` (soft assertions, every screen reported) + `geometry.spec.ts`. Report + diffs uploaded as artifact.

## 5. VERIFY points — verified against the code (2026-11-01)

| Spec point | Reality in the repo | Consequence |
|---|---|---|
| Dedup by hash | `document.checksum` (SHA-256) + partial unique index `ux_document_tenant_checksum`; duplicate upload returns **200** `{alreadyUploaded:true, existingDocumentUrl}`, not 409; no `?asNewVersion`; `document_version` table unused | Keep the 200 contract (frontend shows "già caricato" → open existing); document it in OpenAPI. |
| Unreadable | No status; `Rejected` + `rejection_reason=no_readable_text` (terminal, blob deleted) | Revisione maps `no_readable_text` to the A3 inline error with "Carica un'altra versione". |
| Per-field confidence / page / why-flagged | `extraction_evidence` has value, source_span/page, confidence, decision (threshold 0.90). Critical list exists in code (`ExtractionConfidencePolicy`) but is **not exposed**; **no flag reason** | Add `flagReason` + `critical` to the evidence response (slice 4). |
| Review/validate | `GET /api/contracts/{id}/evidence`, `PATCH /api/contracts/{id}` corrections, `POST /api/documents/{id}/validate` | Reused by Revisione. |
| Conversations restore | No `archived_at`: "archived" = `updated_at < now−7d`; restore bumps `updated_at`; DELETE is hard | Add explicit `archived_at` + `case_id` (slice 6); restore ≠ unarchive. |
| `estimated_savings_*` | Exposed by `GET /api/savings`, `/kpis`, `PATCH`; rendered in savings dashboard | Removed from 4.0 surfaces; new `/api/savings/results` (slice 5). Columns kept. |
| Raffa.Market | Used by Ask packs, contract read (`MarketPriceEstimator`), insights strategy, quotes assessment (P25/P50/P75), renewal priority `benchmarkOpportunity`, re-price job. **No feature flag**; capabilities is a static catalog | Add `askraffa4.noBenchmark`; when on, 4.0 code paths never call Market; market answers refuse honestly. |
| Quote assessment | Percentile benchmark + target saving from P25/P50 | Replaced (new response version) by SQ-style comparison vs own contract/target/other offers/policy. |
| Renewals | `renewal_action` thin; `renewal_alert` internal only; negotiation todos unused by screens; Renewals/Today run on client demo data | `renewal_case` aggregate + projections; old client engine removed. |
| OpenAPI drift | `/api/chat/query` shape stale; several mapped endpoints missing (DELETE conversation/documents, PATCH evidence/{field}, quote reprocess/header, PATCH savings) | Fixed opportunistically when touched. |
| Identity | Roles Admin/Procurement/Legal/Finance/ReadOnly; caller = `oid`; `X-Tenant-Id` header | "Approver" = case-level (`renewal_case.approver_user_id`, default the CPO/Admin); delegations per case. |
| Clock | `IClock` injectable everywhere (5 direct `UtcNow` bypasses in Market/Foundry/warm-up) | Fixture clock honoured by all 4.0 code. |
