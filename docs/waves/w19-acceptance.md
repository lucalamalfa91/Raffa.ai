# Wave w19 "Ask Raffa wow answers: evidence-based savings, a bound notice date, a negotiation strategy that writes Renewals" — acceptance runbook (Q1/Q2/Q3)

Operator checklist for wave `w19` (`.helix/reports/context/waves/w19-requirements.md`;
decision record `.helix/reports/architecture/waves/w19.md`; ADR-024 w19 cl. 12–23 (amended);
ADR-003/ADR-009/ADR-011/ADR-028 (the negotiation TODO entity); ADR-012 cl. 49–55; ADR-020
37–41; source `.helix/inputs/next/2026-09-16-ask-raffa-wow.md`). One section per wow question,
naming the exact screenshot phrasing, the URL/route, the reply's `kind`, and the automated,
non-live golden proof that already runs without Foundry. Same shape as
[`w18-acceptance.md`](w18-acceptance.md).

| | |
|---|---|
| Owner | task `E31/F04/US01/T01` (`w19-integration`), story `us-01-w19-final-integration` |
| Oracles | `w19-requirements.md` items NW-76…NW-97 (22 items; NW-86…90 overflowed to W20) · decision record `waves/w19.md` (22 decisions, 7 ADRs amended) · ADR-024 w19 cl. 12–23 · ADR-003/009/011/028 · ADR-012 cl. 49–55 · ADR-020 37–41 |
| API contract | `web/openapi/raffa-api.v1.json` — every route below is quoted from it, **including** `GET`/`PUT /api/renewals/{id}/negotiation-todos`: unlike w18's phrase-edit gap, this route reached the OpenAPI document, `schema.ts` **and** `client.ts` in full (grepped, this checkout — `web/src/api/client.ts` carries 30 references, `schema.ts` 5) |
| Screens | `web/README.md` "Ask Raffa" (`/ask`, `/ask/:id`, `?scope=<contractId>`, the persistent bound-contract chip) and "Renewals" (`/renewals`, `?select=<contractId>`, the Negotiation TODOs table) |
| Automated cover | `dotnet build backend/Raffa.slnx` · `dotnet test` per project (`Raffa.AiGateway.Tests`, `Raffa.Chat.Tests`, `Raffa.Documents.Contracts.Tests`, `Raffa.Insights.Tests`, `Raffa.Renewals.Tests`, `Raffa.Api.Tests` — Postgres/Testcontainers, real containers, confirmed working in this harness) · `cd web && npm ci && node node_modules/typescript/bin/tsc --noEmit -p .` (typecheck; no `npm run typecheck` script exists — same inherited gap w17/w18 recorded) · `npm test` (vitest) |

This task's own architecture-decision-in-force is **ADR-016: "the acceptance walk is recorded,
never dispatched; promotion is the operator's HITL gate."** This document is that record — the
`curl`/click-path steps below are the runbook an operator runs against deployed `dev` once the
wave is promoted; this task does not dispatch them against a live environment and does not cut a
`demo-v*` tag. w19 writes no Terraform and no workflow: `git diff --stat origin/main..HEAD --
infra .github` is empty on this checkout — delivery-manager and cloud-architect are both `PASS`
this wave (`waves/w19.md` §3 seat roster: "No `infra/`/SKU/apply." / "No CI-YAML/promotion.").

The engine's `reports/execution/wave-close.md` is a fan-out delivery report, **not** the wave
record, per every prior wave's own acceptance doc — not cited as current here either.

---

## 0. Before you start

### 0.1 What the wave is, in one paragraph

Three "wow" questions were demoed and each came back honestly wrong — not crashed, not
fabricated, just the wrong deterministic path. **Q1** ("quali contratti sono mal posizionati sul
mercato? su quali posso lavorare per risparmiare…") was stolen by the `mercato` keyword into
Quote check's routing copy before `risparm*` ever got a turn. **Q2** ("When must we give notice
to this supplier?", asked from an open Contract 360) abstained "cannot determine reliably"
because the global Ask bar never passed the contract's own scope, and "notice" was not a
structured keyword. **Q3** ("sul contratto di AsterCloud GmbH quali sono i maggiori punti su cui
posso contrattare nel prossimo rinnovo?") abstained honestly on the wrong evidence — Italian
`contrattare`/`rinnovo` missed the strategy lexicon entirely, so a working `StrategyPackBuilder`
was never even called. Nineteen tasks across five epics (epic-27…31) fix the shared plumbing
(scope threading, planner lexicon, supplier resolution, contract-scoped RAG, priced-lines parity,
real citation ids) once, then land **Q2** (the smallest end-to-end wow) and **Q3** (which reuses
the same plumbing) inside the wave's 20-task cap. **Q1** is the heaviest flow (a new candidate-set
rule, a mal-position calculator, a two-bucket chat list) and — per the wave's own written,
pre-decided overflow rule (`w19-requirements.md` §5, "if the cap binds, the Q1 flow overflows to
the head of W20, never demoted") — **is not built this wave**. It is recorded here, not silently
dropped, and remains `must` in epic-32/W20.

### 0.2 How this checkout was built

Nineteen live tasks in five phases (`.helix/reports/plan/slices/w19.yaml`), fanned out by Helix
onto `integration`. Verified on this branch, not assumed: `git log --merges --oneline
origin/main..HEAD | grep -E "wave/E(27|28|29|30|31)-"` shows 42 merge commits for 17 of the
wave's 18 dependency branches (several were re-merged more than once across the phase-barrier
loop — normal, not a defect). The eighteenth, `wave/E27-F01-US01-T01` (planner-lexicon, NW-79),
has no merge commit of its own **because it was never merged into `integration` standalone** —
`wave/E27-F02-US01-T01` (engine-scope) branches from it (`slices/w19.yaml`: `engine-scope
depends_on: [planner-lexicon]`) and carries it in transitively. Confirmed, not inferred:
`git merge-base --is-ancestor 5da23c27 HEAD` (the planner-lexicon commit) exits `0`, and the
actual lexicon it ships — `IntentPlanner.PortfolioMarketPositionPattern`/`RenewalStrategyPattern`
(now matching `contrattare`/`rinnovo`/`punti`) — is present and green in this checkout's own
`Raffa.Chat.Tests.Planning.IntentPlannerTests` (234/234 green in the whole project, see §AC-1
below). This checkout is task `E31/F04/US01/T01` itself, forked from `integration` at `9a2c985b`
(merge of `wave/E31-F03-US01-T01`, q3-persist, the last of the eighteen to land).

**Base-SHA note (OQ-w19-001, recorded rather than silently worked around).** `origin/main` was
re-fetched from this checkout: `380397f2`, five commits **ahead of** this wave's own merge-base
(`1f93f417`) on a line of work this wave never touches — document-viewer overlay, hung-processing
recovery, a Portfolio/Renewals "ready" filter (`git diff --stat 8654b304..380397f2`, all under
`backend/src/Raffa.Documents.Contracts/Application/Extraction/*`,
`backend/src/Raffa.Documents.Contracts/Application/Preview/*` and matching `web/` routes — zero
overlap with the Q2/Q3 files this task touches). `w19-requirements.md` §7 already names this as
an open question with its own assumption in force ("the operator merges `origin/main` into the
wave base before fan-out… W19-A1"); it did not happen before this fan-out started, exactly as
w15/w16's own `OQ-w1x-001`-class entries recorded the same gap for their waves. This task does
not merge or rebase onto `origin/main` — reconciling the two lines is the `integration → main` PR
review's job, not a single phase-5 task's, and every fact this document states was verified on
**this checkout** (`9a2c985b`, then the 5-project build/test run below), not assumed from either
branch's history.

### 0.3 The operator sequence

| # | What | How |
|---|---|---|
| 1 | Merge `origin/main`'s five-commit lead into the wave branch, then the wave PR into `main` | resolves §0.2's base-SHA note before anything is promoted |
| 2 | `dev` deploys on push (`backend.yml`, `web.yml`) | the image tag is the merged sha |
| 3 | One interactive sign-in on deployed `dev` | `GET /api/workspaces` is `200` |
| 4 | Walk Q2, then Q3 (this document) | Q1 is not walked — it does not exist yet (§0.1) |
| 5 | Re-run the non-live golden assertions below as a sanity check on the deployed image's own build | `dotnet test` / `npm test`, unchanged from this document |

### 0.4 The values every command below needs

```bash
ENV=dev
RG="rg-raffa-${ENV}"
API=$(az resource show --resource-group "$RG" --name "ca-raffa-${ENV}-api" \
        --resource-type Microsoft.App/containerApps \
        --query "properties.configuration.ingress.fqdn" -o tsv)
API="https://${API}"
# TOKEN: access token the SPA acquires (api://raffa-dev-api/Raffa.Read Raffa.Write)
# TENANT: workspace id (GET $API/api/workspaces -> workspaces[].id)
# CONTRACT: a validated, auto-renewing contract's id (Portfolio or Contract 360)
```

**Header posture.** Every tenant-scoped route below sends `Authorization: Bearer $TOKEN` and
`X-Tenant-Id: $TENANT`. Never `X-Role`, `X-Workspace-Role` or `X-User-Id` (NW-05, unchanged this
wave).

---

## Q1 — portfolio mal-position / 2026 savings (NW-86…90) — **overflowed to W20, epic-32 (queued)**

> Screenshot: *"quali contratti sono mal posizionati sul mercato? su quali posso lavorare per
> risparmiare un po di soldi sull'anno 2026?"* — `w19-requirements.md` §5's own written overflow
> rule; product-owner lock 6 (R-SYS-02 narrowed).

**Not walked. Not a golden case in this wave.** `AskIntent.PortfolioMarketPosition` exists as a
planner destination (`AskIntent.cs:39`, added by NW-79 precisely so this screenshot phrasing
never falls into `QuoteRoute`/unscoped Clause RAG — `IntentPlanner.cs:137-149`), and it is
correctly exempted from the scoped-turn rules NW-76 adds (lock 4 — a `PortfolioMarketPosition`
question is always portfolio-wide, even asked from an open Contract 360). But **no pack is built
for it yet**: `AskCopilotService.BuildInDomainReplyAsync`'s own `packItems` switch
(`AskCopilotService.cs:519-535`) has no `AskIntent.PortfolioMarketPosition` case, so it falls to
the default empty pack and an honest `abstain` — never a fabricated ranking, never the old
Quote-check misroute either. This is proved directly, not left to chance, by
`Raffa.Api.Tests.ScopedAskEndpointTests.Unseen_scope_id_does_not_refuse_a_portfolio_market_position_question`,
whose own doc comment states it verbatim: *"That intent's own pack is not yet built (a later
task's scope — NW-86/87/88/89 in the same wave record), so this question still abstains."* The
candidate-set rule (option 3, "Active + validated + impacting 2026 costs"), the mal-position %
calculator, and the chat's two-bucket list (actionable-in-2026 vs locked-for-2026) are epic-32's
scope, unchanged and un-demoted at the head of W20.

**Do not re-open this as a defect** when walking `dev`: asking the screenshot question today
correctly returns `kind: "abstain"`, never the pre-wave Quote-check routing copy and never a
half-built ranking.

---

## Q2 — the notice date is bound to a real clause, never "which supplier" (NW-91/NW-92/NW-93/NW-94)

> Screenshot: *"When must we give notice to this supplier?"*, asked from an open Contract 360.
> `w19-requirements.md` §4; ADR-024 w19 cl. 22; tasks `E27/F02/US01/T01` (scope, prerequisite),
> `E30/F01/US01/T01` (notice pack), `E30/F02/US01/T01` (notice fallbacks).

**Click path.** Open a validated contract's **Contract 360**, click **Ask about it** (this binds
`scopeContractId` — NW-77). In the new chat, type **"When must we give notice to this
supplier?"** or the bar's own notice chip. The reply must be an **`answer`** stating the
cancellation-deadline **date** (never "cannot determine reliably", never a "which supplier"
question even though the pronoun alone names nothing) — with a two-CTA citation card ("Open
contract" + "Open at this span") when the extracted clause resolved to a real document page, or
the fact alone otherwise. A persistent `.tag-neutral` chip above the thread continues to name the
bound contract for the rest of the conversation, including after a page reload/resume (NW-78).

**Server-decided, before any pack reaches the AI gateway — five cases, all provable without
Foundry** (`AskCopilotService.BuildNoticeFallbackReplyAsync`, short-circuited ahead of the normal
`packItems` switch in `BuildInDomainReplyAsync`):

| # | Condition | Reply | Citations | Action |
|---|---|---|---|---|
| 1 | Known deadline **+** a matching clause with a real page | `answer`, the date | fact + deep-linked clause (real `contractId`/`documentId`/`page` — NW-83, feeds the two-CTA card, NW-93) | `navigate` → `/contracts/{id}` (360 Review) |
| 2 | Known deadline, no matching span | `answer`, the date | fact alone (never a fabricated page) | `navigate` → `/contracts/{id}` |
| 3 | No deadline, a clause names one in its own words | `answer`, quoting the clause verbatim | the clause | `navigate` → `/contracts/{id}` |
| 4 | Neither known | `abstain`, naming **this contract's own supplier** | none | `navigate` → `/contracts/{id}` (never the generic ask-hint recovery) |
| 5 | No contract in scope at all (no conversation scope, no resolvable named supplier) | `abstain`, never "which supplier" | none | `navigate` → `/contracts` (Portfolio) |

`autoRenewal=false` states "no notice window applies, the contract ends on `endDate`" instead of
a fabricated deadline; a known deadline with a `renewalTermMonths` on file adds "if missed,
renews for N month(s)"; **"N days" is only ever a calculator value (`WhenYouMustMove.DaysLeft`,
signed — "N days ago" for a passed deadline), never `EndDate − CancellationDeadline`** — the
task's own forbidden shortcut, and NW-92's `must (floor)`.

```bash
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d "{\"scopeContractId\":\"$CONTRACT\"}" "$API/api/conversations" | jq '{id, scopeContractId}'
# CONVERSATION=<the returned id>

curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"question":"What'"'"'s the cancellation deadline for this contract?"}' \
  "$API/api/conversations/$CONVERSATION/messages" | jq '{kind, answerMarkdown, citations, actions}'
# kind: "answer"; answerMarkdown contains a yyyy-MM-dd date; never "which supplier"

# Unscoped sibling (no conversation scope at all) -- case 5:
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"question":"What'"'"'s the cancellation deadline?"}' \
  "$API/api/chat/query" | jq '{kind, actions}'
# kind: "abstain"; actions: [{ kind: "navigate", href: "/contracts" }] -- Portfolio, never a guess
```

**Non-live golden proof (already runs, no Foundry, no `az` needed).**
`Raffa.Api.Tests.NoticeFallbackEndpointTests` — one `[Fact]` per case above, host-level (real HTTP
round trip through `WebApplicationFactory`, InMemory EF Core + a `RecordingAiGateway` wrapping
`FixtureAiGateway`), asserting `Assert.Empty(recordingGateway.Calls)` on **every** case (the AI
gateway is never reached for a notice question) alongside the `kind`/citation/action shape in the
table above:

- `Deadline_with_a_spanned_clause_answers_citing_the_fact_and_the_deep_linked_clause` — case 1;
  asserts the clause citation's `href` contains `/viewer?page=`, `page == 4`, and a real
  `contractId`/`documentId`.
- `Deadline_with_no_matching_clause_answers_the_date_with_one_unspanned_citation` — case 2;
  asserts `answerMarkdown` **contains** the deadline's `yyyy-MM-dd` string.
- `No_deadline_with_a_matching_clause_quotes_the_clause_text` — case 3; asserts `answerMarkdown`
  equals the seeded clause text verbatim.
- `No_deadline_and_no_clause_abstains_naming_the_contract_not_which_supplier` — case 4; asserts
  `answerMarkdown` **does not contain** "which supplier" and **does contain** the contract's own
  type/supplier name.
- `Unscoped_notice_question_abstains_to_portfolio_never_a_which_supplier_guess` — case 5; asserts
  the action href is `/contracts` and the seeded contract's own id never appears anywhere in the
  response body.

Run just this class: `dotnet test backend/tests/Raffa.Api.Tests --filter
FullyQualifiedName~NoticeFallbackEndpointTests` — 5/5 green on this checkout (part of the
312-passed `Raffa.Api.Tests` run in §AC-1 below).

---

## Q3 — AsterCloud's next-renewal negotiation points, ranked and written to Renewals (NW-95/NW-96/NW-97)

> Screenshot: *"sul contratto di AsterCloud GmbH quali sono i maggiori punti su cui posso
> contrattare nel prossimo rinnovo?"* — `w19-requirements.md` §5; ADR-024 w19 cl. 23; tasks
> `E31/F01/US01/T01` (q3-route), `E31/F02/US01/T01` (point-ranker), `E31/F03/US01/T01`
> (q3-persist).

**Click path.** From Ask (global bar or `/ask`), type the screenshot question, or its tested
English twin **"What should we negotiate before the {supplier} renewal?"** — both match
`IntentPlanner.RenewalStrategyPattern`'s widened lexicon (`contrattare`/`rinnovo`/`punti`/
`negotiat*`, narrowed so bare `rinnovo` never also matches the plain verb "rinnovano" a
structured renewal-window question uses). The reply must be a ranked **`answer`** — never an
abstain — citing the resolved contract's **top 3** grounded negotiation points (never the old
generic seven-lever dump), plus calc/tenant/market/raffa citations, plus a **server-injected**
`Navigate` action to `/renewals?select={contractId}`. Click it: Renewals opens with that row
already selected and its **Negotiation TODOs** table shows the **full** ranked set (not just the
chat's top 3) — `NegotiationTodoList.tsx`, read back from `GET
/api/renewals/{id}/negotiation-todos`. Tick one **Mark done**, ask the same Q3 question again:
the reply's action is not duplicated and the ticked row is still `Done`.

**What actually composes the answer** (`AskCopilotService.BuildRenewalStrategyWithEvidenceAsync`
→ `BuildRenewalStrategyPackAsync(persistTodos: true)` → `BuildNegotiationPointsPackAsync`):

1. **Rank** — `Raffa.Insights.Application.NegotiationPointRanker.Rank` emits a point **only** when
   grounded (a priced line's benchmark band, a clause, an assessed risk, a payment term) — never
   the ungrounded seven-lever dump the older `PricedLineNegotiationCalculator` still produces for
   `/api/contracts/{id}/strategy`. Fixed order: above-band price → uncapped/high liability →
   auto-renew+short notice → SLA/credits → term/volume → payment terms.
2. **Persist — the whole ranked set, before the answer is composed.** `persistTodos: true` upserts
   every grounded point to `RenewalNegotiationTodoService` (idempotent on `(tenant_id,
   contract_id, point_key)`: a repeat ask never duplicates a row, never un-ticks a `Done` row, and
   a point that stops grounding on a later ask becomes `Superseded`, not deleted).
3. **Narrate — the chat pack is capped at 3.** Same ranked list, `.Take(3)`, so a point ranked #4
   or #5 is still in Renewals even though chat never mentions it.
4. **Inject — the `/renewals?select=` action is server-built, never the model's.**
   `BuildInDomainReplyAsync`'s `isQ3PersistTurn` branch (exactly `AskIntent.RenewalStrategy` with
   a resolved named contract) calls `CapabilityRouting.ResolveActions([HowTo(RenewalsKey)],
   routingContext)` — the same catalog-route builder every other deep-link in this codebase uses —
   and `Concat(...).Distinct()`s it onto whatever the model's own `actionKeys` resolved to, so a
   repeat ask never renders the identical button twice.

```bash
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"question":"What should we negotiate before the AsterCloud GmbH renewal?"}' \
  "$API/api/chat/query" | jq '{kind, actions, citationCount: (.citations | length)}'
# kind: "answer" (never "abstain"); actions contains { kind: "navigate", href: "/renewals?select=<contractId>" }

curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" \
  "$API/api/renewals/$CONTRACT/negotiation-todos" | jq 'map({pointKey, rank, status})'
# every grounded point, ordered by rank -- not just the chat's top 3

curl -s -o /dev/null -w '%{http_code}\n' -X PUT \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" -H "Content-Type: application/json" \
  -d '{"pointKey":"auto-renew-short-notice"}' "$API/api/renewals/$CONTRACT/negotiation-todos"
# 200 -- Admin or Procurement only (403 otherwise), authz checked before the route id is even parsed
```

**Non-live golden proof (already runs, no Foundry).** Four classes, all InMemory EF Core + a
fixture/recording AI gateway, no Postgres, no `az`:

- `Raffa.Insights.Tests.NegotiationPointRankerTests` — the pure calculator: grounded-only, fixed
  order, above-band price outranks an ungrounded payment-terms point, the generic dump is never
  produced (135/135 green in the whole `Raffa.Insights.Tests` project, §AC-1).
- `Raffa.Api.Tests.AskNegotiationPointsPackTests` — `Chat_pack_is_capped_at_the_top_three_ranked_points`
  asserts `pack.Count == 3` against a contract grounding five of the six canonical topics;
  `Persist_todos_true_persists_the_whole_ranked_set_not_just_the_chat_top_three` reads all five
  back; `A_repeat_call_does_not_duplicate_rows` re-runs it and asserts the row count is still 5.
- `Raffa.Api.Tests.AskRenewalStrategyTodoUpsertTests` — a real `AskCopilotService.AskAsync` turn
  (not the pack helper directly): `A_live_Q3_turn_upserts_todos_before_the_answer` asserts
  `reply.Kind == ReplyKind.Answer` **and** a persisted, `Open` `auto-renew-short-notice` row exists
  the moment the call returns; `A_repeat_Q3_turn_adds_no_duplicate_open_todo`;
  `A_ticked_done_todo_survives_a_repeat_Q3_turn` ticks one row `Done` via
  `RenewalNegotiationTodoService.SetDoneAsync` directly, re-asks, and asserts it is still `Done`.
- `Raffa.Api.Tests.AskQ3RenewalsDeepLinkActionTests` — the one piece those three do not cover, the
  reply's own `actions[]`: `A_live_Q3_turn_injects_the_renewals_select_action` asserts
  `reply.Actions` contains exactly one `Navigate` action whose `href` is
  `/renewals?select={contractId}`; `A_repeat_Q3_turn_injects_the_action_exactly_once` re-asks and
  asserts the action list still holds exactly one (the `Concat(...).Distinct()` merge is itself
  idempotent, not just the upsert underneath it); `The_injected_action_names_this_turns_own_contract_not_a_stale_one`
  proves two tenants sharing the identical supplier name and question each get their **own**
  contract's id in the link (tenant scoping alone keeps them apart).

Run just these four: `dotnet test backend/tests/Raffa.Api.Tests --filter
"FullyQualifiedName~AskNegotiationPointsPackTests|FullyQualifiedName~AskRenewalStrategyTodoUpsertTests|FullyQualifiedName~AskQ3RenewalsDeepLinkActionTests"`
plus `dotnet test backend/tests/Raffa.Insights.Tests --filter
FullyQualifiedName~NegotiationPointRankerTests` — all green on this checkout (part of the 312- and
135-passed project runs in §AC-1).

---

## AC-1 / AC-2 — the whole wave, build and test

Run 2026-09-18, this checkout (`9a2c985b`), Windows harness, Docker Desktop reachable (`docker
info` succeeded — real Postgres/Testcontainers, not skipped).

```bash
dotnet build backend/Raffa.slnx
# Build succeeded. 0 Warning(s), 0 Error(s). Time Elapsed 00:00:17.12  -- exit 0

dotnet test backend/tests/Raffa.AiGateway.Tests --no-build
# Passed! Failed: 0, Passed: 156, Skipped: 0, Total: 156  -- exit 0

dotnet test backend/tests/Raffa.Chat.Tests --no-build
# Passed! Failed: 0, Passed: 234, Skipped: 0, Total: 234  -- exit 0  (Postgres/RLS, real container)

dotnet test backend/tests/Raffa.Documents.Contracts.Tests --no-build
# Passed! Failed: 0, Passed: 230, Skipped: 0, Total: 230  -- exit 0  (Postgres/RLS, real container)

dotnet test backend/tests/Raffa.Insights.Tests --no-build
# Passed! Failed: 0, Passed: 135, Skipped: 0, Total: 135  -- exit 0

dotnet test backend/tests/Raffa.Renewals.Tests --no-build
# Passed! Failed: 0, Passed: 214, Skipped: 0, Total: 214  -- exit 0  (Postgres/RLS, real container;
# includes RenewalNegotiationTodoServiceTests + RenewalNegotiationTodoRlsCrossTenantIsolationTests)

dotnet test backend/tests/Raffa.Api.Tests --no-build
# Passed! Failed: 0, Passed: 312, Skipped: 2, Total: 314  -- exit 0  (Postgres/RLS, real container;
# 2 skips are RemovedMemberRetrievalTests/MembershipRemovalEndpointTests T7b/T7d, pre-existing,
# unrelated to this wave -- not caused or touched by w19)

cd web && npm ci
# added 133 packages, audited 134 packages, 0 vulnerabilities  -- exit 0

npm run typecheck
# npm error Missing script: "typecheck"  -- no such script exists (inherited from w17/w18); the
# real check is run directly instead, same substitution w18's own final-integration task used:
node node_modules/typescript/bin/tsc --noEmit -p .
# (no output) -- exit 0, ZERO type errors. An improvement over w18's own known-gap row 4 (18
# errors across 14 files that wave) -- those mock-fixture/fixture-shape gaps are closed as of
# this checkout, confirmed by re-running the identical command, not assumed carried-forward.

npm run lint
# npm error Missing script: "lint"  -- still no lint tooling anywhere in web/ (no eslint
# dependency, no config, no script) -- inherited gap, unchanged since w17; re-confirmed this wave,
# not fixed here (this task's own file scope is the acceptance doc alone)

npm test
# Test Files  67 passed (67)
#      Tests  1128 passed (1128)  -- exit 0
```

**Every named project is green. Zero failures anywhere in this run.** AC-1 and AC-2 are both
satisfied by the commands above, run on this checkout, not copied from an earlier wave.

---

## README sweep (this task)

Swept `backend/README.md` and `web/README.md` for the changed surfaces this task's own coding
objective names (scoped engine, planner lexicon, binding chip, `?select=`, TODO list, two-CTA
card, notice pack). Most of the eighteen dependency tasks already carried their own README
hygiene in full — verified by grep, not assumed:

- **Already documented, thoroughly, before this task ran:** the scoped engine (NW-76, HTTP
  surface table row for `POST /api/conversations/{id}/messages`), the TODO list end to end
  (NW-85/97, backend "Insights" section + the `GET`/`PUT
  /api/renewals/{id}/negotiation-todos` HTTP rows + `web/README.md` "Negotiation TODOs" bullet),
  `?select=` (NW-84, `web/README.md` "Renewals" section), the two-CTA card (NW-83/93,
  `CitationCard.tsx`'s own doc comment plus `web/README.md`'s citation-click paragraph), and the
  planner lexicon in outline (NW-79, cited inline in "The V2 engine" section).
- **Genuinely missing, added by this task:**
  - `backend/README.md` — a new subsection, **"Ask Raffa V2 — the notice pack"**, between "The V2
    engine" and "Ask Raffa — capability catalog": `BuildNoticePackAsync`'s three-item pack order,
    the "N days" never-subtracted rule, and all five `BuildNoticeFallbackReplyAsync` cases with
    their reply/citation/action shape — zero prior mentions of `BuildNoticePackAsync`, "notice
    pack" or `NoticeFallback` existed anywhere in the file before this edit (grepped, case
    -insensitive, before/after).
  - `web/README.md` — a new bullet, **"Bound-contract chip"**, in the "Ask Raffa" section: the
    persistent `.tag-neutral` pill (task E27/F04/US01/T01, NW-78) was implemented and tested
    (`askViewModel.test.ts`, `index.tsx`'s own doc comments) but the README's "Ask Raffa" section
    previously documented only the **new-chat** scoped brief (NW-56/NW-77), never the chip that
    persists above the thread through an entire live or resumed conversation (NW-78's own
    "survives resume" requirement).

No infra/workflow sweep was needed — `infra/README.md` and the root `README.md` are unaffected by
this wave (§0.2's diff-stat note).

---

## Promotion sequence

w19 writes no Terraform and no workflow file. It is **one** `integration → main` PR, after
§0.3's `origin/main` merge. `git tag -l "demo-v*"` **read on this checkout: `demo-v1`, `demo-v2`,
`demo-v3`.** No `demo-v4` — this task does not cut one; per ADR-016, a `demo` promotion, if the
operator chooses it, is a separate act after the `dev` walk above, following the identical
tag-then-approve mechanism every prior wave's promotion section already describes.

---

## Known gaps that shape acceptance today

| # | Gap | Effect on acceptance | Disposition |
|---|---|---|---|
| 1 | **Q1 (portfolio mal-position / 2026 savings) does not exist yet.** `AskIntent.PortfolioMarketPosition` routes correctly (never Quote check) but has no pack — every such question abstains. | The first of the three screenshot wows is not walkable on `dev` this wave; §"Q1" above names the exact honest behaviour so it is not re-opened as a new defect. | **by design, recorded** — epic-32, head of W20, still `must`, never demoted (`w19-requirements.md` §5) |
| 2 | **`origin/main` carries five commits this wave's branch does not**, on an unrelated line (viewer overlay, hung-processing recovery, a Portfolio/Renewals ready filter). | The `integration → main` PR must reconcile this before merge; skipping it would silently drop that unrelated work, not this wave's own. | **operator action at the PR**, named in §0.2/§0.3, not resolved by this task |
| 3 | **No lint tooling exists in `web/` at all** (no eslint dependency, no config, no script). Not new to w19 — first recorded w17, re-confirmed every wave since. | The parent story's own Definition of Done names `npm run lint`; there is nothing to run. `tsc --noEmit` (run directly, no `npm run typecheck` script either) is the only static check this repo has today, and it is clean. | **inherited**, unchanged |
| 4 | **Two `Raffa.Api.Tests` skips** (`RemovedMemberRetrievalTests`/`MembershipRemovalEndpointTests`, both named `T7b`/`T7d`). | Neither is new to this wave, neither touches Ask/Renewals/notice code; the suite is still 0 failed. | **pre-existing**, unaffected by w19 |

**Closed this wave, worth recording so a future reader does not re-discover it as new:** w18's own
known-gaps row 4 (`tsc --noEmit` failing with 18 errors across 14 files, from two independent
pre-existing mock-shape gaps) is **gone** — the identical command now exits clean with zero
errors, re-run and confirmed on this checkout, not assumed carried forward from an earlier wave's
say-so.
