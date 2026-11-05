# Ask Raffa conversational reliability

## Discovery and root causes

Ask runs through `web/src/routes/ask/index.tsx` → `POST /api/conversations/ask` →
`ConversationsEndpointExtensions.AskAndAppendAsync` → `AskCopilotService` → gate/planner/pack/answer →
`ConversationService`. Domain services and Postgres RLS enforce tenant scope; conversation reads and
writes additionally require the owning user.

Confirmed causes were:

1. Only model understanding interpreted elliptical `crealo`/`aprilo`; provider degradation routed them
   into ordinary document Q&A.
2. Artifacts lacked a server ID/version/status/contract link, and opening was a transient browser effect.
3. `IntentPolicy.RunsMarketResearch` enabled market research for almost every intent when a portfolio was
   non-empty, adding cost/latency and risking unrelated context on contract questions.
4. A first-call answer-provider failure discarded an otherwise useful authorised pack.

## Architecture implemented

`ArtifactCommandResolver` handles low-entropy create/open commands before any model call. Create requires
a create verb and artifact noun; open requires the persisted current artifact. Neither path accepts a
model/user-selected ID or performs broad retrieval. Artifacts carry a server ID, version, draft status,
linked contract, missing inputs and an allowlisted `OPEN_ACTION_PANEL` event. The SPA executes it only if
that artifact ID exists in the authenticated conversation.

Every fixed intent maps to a maximum retrieval tier: `None`, `ActiveEntity`, `Portfolio`, `Benchmarks`, or
`PublicWeb`. Contract facts and clauses stay on the active entity; portfolio questions stay portfolio-wide;
only comparison, savings, renewal-strategy and market-position intents run benchmark research. Audit detail
records `intent`, `planBasis` and `retrievalNeed`.

On answer-provider failure, `GroundedFallbackAnswer` composes from the already-authorised pack and runs the
same grounding/numeric guards. Provenance says `deterministic-fallback`; an insufficient pack still yields
an honest abstain. Prompt `answer-v2.5.1` treats retrieved snippets/history as untrusted data and forbids
following embedded instructions, revealing prompts, selecting IDs or invoking tools.

Conversation state lives in `conversation.state_json` (`jsonb`): active contract, goal slot, current
artifact, pending action, missing inputs and last successful tool. It updates only after the tenant+user
ownership predicate succeeds. IDs come from authorised scope or structured server replies, never prose;
mutations emit `conversation.state.updated`.

The state now also carries primary and previous supplier, comparison suppliers, last intent, structured
saving target (amount, percentage, currency and window), conversational constraints and a pending artifact
change. It is supplied to turn understanding and reduced again server-side after the authorised plan. A
temporary portfolio question does not erase the primary contract; comparisons do not replace it; quantified
follow-ups replace the current goal. Explicit ellipsis has a deterministic provider-degradation fallback.

Artifact changes are two-phase: an update request stores `CONFIRM_ARTIFACT_UPDATE` and its instruction but
does not execute; only an explicit confirmation creates the next version and emits the allowlisted open
event. This prevents prose or an indirect prompt from silently modifying a persisted artifact.

## Procurement coverage

`ProcurementAdvisory` covers broad but still procurement-related work: sourcing/RFI/RFP/RFQ, supplier
selection, governance and scorecards, supplier/delivery/security risk, due diligence, category strategy,
spend and demand management, licence optimisation, TCO and business cases, make-or-buy, operating policy,
ESG and exit planning. Its pack combines a versioned methodology with authorised portfolio indicators.

Prompt `answer-v2.6` requires five claim classes to remain distinguishable: validated contract facts,
deterministic calculations, market benchmarks, recommendations and assumptions. Proactive output may name
the next decision, real deadline, missing input and safest reversible action, but unsupported dimensions are
`not_evaluable`; prediction never grants permission to invent a score, urgency, saving or date.

## Operations, metrics and runbook

Correlate `chat.*` and `ai.*` using `turnId`/`runId`. Track:

- success/failure by intent and `retrievalNeed`;
- market invocation rate (target zero for `None`/`ActiveEntity`);
- false-success UI events (target zero) and artifact reopen success;
- `fallbackUsed=true` grouped by provenance model;
- p50/p95 duration and provider-reported input/output tokens by prompt version and intent.
- state continuity accuracy: primary supplier retained, comparison removed, latest goal selected and
  constraints respected across the sanitised multi-turn fixture;
- proactive usefulness: grounded next-decision/deadline/missing-input/action coverage without unsupported
numeric or contract claims.

Italian and English are first-class and follow identical policies. `X-Raffa-Lang` controls model and
deterministic replies; language detection is the fallback. Intent lexicons, elliptical references,
artifact commands, interviews, degraded-provider replies and behavioural-memory suggestions all have
IT/EN forms. The sanitised IT/EN fixtures must retain equivalent state, retrieval and UI-event assertions,
not merely translated display text.

No production baseline dataset or provider price table is in this checkout, so monetary and p95 deltas are
not fabricated. Capture the old-policy baseline, replay the sanitised fixture only where consent,
data-residency and contracts permit, then compare duration, tokens, failure and grounded-answer rates.

Runbook:

1. Retrieval mismatch: inspect `intent`/`retrievalNeed`; disable
   `Chat:AskTurn:ReliableRetrievalPolicy` only if the new policy is the cause.
2. Provider/tool failure: verify `fallbackUsed`, guard outcome and absence of false-success UI events.
3. UI-event failure: verify the artifact exists in that conversation; ignore unknown event/resource types.
4. Injection report: preserve hashes/audit IDs, quarantine the document through its workflow, verify no
   cross-contract citation/external action, and never copy source text, personal data or secrets into logs.

## Rollout and rollback

Apply migration `20261105120000_AddConversationState` first, followed by
`20261105130000_AddUserProcurementMemory`; both are additive and backward-compatible. Deploy backend
before/together with the web client; older clients ignore
`uiActions`. Enable internally, run conversational evaluation, compare baseline, then expand gradually.
Emergency rollback sets `Chat:AskTurn:ReliableRetrievalPolicy=false` and rolls back application code. Keep
the additive state column and payloads so artifacts already created remain reopenable.
# Cross-conversation procurement memory

Ask Raffa also maintains a tenant- and user-scoped behavioural profile in
`user_procurement_memory`. It deliberately stores only allowlisted counters (intent, workflow and
transition), an explicitly stated presentation preference and at most five recent supplier labels.
It never stores raw questions, answers, document excerpts, amounts, personal data extracted from
documents or model-generated claims.

The profile is loaded before turn understanding. It may influence presentation and add a suggested
next action, but is labelled as a preference-only input: it cannot choose contract/source IDs,
expand authorization, establish facts or override the current request. Suggestions are non-binding
and never execute tools or external actions.

Retention is rolling 180 days. `GET /api/ask/memory` lets the authenticated user inspect the safe
projection and `DELETE /api/ask/memory` erases learned values and writes a disabled tombstone, so
learning does not silently restart on the next message. Update and deletion emit
`memory.profile.updated` / `memory.profile.deleted` audit events. The separate namespace prevents
memory maintenance from being misclassified as a user-visible `chat.*` turn metric.
The table has PostgreSQL row-level security on `tenant_id`; application queries additionally bind
both tenant and authenticated user. Recent supplier labels are returned only through an explicitly
authorized supplier allowlist (the public inspection endpoint currently omits them).
