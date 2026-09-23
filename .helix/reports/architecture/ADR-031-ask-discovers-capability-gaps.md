# ADR-031 — Ask Raffa discovers capability gaps by itself: a capability investigator, a proposed feature, and a human-approved GitHub issue

- **Status**: accepted
- **Date**: 2026-09-23
- **Deciders**: product-owner (the ruling below: "non deve essere deterministico … deve essere l'intelligenza di Raffa"; the request "deve passare da GitHub ed essere approvata da un HITL") + software-architect (placement beside the answer, the follow-up message, fail-open, the verdict contract) + security-architect (what a model-written feature text may carry into a public issue, who may approve) + client-architect (the additive `payload.gap.discovery` member)
- **Amends**: ADR-030 D1 ("deterministic first … no model call decides whether a turn is a gap") — for in-domain turns only, and only for a follow-up after the answer; ADR-030 D5 (the issue allow-list gains the generic discovery texts; every issue gains the human-approval gate); ADR-030 A ("an interview turn … calls no model") — the one capability check runs beside it
- **Locked citations**: ADR-002 (Raffa.Chat's allow-list `[SharedKernel, AiGateway]`); ADR-004 (the `analyst` role, strict JSON, no tools); ADR-011 (audit carries outcomes, never content); ADR-012 (one OpenAPI contract → one generated client); ADR-024 (engine, guards, reply kinds unchanged); R-ASK-02/03 (greeting, off-domain and needs-document turns call no gateway — the golden set's zero-call cases)

## Context and problem statement

The owner asked Ask Raffa, three times: *"puoi scrivere un report per riportare l'andamento
del 2026 al CFO?"*. The first answer was "No CFO contract has been uploaded and validated" (the
gate took the role for a supplier name); the second an interview; the third a ranking of
critical contracts. None said the true thing: **Raffa has no report feature**. ADR-030 already
does this for five operations — send an email, draft an email, set a reminder, export a file,
raise a PO — but only when a regex lexicon matches the phrasing. "Scrivere un report" matches
none, and no lexicon will ever list every feature a buyer can ask for.

The owner's ruling: this must not be deterministic. Raffa's own intelligence must recognise that
a request could become a feature, investigate what Raffa can and cannot do, and — when the
feature is missing — ask the user whether they want to contribute by answering the same
interview as the email gap. The request goes to GitHub and is **approved by a human** before
anything is built.

## Decision drivers

- **The model decides, the server bounds.** Whether a turn is a gap is a judgement over the
  whole capability map; everything the judgement produces that a user sees or GitHub publishes
  is cleaned, bounded and server-worded.
- **Never worse than before.** The answer is exactly what ADR-030 would give; a failed, slow,
  unsure or unusable verdict adds nothing to it. A false gap would interrupt the user with a
  proposal they did not want; the prompt and the confidence threshold both lean towards "question".
- **Privacy by construction.** The repository is public; a model-written feature text comes
  from the user's own words, so the prompt's law is backed by a server-side scrub.
- **A human decides what gets built.** Raffa can propose, never approve.
- **No latency.** The owner's second ruling (2026-09-23): the check must not slow the answer. It
  runs beside the answer, the user gets the standard reply at once, and a proposal — if any —
  arrives afterwards as a separate message.
- **Cost where it earns its keep.** One small strict-JSON call per fresh in-domain turn; zero on
  every turn the gate already decides.

## Considered options

1. **Grow the regex catalog** — rejected: the ruling is explicitly against it, and it can never
   be complete.
2. **Let the `answer` role say "I can't" in prose** — rejected: no structured gap, no card, no
   issue; the ADR-030 option 1 argument still holds.
3. **An `IAiGateway.ClassifyAsync` label set** — rejected: that role is pinned to the document
   taxonomy (`AiDocumentType`, see `DomainGate`'s own note), and a label cannot carry the
   feature description the issue needs.
4. **A capability investigator on the `analyst` role, before the planner, fail-open** — first
   implementation; rejected by the owner for the latency it put in front of every answer.
5. **The same investigator beside the answer, its proposal a separate follow-up message** (chosen).

## Decision outcome

### D1 — The capability investigator

`Raffa.Chat.Application.Gaps.CapabilityInvestigator` calls `IAiGateway.AnalyzeAsync` with the
agent `capability-investigator` (`CapabilityInvestigatorAgent`, version `gaps-v1`, mirrored in
`Prompts/gaps/v1.md` with a drift test). Its input is the question, its language, the ten
`CapabilityCatalog` screens, the six abilities of the Ask chat itself and the five
`CapabilityGapCatalog` entries — never a pack, never a supplier list. Its strict output:
`rationale`, `verdict` (`question | supported | known-gap | gap`), `confidence`, `knownGapKey`,
`nearestCapabilityKey`, a `feature` (kebab-case key, title, operation verb phrase and one-line
description in English and Italian) and up to two `alternativeQuestions` Ask can already answer.

- `question` / `supported` → nothing.
- `known-gap` → the named catalog entry, unless its own veto matches (a notice-timing question is
  never a send request, whoever recognised the verb). The follow-up offers that entry's
  alternative: one chip per supplier that re-enters the drafted-email path, or the screen that
  already holds the answer.
- `gap` at or above `Chat:GapInvestigation:MinConfidence` (default `medium`) → a
  `CapabilityGap.Discovered(...)`: key `discovered:<slug>` (fits the 40-char `gap_key` column),
  `GapOrigin.Investigator`, `GapAlternative.NearestCapability`, and a `GapDiscovery` record.

**Fail-open**: the kill switch (`Chat:GapInvestigation:Enabled`, default true), a gateway
failure, a timeout (`TimeoutSeconds`, default 12), an unparseable payload, a low confidence or a
feature text that is empty once cleaned all return "nothing found" — no follow-up. The turn's own
audit row says whether a check was started (`gapInvestigation=started|off|skipped`); the verdict
is logged, and an appended follow-up writes its own row (`ask.capability_follow_up`, resource
`capability_follow_up`, the gap key only).

### D2 — Where it runs: beside the answer, never in front of it

`AskCopilotService.AskAsync` decides, after the gate and the scope resolution, whether a turn is
checked: the gate label is `InDomain`, the turn is fresh (typed by the user, not an interview
option, a consent or a decline resolved by key) and the caller passed a `CapabilityCheckSlot` (the
conversation endpoints do; other callers get no check). It then **starts** the check through
`CapabilityCheckDispatcher.Start` and goes on answering — the interview, the planner, the pack and
the answer role run while the investigator's model call is in flight. The check runs in its own DI
scope and tenant scope, because the request's scoped `IAiGateway` writes its audit rows through a
scoped DbContext that is gone once the response is sent, and it is never cancelled with the request.

Greeting, off-domain, legal, capability, needs-document and catalog-gap turns keep their zero
gateway calls (R-ASK-02/03, the golden set).

The gate also stops reading role and metric acronyms as supplier names (`CFO`, `CEO`, `Board`,
`KPI`, …): the screenshot's first turn was a needs-document redirect for a "CFO contract".

### D3 — The follow-up message

`ConversationsEndpointExtensions.AskAndAppendAsync` stores the question and the answer exactly as
before, then:

- the check has **already finished** and found something → the follow-up is stored right after
  the answer and returned in the same response as `followUpMessage` (the stored-message shape of
  `GET /api/conversations/{id}`); `capabilityCheck: null`;
- the check is **still running** → the response says `capabilityCheck: "pending"`;
  `CapabilityCheckDispatcher.AppendWhenDone` stores the follow-up from the background when the
  check completes, and the web client reads the conversation back every 2 s for up to 16 s until
  a Raffa message whose `payload.capabilityCheckFor` is the answer's id appears. It stops at a new
  question, another conversation or an unmount;
- the check found nothing → nothing is stored, `capabilityCheck: null`.

A follow-up is appended only while its answer is still the conversation's last message: when the
user has already asked something else, a late proposal would land in the middle of another
exchange, so it is dropped (and logged). It never counts as "the last Raffa turn" a typed message
answers — an interview right before it is still the one a free-text reply answers, on the server
and in the client.

The follow-up is a `redirect` turn with `payload.gap` + `payload.feedbackOffer`, plus two new
optional payload members: `followUps` (its chips — a stored message has no follow-up column of its
own, and this turn is only ever read back from the conversation) and `capabilityCheckFor` (the
answer it follows). Its markdown opens with `CapabilityGapCopy.CheckedOpening` ("Ho verificato cosa
sa fare Raffa.ai per la tua richiesta.") and continues:

- a discovered gap: `CapabilityGapCopy.Preface` with the investigator's operation and a
  **server-authored** alternative for the nearest screen (`CapabilityGapCopy.NearestAlternative`),
  then `DiscoveredLeadIn` ("there is no feature for this yet … a person on the team approves it
  before it is built"); the one action opens that screen (Contract 360 without a contract falls back
  to Portfolio; nothing when the nearest thing is Ask itself); the chips are the investigator's
  alternative questions; the card asks to *propose* the feature by name ("Vuoi proporre «Report per
  il management» come nuova funzionalità?") and its first question is prefilled with the description;
- a known email gap: "which contract?" with one chip per supplier (the named one alone when the turn
  named one), each re-entering the drafted-email path;
- another known gap: its preface and the screen that already holds the answer.

`payload.gap` gains an optional `discovery` object (`titleEn`, `descriptionEn`,
`nearestCapability`, `confidence`, `investigatorVersion`). Every wire addition is optional and
additive; turns stored before this ADR omit them.

### D4 — What a discovered text may carry

`Gaps.DiscoveredGapText` cleans every feature text before it is shown, stored or published:
links, e-mail addresses, every token that carries a digit (amounts, dates, years, "Q3"), every
supplier name the tenant has on file and every markdown character are removed; the text is
bounded (title 80, operation 100, description 240 chars) and a text left with fewer than three
letters makes the discovery unusable. The slug drops digit segments and is bounded to 28 chars.
Alternative questions go back into Ask as the user's next message and never into an issue, so
they keep supplier names and numbers but lose links, e-mails and markup. The prompt's first law
says the same thing; the server does not rely on it.

### D5 — The human in the loop

Every issue Raffa opens — catalog or discovered — is a **proposal**:

- `FeatureRequestIssueText.Labels` always adds `awaiting-approval`, adds `ai-discovered` for a
  discovered gap, and strips `approved` from any configured label list, so no configuration can
  make Raffa pre-approve its own request.
- The body gains a "Human approval" section (approve with the `approved` label, decline by
  closing as not planned) and, for a discovered gap, a "Discovered by Ask Raffa" section with the
  generic title, description, nearest capability, investigator version and confidence. The
  title reads `[Ask Raffa feature proposal] <English title> (<env>)`.
- `.github/workflows/feature-request-approval.yml` runs on `issues: labeled`: when `approved` is
  added to an `awaiting-approval` issue by a non-bot with `admin`/`write` permission it removes
  `awaiting-approval` and records who approved it and when; anyone else gets the label removed
  and a comment. Only `GITHUB_TOKEN` with `issues: write`; the issue body is never executed.
- The card's public notice and the confirmation turn tell the user that a person on the team
  reviews the request and must approve it before it is built.

## Consequences

- **Good**: the screenshot's question gets the standard answer at once and, a moment later, an
  honest "not yet", the nearest screen, questions Ask can answer now and an offer to propose the
  feature; the regex's misses of the five known gaps are caught too; nothing reaches the backlog
  without a person's decision; no answer waits for the check.
- **Bad**: one extra `analyst` call (≈ 2–3k input tokens) per fresh in-domain turn; when the answer
  is faster than the check (an interview, a notice fallback) the client reads the conversation back
  a few times; a proposal can arrive after the user has read the answer, and is dropped when they
  have already asked something else; many users may propose the same feature under different slugs
  — triage deduplicates on GitHub.
- **Neutral**: no schema change (`feature_request` stores `discovered:<slug>` in `gap_key`; the
  follow-up's chips and link ride in the existing `payload_json`); the fixture gateway carries a
  deterministic double (a request for a report-like deliverable is the one discovered gap) so CI and
  the golden set stay stable; an interview or a notice fallback is still retrieval-free and
  answer-free — the tests assert exactly one capability check and nothing else.

## Verification

- `Raffa.Chat.Tests`: `CapabilityInvestigatorTests` (verdicts, scrub, veto, threshold, fail-open
  incl. timeout, the fixture double, the strict schema), `CapabilityInvestigatorPromptTests`
  (drift, privacy law), `FeatureRequestIssueTextTests` (approval section, labels, proposal
  title), `DomainGateCapabilityGapTests` (roles are not suppliers).
- `Raffa.Api.Tests`: `AskCapabilityGapTests` (the screenshot question: the standard interview, then
  the follow-up inline; a check slower than the answer appended afterwards; a late follow-up dropped
  once the conversation moved on; a typed reply still answers the interview; the kill switch),
  `ConversationFeedbackEndpointTests` (a discovered gap published as a proposal), and the zero-call
  assertions of the interview / notice / web-consent / abstain tests restated as "one capability
  check, nothing else".
- `Raffa.AiEval`: `seeded-capability_gap-discovered-report-it` (the golden set now also knows the
  `interview` kind).
- Web: `askViewModel.test.ts` (follow-up mapping, dedupe, lookup, bounded polling, interview still
  pending behind a follow-up) and `AskRoute.test.tsx` (inline and polled follow-up).
