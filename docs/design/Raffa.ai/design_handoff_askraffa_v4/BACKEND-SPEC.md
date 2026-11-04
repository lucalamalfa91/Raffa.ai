# AskRaffa 4.0 — Backend spec & gap analysis

Scope: what the **backend** must provide so the front-end in `AskRaffa V4.dc.html` stops being simulated. Grounded in the real repo `lucalamalfa91/Raffa.ai` (.NET modular monolith: `backend/src/Raffa.*`, Postgres + EF, row-level security per `tenant_id`, OpenAPI at `web/openapi/raffa-api.v1.json`). Everything marked **VERIFY** was not fully read (e.g. the 51 KB `documents-contracts.sql` and the OpenAPI request/response bodies) — Claude Code must confirm before building.

Source of truth for business rules: `reference/renewal-case-v4.js` (RC), `reference/savings-quote-v4.js` (SQ), README "Hard product rules".

---

## 0. What already exists (read in the repo)

| Module / table | Today | Notes for AskRaffa 4.0 |
|---|---|---|
| `Raffa.Renewals` · `renewal_action` (unique `tenant_id, contract_id`) | one row per contract: `owner`, `status` (≤20), `action` text | Too thin: this is a to-do, not a case. Keep as the *summary projection* or migrate (see §2.1). |
| `renewal_alert` | milestone alerts (`milestone`, `threshold_days`, `milestone_date`, `status` Active…) | Contract-driven alerts. Reuse for "notice deadline within N days"; **not** the user's "check date" reminder. |
| `renewal_negotiation_todo` | ranked negotiation points per contract (`point_key, topic, rank, current, target, rationale, citation_keys, source, status`) | Maps well to the Contract 360 "Cosa richiede una decisione" cards. Add the user's decision (§2.4). |
| `Raffa.Savings` · `savings_opportunity` | has `estimated_savings_low/high`, `confidence`, `type`, `status`, `owner`, `opportunity_key` | **Conflicts with the product rule "never estimate savings."** See §6. |
| `realized_savings` | `amount, currency, realized_at` per opportunity | Useful, but lacks baseline/period/method/evidence/invoice link. |
| `Raffa.Quotes` · `quote`, `quote_line`, `quote_extraction_job`, `sku_product_mapping`, `negotiation_outcome` | upload → async extraction job → lines (sku, qty, unit, unit_price, list_price, discount, term, source_span/page, confidence, normalized price/term/SKU), checksum, supplier, currency, geography, purchase_date | Good base for Quotes. Has benchmark-oriented fields/endpoints (`/api/quotes/{id}/assessment`, `benchmark-history`) that must change (§6). |
| `Raffa.Market` | market records / benchmarks (`/api/market/records/{id}`) | Product decision: **no market benchmark in 4.0**. Feature-flag off; do not surface. |
| `Raffa.Chat` · `conversation`, `conversation_message` | `user_id, title(48), custom_title, scope_contract_id`; messages with `role, kind, markdown, citations_json, actions_json, interview_json, payload_json, model_id, prompt_version, input_hash`; `/conversations/{id}/restore`, `/feedback`, `feature_request`, web-research usage | Already the right shape for **Chiedi** (recent/archived via restore) and for sourced answers. Archive vs delete must be explicit (§2.8). |
| `Raffa.Documents.Contracts` | `/api/documents` (+ preview, reprocess, prioritise, validate), `/api/contracts` (+ corrections, evidence, strategy, negotiation-steps) | Covers upload, extraction, review/validate, evidence. **VERIFY**: duplicate detection by hash, "unreadable" status, field-level confidence/why-flagged. |
| `Raffa.Identity.Workspace` | workspaces, members, invites, settings | Roles exist; **delegation per case does not** (§2.3). |
| `Raffa.Audit` · `/api/audit` | audit trail | Use for the case timeline's "who did what" (§2.5). |
| `/api/capabilities`, `/api/insights/criticality` | capability flags; criticality | Use `capabilities` as the feature-flag mechanism for 4.0. |

---

## 1. Gaps at a glance

| # | Gap | Needed by screen | Severity |
|---|---|---|---|
| G1 | **Renewal case aggregate** (phase, plan, steps, owner, approver, outcome) | Rinnovi, Pratica, Oggi | Blocker |
| G2 | **Email lifecycle with versions** (Bozza → Da approvare → Approvata vN → Copiata → Invio confermato; invalidation on edit; recipient required; "sent text" capture) | Pratica | Blocker |
| G3 | **Approvals & delegations per case** (approver or explicit delegate; close permission) | Pratica, Oggi | Blocker |
| G4 | **Case timeline/event log** + upcoming milestones | Pratica scheda | High |
| G5 | **Vendor thread**: pasted replies, analysis, dedup, position changes | Pratica | High |
| G6 | **User reminders** ("check date") + notifications | Oggi, Pratica | High |
| G7 | **Contract-point decisions** (negotiate / accept / clarify, by whom, when) feeding the plan | Contratto | High |
| G8 | **Signed contract, agreed-vs-contract comparison, acquisition, outcome** (renewed/terminated, new term, new dates/spend) | Pratica, Contratti, Risultati | Blocker |
| G9 | **Contractual-result savings model** (baseline, period, method, evidence, kind, realized via invoices) + **invoices** | Risultati | Blocker |
| G10 | **Savings goal** (free text → structured target) + per-case objective | Risultati, Pratica | High |
| G11 | **Quote comparison without benchmark** (vs contract price per line, target, other offers, policy), link to contract/case, saved decisions | Offerta | Blocker |
| G12 | **Policy rules** (e.g. liability ≥ 24 months, no auto-renew > CHF 100k) as data | Pratica, Contratto, Offerta | Medium |
| G13 | **Today feed** (prioritised "next action" items per user) | Oggi | Blocker |
| G14 | **Case-scoped Q&A that resumes the step** | Pratica composer | Medium |
| G15 | **Document dedup / unreadable / version** states | Contratti | Medium |
| G16 | **Search/filter/group queries** for Rinnovi (by phase, owner, notice date) | Rinnovi | Medium |
| G17 | i18n of server-generated copy (steps, emails, timeline) | all | Medium |

---

## 2. Data model additions (Postgres, all with `tenant_id` + RLS policy `tenant_isolation`, like the existing tables)

### 2.1 Renewal case (G1)
```
renewal_case
  id uuid pk, tenant_id, contract_id uuid (fk contract),
  owner_user_id, approver_user_id,                    -- approver defaults to workspace CPO
  phase text      -- avvio|piano|negoziazione|accordo|contratto_firma|acquisizione  (6)
  step text       -- machine step key within the phase (see §3.1)
  status text     -- open|waiting_supplier|waiting_internal|closed
  branch text     -- renew|notice|alternatives
  plan_json jsonb -- {products:keep|reduce|expand, term_months, target:{kind:p5|p10|budget|none|custom,text}, policy_ok, limits{}}
  agreed_json jsonb  -- agreed terms {price, term, cap, liab, auth}
  notice_deadline date, contract_end date,           -- snapshot at case open, never overwritten by reminders
  outcome_kind text null  -- renewed|terminated
  outcome_json jsonb null -- {new_start,new_end,new_notice,new_spend,delta,file_document_id}
  opened_at, updated_at, closed_at
  UNIQUE (tenant_id, contract_id) WHERE closed_at IS NULL     -- one open case per contract, ever
```
`renewal_action` becomes a read projection (owner/status/action) or is dropped after migration. Opening a case twice must be idempotent (return the existing one).

### 2.2 Emails (G2)
```
case_email
  id, tenant_id, case_id, kind text (notice|request_info|counter|clarify|request_contract|corrective…),
  version int, state text  -- draft|to_approve|approved|copied|sent|superseded
  to_address text null     -- never generated by AI; required before to_approve
  subject text, body text, attachments_json,
  approved_version int null, approved_by, approved_at,
  copied_at, sent_by, sent_at, sent_body text null  -- text the user says they actually sent
  created_by, created_at, updated_at
```
Rules (enforce server-side): `to_address` mandatory to leave `draft`; any edit of `body/subject/to` creates a new `version` and resets `state=draft`, clears approval (and writes a timeline event "approval invalidated"); only `approver` or an active delegate may approve; **copy never marks sent**; `sent` requires an explicit user confirmation and may carry `sent_body` ≠ approved body (flag the difference). Concurrent edit: optimistic concurrency via `version` (HTTP 409 with current version; client keeps the user's unsaved text).

### 2.3 Delegation & permissions (G3)
```
case_delegation
  id, tenant_id, case_id, user_id, can_approve bool, can_close bool, granted_by, granted_at, revoked_at null
```
Permission matrix (enforce in the API, not the UI): owner → work the case (answer interview, confirm plan, draft, add docs, mark sent); approver → approve emails/versions, close case, grant delegations; delegate → approve and/or close **only if explicitly granted**; participating in a conversation grants nothing. Response bodies include `capabilities: {canWork, canApprove, canClose}` for the caller so the UI can show "who can proceed".

### 2.4 Decisions on contract points (G7)
Extend `renewal_negotiation_todo` (or add `contract_point_decision`): `decision text (negotiate|accept|clarify|null)`, `decided_by`, `decided_at`. When a case is created, `negotiate` points pre-select the clauses in the plan; `clarify` generates a "clarify" email draft suggestion. Also store `policy_ref` and `says`, `why_it_matters`, `proposal`, `source_page` (the card needs all five).

### 2.5 Timeline (G4)
```
case_event
  id, tenant_id, case_id, at timestamptz, actor_user_id|'system'|'supplier',
  type text   -- case_opened|plan_confirmed|email_drafted|email_submitted|email_approved|email_edited_invalidated|email_copied|email_sent_confirmed|reply_added|reply_analysed|decision_recorded|reminder_set|reminder_due|doc_added|doc_checked|signed_added|acquisition_started|closed
  payload jsonb, email_id null, document_id null
```
Append-only. Also mirror to `Raffa.Audit`. "Upcoming" = derived (notice deadline, reminders, offer validity), not stored.

### 2.6 Vendor thread (G5)
```
case_vendor_message
  id, tenant_id, case_id, direction (in|out), received_at, pasted_text text, content_hash text,   -- dedup: unique(case_id, content_hash)
  parsed_json jsonb   -- {positions:{price,term,cap,liab,payment…}, concessions[], open_points[], tone}
  analysed_by_model text, prompt_version text, input_hash text
```
Pasting the same reply twice must be detected (hash) and refused with a friendly message. Multiple vendor contacts = multiple threads (`thread_key`).

### 2.7 Reminders (G6)
```
user_reminder
  id, tenant_id, case_id null, user_id, kind (check_reply|check_contract|custom), due_date date,
  state (set|due|done|dismissed), created_at
```
Daily job flips `set→due` and creates a Today feed item. A reminder never changes the contractual notice deadline; the UI keeps them labelled apart.

### 2.8 Conversations (G14)
Keep `conversation`/`conversation_message`. Add `archived_at timestamptz null` (distinct from soft delete that `restore` handles — **VERIFY** what `restore` undoes), `case_id uuid null`. Case chat messages are stored as `conversation_message` with `kind='case_step'` + `payload_json` (step key, card data) so "Rivedi la panoramica" can replay completed steps. A free question inside a case must (1) answer via the existing sourced-answer path with `citations_json`, (2) append an automatic "back to the pending step — <next action>" message.

### 2.9 Contract outcome & renewed terms (G8)
```
contract_term_version
  id, tenant_id, contract_id, source_document_id, valid_from date, valid_to date, notice_days int,
  annual_value numeric(18,2), currency, auto_renew bool, uplift_pct numeric, is_current bool
```
When a case closes as `renewed`, create a new `contract_term_version` (is_current=true) from the **validated** signed document; Contratti/Rinnovi/Oggi read the current version. `agreed_vs_contract`: store the comparison result `{field, agreed, found, status ok|diff}` on the case document check; the signed file must go through the standard upload → extraction → review → validate pipeline linked via `case_id`.

### 2.10 Savings model (G9, G10)
Replace estimates with verifiable records:
```
saving_result
  id, tenant_id, contract_id, case_id null, supplier_id,
  kind text  -- price|term|clause|volume
  counts_as_negotiation bool,            -- false for volume (quantity reduction, shown apart)
  amount_annual numeric(18,2), currency,
  baseline_desc text, baseline_value numeric, new_value numeric, quantity numeric, method text,
  period_from date, period_to date,
  evidence_document_id uuid,             -- the signed contract
  recorded_at, recorded_by
invoice
  id, tenant_id, supplier_id, contract_id, period date, amount numeric(18,2), currency, document_id
saving_realization
  id, saving_result_id, invoice_id, realized_amount numeric(18,2), computed_at   -- realized only through invoices
savings_goal
  id, tenant_id, user_id, source_text text, pct numeric null, annual_amount numeric null,
  scope_json jsonb ({categories[], supplier_id|null, contract_ids[]}), period text, active bool, created_at
case_objective
  case_id, kind (p5|p10|budget|custom|none), pct numeric null, amount numeric null, text
```
Formulas (must match `savings-quote-v4.js`): contractual = (old unit price − new unit price) × contracted quantity; term/clause results use the formula stored in `method`; **realized** = invoice-proven portion only; quantity reductions never count as negotiation results; objectives are user targets applied to contract spend, never estimates.

### 2.11 Quotes without benchmark (G11, G12)
Extend `quote`: `kind (new|renewal)`, `contract_id null`, `case_id null`, `status (checked|negotiating|signing|signed|declined)`, `decided_by`, `decided_at`, `valid_until date`, `stated_terms jsonb` (payment, uplift, renewal, liability, sla — null = "not stated"), `missing_json`. New table `quote_line_comparison(quote_line_id, contract_unit_price, delta_pct, source_label)`. Assessment (replace benchmark logic): compare each line with the **customer's own contract price** for the same SKU (via `sku_product_mapping`), the **case target**, **other offers** on the same need, and **policy**; verdict ∈ `nocompare | misses_target | meets_target | below_current | above_current | same`; return ≤5 evidence points each with `{text, source}` and `missing[]`. When nothing is comparable return `nocompare` + what can still be checked + what is missing — never a guessed verdict.
```
policy_rule
  id, tenant_id, key text, label, operator, value jsonb, binding bool (binding|guidance), active
```
Seed: liability ≥ 24 months (binding), no auto-renew above CHF 100k (binding), payment quarterly or Net 60 (guidance).

### 2.12 Today feed (G13)
Computed, not stored (or cached per user): `GET /api/today` returns items `{key, kind, caseId|contractId|quoteId|documentId, supplier, contract, title, why, dueDate, dueLabel, daysToDue, phase, actor (user|supplier), ctaKind, canAct, secondary: {kind}}`. Ordering: caller's own actions by `dueDate` asc; then items waiting on others; unassigned notice deadlines ≤120 days only for approvers/CPO; snoozed items excluded (`today_snooze(user_id,item_key,until)`).

---

## 3. State machines

### 3.1 Renewal case (phases → steps)
`avvio` (case opened, branch chosen) → `piano` (interview: products/quantities → term → economic objective [→ policy exception] → confirm plan) → `negoziazione` (draft email → recipient → submit → approve → copy → sent confirmed → waiting_supplier [reminder] → reply pasted → analysed → decision: accept / counter / clarify / escalate) → `accordo` (agreed terms confirmed by owner; policy exceptions require approver) → `contratto_firma` (request contract → waiting_doc → contract added → compared with agreed terms → corrective email loop (new version) → ready for signature → **signed outside Raffa** → signed file added → verified) → `acquisizione` (standard document pipeline → fields reviewed → validated → outcome recorded). Secondary branches: `notice` (non-renewal notice email, same email lifecycle) and `alternatives` (8-step supplier replacement; **current contract stays open until an outcome is recorded**).
Invariants: negotiation completes only with the signed contract + confirmation; an "agreement" is not a completed negotiation; closing requires approver/delegate with `can_close`.

### 3.2 Email — §2.2. 3.3 Quote — `checked → negotiating → signing → signed`, `checked → declined`, any → `checked` (reopen). 3.4 Document — `uploaded → processing → needs_review|failed(unreadable) → completed`; same-hash upload returns the existing document (or creates a new version on explicit user choice).

---

## 4. API (new/changed; follow existing `/api/...` conventions, regenerate `web/src/api/generated/schema.ts`)

**Today** — `GET /api/today?as=me|waiting`, `POST /api/today/{key}/snooze`, `DELETE /api/today/{key}/snooze`.
**Renewals** — `GET /api/renewals` (extend: group/phase/owner/noticeFrom/To/q, `include=nextAction`), `POST /api/renewals` (idempotent open: `{contractId, branch, ownerUserId?}`), `GET /api/renewals/{caseId}` (summary + capabilities), `GET /api/renewals/{caseId}/steps` (completed + current step card, replayable), `POST /api/renewals/{caseId}/answers` (interview), `POST /api/renewals/{caseId}/plan/confirm`, `POST /api/renewals/{caseId}/transfer`, `PUT /api/renewals/{caseId}/delegations/{userId}`, `GET /api/renewals/{caseId}/timeline`, `POST /api/renewals/{caseId}/close`.
**Emails** — `GET/POST /api/renewals/{caseId}/emails`, `PATCH …/emails/{id}` (409 on stale version), `POST …/emails/{id}/submit`, `…/approve` (body: `{version}`; 403 if not approver/delegate), `…/copy`, `…/sent` (`{sentBody?}`), `POST …/emails/{id}/regenerate`.
**Vendor thread** — `POST /api/renewals/{caseId}/vendor-messages` (dedup by hash; returns analysis), `GET` list, `POST …/{id}/decision`.
**Reminders** — `POST/GET/PATCH /api/reminders`.
**Contract points** — `GET /api/contracts/{id}/decision-points`, `PUT /api/contracts/{id}/decision-points/{pointKey}` `{decision}`.
**Documents/Contracts** — upload returns `409 {existingDocumentId}` on duplicate unless `?asNewVersion=true`; `GET /api/documents?attention=true`; review endpoints return per-field `{value, source, page, confidence, flagReason, critical}`; `POST /api/renewals/{caseId}/documents` (signed/draft contract, linked by `case_id`) with `GET …/documents/{id}/comparison` (agreed vs found).
**Savings** — `GET /api/savings/results?period&supplier&category&owner` (contractual / realized / volume apart / objectives), `GET /api/savings/kpis` (extend), `POST/GET /api/savings/goals` (`POST {text}` → parsed draft `{ok, pct, annual, scope, period, rows[]}`; `POST …/{id}/confirm`), `GET /api/savings/goals/active/progress` (signed, objectives, gap, actionable contracts), `POST /api/invoices` (+ reconciliation job).
**Spend (Spesa e risparmi)** — `GET /api/savings/spend?from&to&basis=accrual|cash` (fiscal years named by the calendar year they start in; both default around the current one): per fiscal quarter and per validated contract, in the reporting currency, `actual` · `committed` · `autoRenewal` · `toDecide`, the timeline segments (`documented` from the contract or a recorded renewal, `rolledOver` when the notice passed, `autoRenewal`, `toDecide` at the last documented price), the next decision, and per fiscal year how many contracts have a documented term. Nothing is estimated: no history is reconstructed, another currency is converted only at the admin's budget rate, and a contract without dates, value, rate or (cash) billing period is listed in `excluded` with its reason. The reporting settings live on the workspace: `GET/PATCH /api/workspaces/{tenantId}/settings` carries `reportingCurrency` (closed ISO 4217 list, `supportedCurrencies`), `fiscalYearStartMonth` (1-12, null = not set, calendar year) and `budgetRates` (units of the reporting currency per unit; the whole set, `{}` clears it); only an Admin writes, each change is audited, and changing the currency clears rates the same request does not set.
**Quotes** — `POST /api/quotes` (existing), `POST /api/quotes/{id}/link` `{contractId|caseId|new}`, `POST /api/quotes/{id}/confirm-extraction`, `GET /api/quotes/{id}/assessment` (new shape: verdict, comparisons, points≤5, missing, evaluable), `POST /api/quotes/{id}/decision` `{status}`, `POST /api/quotes/{id}/counter-proposal` (creates a draft `case_email`, never sends).
**Chat** — `POST /api/chat/query` (existing; add `caseId`, `resumeStep=true`), `POST /api/conversations/{id}/archive|unarchive`, list filter `?state=recent|archived|case`.
**Policy** — `GET/PUT /api/workspaces/{tenantId}/policy-rules`.
**Capabilities** — add flags `askraffa4.guidedFlow`, `askraffa4.noBenchmark`, `savings.results` so the old surfaces can be switched off per workspace.

All mutations return the updated aggregate (so the UI re-renders from the server), carry an `Idempotency-Key`, write a `case_event` and an audit record, and enforce the permission matrix from §2.3.

---

## 5. Jobs, AI tasks and integrations
- **Extraction** (exists): add *flag reasons* ("why check this") and a duplicate/unreadable outcome.
- **LLM tasks (prompt-versioned, `input_hash` stored as in chat)**: (1) interview-to-plan, (2) email drafting (never invents recipient/addresses; cites the clause/page it relies on), (3) vendor reply analysis → `parsed_json`, (4) agreed-vs-contract comparison, (5) free-text goal parser → `{pct, annual, scope, period}` (replace the prototype's regex), (6) sourced Q&A (exists) with "cannot answer" path, (7) quote-line mapping.
- **Daily jobs**: reminders due, notice-deadline alerts (`renewal_alert`), stale-case flag ("stalled N days"), invoice reconciliation.
- **Out of scope for 4.0 (state clearly in UI + docs)**: sending email from Raffa (manual copy/send only), e-signature (outside Raffa), market benchmarks.
- **Notifications**: in-app only for 4.0 (Today feed); email notifications optional later.

## 6. Conflicts with the current backend that need a product decision
1. `savings_opportunity.estimated_savings_low/high` + `confidence` → prototype shows **no estimates**. Recommendation: stop exposing these fields in the API response for 4.0 (keep columns), add `saving_result`/`case_objective` (§2.10), and migrate "opportunity" semantics to *objective*.
2. `Raffa.Market` and quote `assessment`/`benchmark-history` → hide behind a capability flag; implement the no-benchmark assessment (§2.11) under the same route with a new response version.
3. `renewal_action` unique per contract → superseded by `renewal_case` (§2.1); provide a migration that creates a case for each action with an owner.
4. Email **sending** is not a Raffa capability; if a mail integration exists in the repo, keep it disabled for this flow.

## 7. Acceptance tests (backend)
1. Opening a case twice for one contract returns the same case.
2. An email cannot leave `draft` without a recipient; the recipient can never be generated.
3. Editing an approved email → new version, state `draft`, event `email_edited_invalidated`, approval cleared.
4. A non-approver (owner/buyer) gets 403 on approve and on close; an explicit delegate with `can_approve` succeeds; `can_close` is separate.
5. Pasting the same vendor reply twice → second call rejected with the first message id.
6. `copy` leaves state `copied`, never `sent`; `sent` requires explicit confirmation and records `sent_body`.
7. Concurrent PATCH on an email → 409 with current version; no silent overwrite.
8. Closing as `renewed` creates a `contract_term_version` and moves the contract's notice deadline; Today, Rinnovi, Contratti and Risultati read the new values.
9. Savings: a signed contract yields a `saving_result` (kind=price) and `realized=0` until invoices exist; a volume result never counts toward negotiation totals; no endpoint returns an estimate.
10. Quote assessment with no matching contract price, no target, no other offer returns `nocompare` + `missing[]` and never a market verdict.
11. Free-text goal "voglio risparmiare il 20% sui contratti IT l'anno prossimo" → `{pct:20, scope: IT categories, period: next FY}`; ambiguous input returns `ok:false` + the one question to ask.
12. Duplicate upload returns 409 with the existing document id; unreadable scan → `failed` with a recoverable message.
13. RLS: every new table rejects cross-tenant reads/writes (reuse the repo's tenant-isolation test pattern).
