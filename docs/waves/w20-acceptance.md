# Wave w20 "Ask says what it cannot do, and does the next best thing" — acceptance runbook (ADR-030)

Operator checklist for wave `w20` (decision record
`.helix/reports/architecture/ADR-030-ask-capability-gaps-and-feedback-loop.md`; ADR-024 w20 footer
clauses 1–5; source: the owner's ruling of 2026-09-22 on the Amazon screenshot — "non devi
rispondere a caso o non dare alternative all'utente"). One section per behaviour, naming the exact
question, the route, the reply's `kind`, and the automated, non-live proof that already runs without
Foundry. Same shape as [`w19-acceptance.md`](w19-acceptance.md).

| | |
|---|---|
| Owner | this change (single task; no Helix fan-out — the wave is one branch, `claude/intelligent-tesla-j3rblh`) |
| Oracles | ADR-030 D1–D6 · ADR-024 w20 footer cl. 1–5 · `.helix/reports/architecture/INDEX.md` w20 section |
| API contract | `web/openapi/raffa-api.v1.json` **1.2.0** — `kind` gains `draft`, `actions[].kind` gains `external`, every reply/message gains a required nullable `payload`, new `POST /api/conversations/{id}/feedback`; regenerated into `web/src/api/generated/schema.ts`, consumed by `client.ts#postConversationFeedback` |
| Screens | `web/README.md` "Ask Raffa" → "Draft and the feedback card" (`/ask`, `/ask/:id`) |
| Automated cover | `dotnet build backend/Raffa.slnx` · `dotnet test` per project (`Raffa.Chat.Tests`, `Raffa.AiGateway.Tests`, `Raffa.AiEval`, `Raffa.Api.Tests`, `Raffa.ArchitectureTests`) · `cd web && npm ci && node node_modules/typescript/bin/tsc --noEmit -p . && npm test && npm run build` · `terraform fmt -check -recursive infra` |

The engine's `reports/execution/wave-close.md` is a fan-out delivery report, **not** the wave
record; there is none for this wave. This document is the record.

---

## 0. Before you start

### 0.1 What the wave is, in one paragraph

A user asked, from a chat bound to their Amazon Web Services order form, *"I have to renegotiate
with Amazon. Can you help me create an email based on the negotiation leverage?"* and got the pink
block "I don't have data I trust enough to answer." followed by a dump of pack facts: the `lever`
lexicon had routed the turn to the savings pack, the `answer` role wrote a zero-based `[0]`, the
grounding guard rejected it twice and `RegenerateOnce` replaced the whole reply. The user had asked
for an **operation** — write/send an email — and Raffa neither said it cannot send email nor did the
next best thing it can plainly do. This wave adds a **capability-gap gate** (five operations, IT/EN,
before the planner), a **fifth reply kind `draft`** whose email is written by a two-agent workflow
over the contract's own pack, guarded by `DraftGuard` and never blanked (a deterministic template is
the floor), an honest `redirect` to Renewals / Portfolio / Contract 360 for the other gaps, and an
**in-chat feedback card** — three quick questions, one call — that stores a feature request and
opens a GitHub issue on `lucalamalfa91/Raffa.ai` with a public-safe body.

### 0.2 The operator sequence

| # | What | How |
|---|---|---|
| 1 | Merge the PR into `main`; `dev` deploys (backend + web); CI applies `chat.sql` (ADR-021: `payload_json` column + `feature_request` table + RLS) | `backend.yml` "Verify schema applied" green |
| 2 | Set the GitHub token in **both** HCP workspaces, `raffa-dev` and `raffa-demo`: sensitive variable `github_feedback_token` = a fine-grained PAT with **Issues: write on `lucalamalfa91/Raffa.ai` only** (no other repo, no other permission); one token per workspace, so either can be rotated alone | HCP UI → workspace variables |
| 3 | HCP VCS apply of `infra/` on each workspace — creates `github-feedback-token`, the `gh-feedback` secret handle and `Feedback__GitHub__*` on the API app (`feedback_github_enabled` is `true` on `dev` **and** `demo`) | confirm the apply in the HCP UI, **then** the API revision restarts |
| 4 | Without step 2 on a workspace: nothing to do — the switch is ANDed with the token's presence, so the API boots with the null publisher and every submission is `status: recorded` | `az containerapp show … --query "properties.template.containers[0].env[?name=='Feedback__GitHub__Enabled']"` → `false` |
| 5 | **`demo` product switches** (owner's ruling 2026-09-22, ADR-016 w20 footer): the same apply as step 3 flips `Invitations__Mail__Enabled` and `Invitations__GuestProvisioning__Enabled` to `true` on `demo`'s API app — the one-line PR ADR-016's w15 clause 14 had reserved. Terraform writes nothing in Entra (`guest_role_assignment_managed` stays `false`), so **a Global Administrator grants `User.Invite.All` to `demo`'s workload identity out-of-band**, the `docs/waves/w15-acceptance.md` §0.3 step 2 command with `demo`'s principal id (`az identity show -g rg-raffa-demo -n id-raffa-demo-workload --query principalId -o tsv`). Skipping it is legitimate: invitations then run in the `NotConfigured` (link-only) shape | `az containerapp show … env[?name=='Invitations__Mail__Enabled']` → `true`; an invite from `demo` arrives by mail, and provisions a guest once the grant is written |

### 0.3 The values every command below needs

```bash
ENV=dev
API=https://<api host>                 # infra output api_fqdn
WEB=https://<swa host>
TENANT=<workspace tenant id>           # X-Tenant-Id
TOKEN=<bearer token from the SPA>     # Authorization: Bearer
H=(-H "X-Tenant-Id: $TENANT" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json")
```

### 0.4 Direct database access

RLS stays on — never disable a policy. `SELECT count(*) FROM feature_request;` through the app role
returns only the current tenant's rows; the superuser sees them all.

---

## W20-1 — the screenshot question gets a drafted email, never an abstain (ADR-030 D1–D3)

Open Contract 360 of a validated contract → **Ask about it** → type the screenshot question:

> I have to renegotiate with Amazon. Can you help me create an email based on the negotiation leverage?

```bash
C=$(curl -s "${H[@]}" -X POST $API/api/conversations -d '{"scopeContractId":"<contract id>"}' | jq -r .id)
curl -s "${H[@]}" -X POST $API/api/conversations/$C/messages \
  -d '{"question":"I have to renegotiate with Amazon. Can you help me create an email based on the negotiation leverage?"}' \
  | jq '{kind, answerMarkdown, subject: .payload.draft.subject, body: .payload.draft.body, citations: (.citations|length), actions: [.actions[].href], followUps, offer: .payload.feedbackOffer.prompt}'
```

Pass: `kind == "draft"`; `answerMarkdown` starts with *"I can't create or send emails from Raffa.ai
yet, but I can help you write the renewal email."*; `payload.draft.body` names the supplier and
carries only dates/amounts that exist in the citations; **no `[n]` in the body**; `citations` non-empty;
actions `/renewals?select=<id>` and `/contracts/<id>`; two follow-ups; the feedback prompt. In the
SPA: the pink abstain block is **absent**, the "Draft email" card shows subject + body verbatim,
**Copy email** copies `subject\n\nbody` and flips to "Copied". Audit: exactly one `chat.drafted` row
for the turn, `abstainGuardIntervened=False` (a live model that fails `DraftGuard` twice still
returns a template email — then the row reads `True`, and the golden set would catch a fixture
regression of the same kind).

Italian twin: *"Puoi scrivermi la mail per il rinnovo Amazon Web Services?"* → the same, in
Italian ("Al momento non posso creare o inviare email da Raffa.ai, però posso aiutarti a scrivere
la mail per il rinnovo.").

Automated: `Raffa.Api.Tests.AskCapabilityGapTests.Scoped_email_request_returns_a_draft_grounded_in_the_contract`
(plus the resume half of the same test: `GET /api/conversations/{id}` carries the identical body);
golden `seeded-capability_gap-email-draft-salesforce-en` (`Raffa.AiEval`, zero interventions).

## W20-2 — no contract to draft for: "which contract?", one supplier per chip (D1/D2)

From the global Ask bar (no scope): *"Puoi aiutarmi a scrivere la mail per il rinnovo?"*

Pass: `kind == "redirect"`, the preface plus *"Dimmi per quale contratto la vuoi…"*, `followUps` =
one "Scrivi la mail per il rinnovo {supplier}" per validated supplier (≤ 5), action `/contracts`;
clicking a chip re-enters W20-1 for that supplier. Naming an unknown supplier (*"Write the renewal
email for Databricks"*) says *"I have no validated Databricks contract"*. An empty workspace gets the
Documents upload action instead.

Automated: `AskCapabilityGapTests.Unscoped_email_request_asks_which_contract_with_supplier_follow_ups`,
`…Email_request_naming_an_unknown_supplier_names_it_and_offers_the_portfolio`; golden
`seeded-capability_gap-email-draft-unscoped-it` (no gateway call).

## W20-3 — reminder / export / PO: honest preface + the screen that already has the answer (D1)

| Question | `kind` | Preface starts with | Action |
|---|---|---|---|
| *Mettimi un promemoria per la disdetta Amazon Web Services* | `redirect` | "Al momento non posso impostare promemoria o eventi in calendario da Raffa.ai, però in Renewals…" | `/renewals?select=<id>` |
| *Export my contracts to Excel* | `redirect` | "I can't export files from Raffa.ai yet, but Portfolio…" | `/contracts` (Documents upload on an empty workspace) |
| *raise a PO for the Microsoft renewal* | `redirect` | "I can't raise purchase orders or talk to your ERP from Raffa.ai yet, but Contract 360…" | `/contracts/<id>` |
| *can you help me send an email to the supplier?* | `redirect` | the send-gap preface — **not** the "Raffa can…" feature tour | Portfolio / upload |

Every row: no AI Gateway call, `chat.redirected`, `payload.gap.key` set, `payload.feedbackOffer`
present in the question's language. A notice **timing** question (*"When must we send the notice
to Salesforce?"*) is **not** a gap — it stays the w19 notice fact.

Automated: `AskCapabilityGapTests` (reminder, export, help-me-send), `Raffa.Chat.Tests.Gaps
.CapabilityGapCatalogTests` (16 positives, 11 negatives), `Gate.DomainGateCapabilityGapTests`;
golden `seeded-capability_gap-reminder-docusign-it`, `seeded-capability_gap-export-excel-en`.

## W20-4 — the feedback card opens a GitHub issue, and the issue carries nothing private (D5/D6)

Under any W20-1/2/3 reply: **Sì** → Q1 (prefilled, editable; the public-GitHub notice) → Avanti →
Q2 chips → Avanti → Q3 chips → **Invia**.

```bash
M=<messageId of the gap turn>
curl -s "${H[@]}" -X POST $API/api/conversations/$C/feedback \
  -d "{\"messageId\":\"$M\",\"answers\":{\"what\":\"Send the email to the supplier from Raffa.ai\",\"frequency\":\"every-renewal\",\"importance\":\"blocking\"}}" | jq .
```

Pass on `dev` (token set): `201`, `status: "issue_opened"`, `issueNumber`, `issueUrl`, `message` =
the confirmation turn (`kind: answer`, "Thanks, I opened issue #N…", one `external` action). In
the SPA the confirmation appears in the thread, "Open issue #N →" opens GitHub in a new tab, and the
card does **not** re-open — not after a reload either. Open the issue: title `[Ask Raffa feedback]
{gap title} (dev)`; body = gap key/title, language, environment, an 8-hex workspace hash, the three
answers, the footer — **no question text, no supplier name, no amount, no e-mail address, no guid**.
A second submit for the same turn → `409` with the same numbers. Audit row
`conversation.feedback.submitted`: `gapKey=… status=issue_opened issueNumber=N`, never the answers.

Pass on a workspace with no token, or with the publisher down: `201`, `status: "recorded"`, "Grazie, la tua
segnalazione … è stata registrata", no action; `feature_request.status` is `recorded` (or
`issue_failed` with `publish_error` set — an operator's concern, never shown).

Automated: `Raffa.Api.Tests.ConversationFeedbackEndpointTests` (201 + external action, recorded
without a publisher, publisher failure and exception, 409, 400 ×2, 404, audit never carries the
answers), `Raffa.Chat.Tests.Feedback.FeatureRequestIssueTextTests` (the allow-list, on the text
that leaves the tenant), web `tests/routes/ask/reply/FeedbackCard.test.tsx`, `AskRoute.test.tsx`
"ADR-030: feedback card", `tests/api/client.test.ts` `postConversationFeedback`.

## W20-5 — the drafting workflow degrades, never blanks (D3)

`Raffa.Chat.Tests.Drafting.NegotiationDraftingWorkflowTests`: fixture → planner then writer, passes
first time (`Attempts == 1`, `Source == Model`); a writer that returns `[1]`/an invented amount twice →
retry names the violation, then the template (`Source == Template`, `Attempts == 2`); gateway outage →
template; planner outage → the writer still writes from the council's plays;
`Chat:Drafting:Enabled=false` or a thin pack → template, no call. `NegotiationEmailTemplateTests
.Template_always_passes_the_draft_guard` (full / fact-only / empty pack, IT + EN). `DraftGuardTests`
(marker, link, guid, internal token, unknown key, numbers not in the pack). `DraftingAgentsPromptTests`
(`Prompts/draft/v1.md` ↔ constants drift).

## AC-1 / AC-2 — the whole wave, build and test

Run on this checkout (Ubuntu 24.04, .NET SDK 10.0.112, Node 22.22, Terraform 1.9.8; **no Docker
daemon** in this harness, so every Testcontainers class — `ChatMigrationScriptTests`,
`ConversationServiceTests`, the invitation/membership/workspace-directory endpoint suites — could
not start a Postgres and is reported as failing here for that reason alone; CI has Docker):

```text
dotnet build backend/Raffa.slnx --configuration Release      Build succeeded. 0 Warning(s) 0 Error(s)
Raffa.Chat.Tests          (Docker classes excluded)         Passed: 353, Failed: 0
Raffa.AiGateway.Tests                                        Passed: 165, Failed: 0
Raffa.AiEval  (golden set, 4 new cases, 0 interventions)     Passed:  83, Failed: 0
Raffa.ArchitectureTests                                      Passed:  40, Failed: 0
Raffa.Api.Tests  (Ask/conversation/feedback suites)          Passed:  85, Failed: 0
web: tsc --noEmit                                            clean
web: vitest                                                  78 files, 1238 tests, 0 failed
infra: terraform fmt -check -recursive                       clean (validate needs registry.terraform.io, blocked here — CI infra.yml runs it)
```

Migration: `20260922171713_AddDraftPayloadAndFeatureRequest` (+ `chat.sql` regenerated,
`ChatMigrationScriptStaleCheckTests` green with no Docker).

## README sweep (this change)

Root `README.md` (one paragraph under Ask), `backend/README.md` (HTTP surface rows for messages +
feedback, new section "Ask Raffa V2 — capability gaps, the drafted email and the feedback loop"),
`web/README.md` ("Draft and the feedback card" bullet, `postConversationFeedback` in the client
list), `infra/README.md` ("Ask Raffa feedback issues" paragraph), `docs/architecture/
ask-raffa-v2-data-flow.md` (§4 sequence, §5 five kinds, §8 provenance rows),
`docs/architecture/product-flow.md` (the capability-gap paragraph), ADR-030, ADR-024 w20 footer,
`INDEX.md` w20 section.

## Promotion sequence

1. Merge → `dev` auto-deploys; schema apply lands the column and the table.
2. Set `github_feedback_token` in `raffa-dev` (HCP, sensitive) → VCS apply → API revision restarts
   with `Feedback__GitHub__Enabled=true`.
3. Walk W20-1…W20-4 on `dev`; check the issue on GitHub.
4. Set `github_feedback_token` in `raffa-demo`, confirm its HCP apply, then tag `demo-v*`; walk
   W20-4 on `demo` too — the issue title reads `(demo)`. Until the token is set there, W20-4 reads
   `recorded` on `demo` with no flag change needed.
5. The same `raffa-demo` apply carries `invitation_mail_enabled = true` and
   `guest_provisioning_enabled = true` (§0.2 step 5). After it, write the out-of-band Graph grant
   for `demo`'s workload identity and re-walk w15's A15-4/A15-5/A15-7 on `demo` — the invitation
   walk w17's known gap 5 left owed. Record the `NotConfigured` shape honestly if the grant is
   not written yet.

## Known gaps that shape acceptance today

| # | Gap | Effect on acceptance |
|---|---|---|
| 1 | The `answer` role's zero-based `[0]` marker (the screenshot's root cause on the fact path) is not fixed; the draft path is immune by design | A fact question whose live answer carries `[0]` still ends as before (retry, then abstain with recovery action). Follow-up: accept `[0]` as `[1]` or instruct the persona harder. |
| 2 | Greeting/off-domain lexicons still win first | *"Ciao, scrivimi una mail…"* is a greeting redirect, not a gap. |
| 3 | The gap lexicon is conservative; the language heuristic is a word-count | A phrasing outside the lexicon gets the old honest abstain; a tie in the heuristic answers in English. |
| 4 | `terraform validate` could not run in this harness (provider registry blocked) | `infra.yml` on the PR is the validate/plan gate. |
| 5 | No e2e case for the card (`web/e2e` runs only in the manual acceptance walk) | W20-4's click path is manual on `dev`. |
| 6 | `demo`'s invitation switches are on from this apply, but its workload identity holds no `User.Invite.All` grant until a Global Administrator writes it (§0.2 step 5) | Mail goes out from `demo`; guest provisioning reads `NotConfigured` (link-only) until the grant lands — the same two-step `dev` went through at w15. |
