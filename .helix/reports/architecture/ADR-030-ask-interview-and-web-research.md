# ADR-030 — Ask Raffa: the interview on ambiguous questions, and web research as an isolated, consented exception

- **Status**: accepted
- **Date**: 2026-09-22
- **Deciders**: software-architect (owner) + security-architect (isolation, consent, audit) + client-architect (wire contract, consent dialog) + ux-ui-designer (interview and alert surface) + product-owner (the third screenshot: "Did you over all my contract?", and the explicit request for a web-research agent behind an authorization alert)
- **Locked citations**: ADR-024 §Engine (gate → planner → pack → answer → guards; four reply kinds; "never the web, no tools on the answer role"); `inputs/requirements.md` R-ASK-03/05/06, R-AI-03, R-ASK-08, R-SYS-02; ADR-011 (input-hash logging, no content in audit); ADR-009 (RLS on every tenant table); ADR-021 (idempotent migration scripts); ADR-012 (one generated client); NW-80 (soonest-deadline pick), NW-94 case 5 ("never ask which supplier")
- **Amends**: ADR-024 §Engine (fifth reply kind `interview`; forced plans; NW-94 case 5 narrowed); R-AI-03 gains an explicit, isolated exception
- **Wave**: post-w19 product feedback (screenshot 3)

## Context and problem statement

Screenshot 3: "Did you over all my contract?" reached the `answer` role through the planner's
fall-through (`StructuredFact`, five soonest-ending contracts as the pack) and came back as an
honest but useless abstain — "your question is ambiguous, so I should not guess" — with a dead
"Open Ask Raffa" button on the Ask screen itself. The product owner asked for two things:

1. When a question is ambiguous, Raffa should **interview** the user — one short question with
   clickable options — then reason and answer.
2. Among the possible resolutions, Raffa should be able to **go to the public web** with an
   agent specialised in procurement topics, **always asking the user's authorization with an
   alert** first.

The second conflicts with two locked rules: ADR-024's "never the web" and R-AI-03's "no tools on
the `answer` role" (held by `FoundryAnswerClientTests`, which asserts the request has no `tools`
key). This ADR does not weaken either rule; it adds a separate, opt-in path beside them and makes
the isolation structural.

## Decision drivers

- An ambiguous turn must cost **zero retrieval and zero model calls** until the user has said
  what they mean (Appendix C rule 6).
- Options must be **server-authored and resolved by key**: a tampered label can never change what
  Raffa does (R-ASK-08, R-SYS-02).
- Web research must be **impossible without a consumed, single-use consent**, impossible when
  the workspace or the environment has not opted in, bounded by a budget, and **visibly
  unverified** in every rendering.
- Tenant data must never leave: the request type that reaches the web has **no slot** for a pack.
- Every existing guard (`GroundingGuard`, `NumericGuard`) stays in force on whatever is shown.

## Considered options

1. **No web at all** — keep ADR-024 verbatim; interview only. Rejected: the product owner asked
   for the web path explicitly, and a procurement buyer legitimately needs market practice and
   supplier news Raffa's store does not hold.
2. **Grounding on the `answer` role** (a `web_search` tool on the same deployment, the pack in
   the same request). Rejected outright: it breaks R-AI-03, puts the tenant's pack and the web in
   one prompt, and makes "did anything leave Raffa?" unanswerable.
3. **A separate `research` role, client and route, gated three times, consented per question,
   guarded and labelled** — chosen.

## Decision outcome

### A. The interview (ADR-024 §Engine amendment)

1. **`interview` is the fifth reply kind** (`ReplyKind.Interview`, wire `"interview"`,
   `ConversationMessageKind.Interview`). An interview turn retrieves nothing and calls no model.
2. **Decided deterministically, before any pack**: `IntentPlanResult` carries `Basis`
   (`Lexicon | LegacyRouter | Fallback | FollowUp | Forced`) and `Candidates`;
   `AmbiguityDetector` combines planner fall-through, vague/deictic phrasing, several readings, a
   supplier with several contracts and an unscoped notice question into
   `Clear | Unsure | Ambiguous`. Only `Ambiguous` interviews. A model stage (`Chat:Interview:
   UseModelStage`) exists as an option and is off.
3. **Stage 3**: a model abstain whose reason says "ambiguous" becomes the interpretation menu
   instead of the abstain block. The model never authors an option.
4. **Options resolve by key.** Each option carries a server-authored `InterviewResolution`
   (`Intent?`, `ContractId?`, `SupplierName?`, `RewrittenQuestion`, `WebResearch?`) persisted in
   `conversation_message.interview_json` and **never sent to the client**. The client answers with
   `interviewAnswer { messageId, questionKey, optionKey | freeText }`; the server reloads the turn
   under RLS, resolves the key and runs the **normal** pipeline on the rewrite with
   `AskTurnHints` (forced intent/contract/supplier, `SuppressInterview`). Every rewrite re-plans
   to its option's intent (round-trip tested).
5. **Never two interviews in a row**; free text answers the pending interview as a follow-up.
6. **NW-94 case 5 is narrowed**: an unscoped notice question asks "which contract?" (up to four,
   by cancellation deadline) instead of abstaining to Portfolio. **NW-80's soonest-deadline pick
   becomes the fallback** when the interview is off (`Chat:Interview:Enabled=false` restores the
   previous behaviour exactly).

### B. Web research — the exception to R-AI-03, and why it is not a loophole

7. **A separate role.** `AiGatewayModelOptions.Research` (nullable: null means the feature does
   not exist in that environment; **no fallback** to the `answer` deployment).
   `IAiGateway.ResearchAsync(AiResearchRequest)` → `AiResearchResult`. `FoundryResearchClient`
   calls the Azure OpenAI **Responses API** (`openai/v1/responses`) with **exactly one** tool,
   `{ type: "web_search" }`, and strict JSON output. It is the only request type in the gateway
   with a `tools` member; `FoundryAnswerClientTests` still proves the `answer` request has none and
   `FoundryResearchClientTests` proves the inverse (root keys ⊆ `{model, instructions, input,
   tools, text, max_output_tokens}`, no pack substring, refusal without a deployment).
8. **Structural isolation.** `AiResearchRequest` has **no pack, evidence or tenant text field**;
   `WebResearchComposer.ComposeAsync(query, purpose, language)` takes three strings; it is the only
   caller of `ResearchAsync` and the only producer of `PackCorpus.Web` (a source-scan test holds
   both). The query is `WebQuerySanitizer`'s: the user's own words minus the "search the web"
   phrase, every amount, percentage, date, money shorthand, e-mail and URL; supplier names stay.
9. **Three gates, every one re-checked when a consent is spent**: `Chat:WebResearch:Enabled`
   (kill switch, default **false**) → the workspace Admin's opt-in
   (`workspace.web_research_enabled`, default false, `PATCH /api/workspaces/{tenantId}/settings`,
   audited) → a daily per-tenant budget (`chat_web_research_usage`, RLS, one atomic conditional
   upsert). A closed gate on an explicit request is a **redirect that says which gate**, never a
   silent fall-through. Only a question matching the procurement topic lexicon (market practice,
   supplier news, public benchmark ranges, negotiation tactics) is ever offered the web.
10. **Consent per question.** The offer is an interview question with `presentation: "consent"`
    — the SPA renders it as `role="alertdialog"` with the exact query, "Nothing from your contracts
    leaves Raffa", "results are not verified", focus on "No, stay in Raffa", Escape declines.
    `allow` carries the server-authored `WebResearchRequest` and is **single-use**
    (`consumedAt` on the turn; a replay is **409**); `decline` re-plans the same words without the
    web phrase and is audited as a decline. Consent is **never stored as a preference**. An
    interview menu's "search the public web" option is **not** a consent: it forces the
    `WebResearch` intent, whose own consent question is the only authorization.
11. **Guards on the way back.** `WebGuard` (≥ 1 source; every source absolute https to a public
    DNS host; every `[n]` in range; every URL in the prose is a returned source) →
    `NumericGuard` against the sources' snippets → `GroundingGuard` on the web pack. A failure is
    an **abstain that names the hosts**, never a retry (each call is budgeted); `offTopic` is a
    refusal. The reply is `answer` with `citations[].corpus = "web"`, `provenance.sources =
    ["web"]`, `provenance.unverified = true`; the client shows the banner and files web sources
    under "Web · unverified" as `target="_blank" rel="noopener noreferrer"` links. Web results are
    **never indexed and never merged into an `answer`-role pack**.
12. **Audit** (ADR-011: hashes and counts, never the query or a URL): `chat.interviewed`
    (`webConsent=`), `chat.web_research_authorized` (queryHash, purpose),
    `chat.web_research_declined`, `chat.web_researched` (queryHash, outcome, sourceCount,
    guardIntervened), `chat.web_research_refused` (gate), `ai.researched` on the gateway, and
    `workspace.settings.web_research_enabled`.
13. **Infra.** `research` is an optional key of `model_roles` (Terraform validation relaxed);
    `scripts/foundry_research_probe.py` proves the Responses API + `web_search` in the region
    before any environment binds it; `dev` binds the role and flips `Chat__WebResearch__Enabled`
    only after the probe passes; `demo` stays off until promotion (ADR-016). If the probe fails,
    the fallback is Foundry Agent Service + Grounding with Bing Search behind the same
    `AiResearchResult`.

## Consequences

- Positive: the screenshot's question becomes a two-click answer; web research exists without
  touching the `answer` role or any pack; every gate, consent and outcome is auditable.
- Negative: two new lexicons (IT/EN) to tune; a consent dialog is friction by design; the web
  path costs one budgeted Foundry call per consent and is unavailable until the probe passes.
- Neutral: `ReplyProvenance` gains `Unverified`; the wire gains `interview`, `provenance.unverified`,
  a `web` corpus value and the settings endpoint — all additive.

## Verification

- `Raffa.Chat.Tests`: `AmbiguityDetectorTests`, `InterviewPlannerTests`, `IntentPlannerBasisTests`,
  `IntentPlannerWebResearchTests`, `WebGuardTests`, `WebQuerySanitizerTests`,
  `WebResearchTopicLexiconTests`, `WebConsentInterviewTests`, `WebResearchComposerTests`,
  `WebResearchPromptTests`, `WebResearchIsolationTests`, `ChatMigrationScriptTests`.
- `Raffa.AiGateway.Tests`: `FoundryResearchClientTests` beside the unchanged `FoundryAnswerClientTests`.
- `Raffa.Api.Tests`: `AskInterviewTests`, `AskWebResearchConsentTests` (kill switch, opt-in,
  consent, allow → one research call + web answer, replay → 409, decline, budget, menu offer),
  `WorkspaceSettingsEndpointTests`.
- Web: `ConsentDialog.test.tsx`, `EvidenceCard`/`evidenceGrouping`/`ReplyBody`/`askViewModel`/
  `AskRoute`/`MembersRoute`/`client` tests for the web corpus, the banner, the dialog and the toggle.
