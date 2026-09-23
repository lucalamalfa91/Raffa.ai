# ADR-031 — Ask Raffa discovers capability gaps by itself: a capability investigator, a proposed feature, and a human-approved GitHub issue

- **Status**: accepted
- **Date**: 2026-09-23
- **Deciders**: product-owner (the ruling below: "non deve essere deterministico … deve essere l'intelligenza di Raffa"; the request "deve passare da GitHub ed essere approvata da un HITL") + software-architect (placement before the planner, fail-open, the verdict contract) + security-architect (what a model-written feature text may carry into a public issue, who may approve) + client-architect (the additive `payload.gap.discovery` member)
- **Amends**: ADR-030 D1 ("deterministic first … no model call decides whether a turn is a gap") — for in-domain turns only; ADR-030 D5 (the issue allow-list gains the generic discovery texts; every issue gains the human-approval gate); ADR-030 A ("an interview turn … calls no model") — the one capability check may precede it
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
- **Never worse than before.** A failed, slow, unsure or unusable verdict leaves the turn exactly
  as ADR-030 answered it. A false gap hijacks a real question; the prompt and the confidence
  threshold both lean towards "question".
- **Privacy by construction.** The repository is public; a model-written feature text comes
  from the user's own words, so the prompt's law is backed by a server-side scrub.
- **A human decides what gets built.** Raffa can propose, never approve.
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
4. **A capability investigator on the `analyst` role, before the planner, fail-open** (chosen).

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

- `question` / `supported` → nothing changes.
- `known-gap` → the named catalog entry, unless its own veto matches (a notice-timing question is
  never a send request, whoever recognised the verb). The regex's misses now reach the drafted
  email and the other ADR-030 alternatives.
- `gap` at or above `Chat:GapInvestigation:MinConfidence` (default `medium`) → a
  `CapabilityGap.Discovered(...)`: key `discovered:<slug>` (fits the 40-char `gap_key` column),
  `GapOrigin.Investigator`, `GapAlternative.NearestCapability`, and a `GapDiscovery` record.

**Fail-open**: the kill switch (`Chat:GapInvestigation:Enabled`, default true), a gateway
failure, a timeout (`TimeoutSeconds`, default 8), an unparseable payload, a low confidence or a
feature text that is empty once cleaned all return "nothing found". The audit row of the turn
records the outcome only: `gapInvestigation=off|skipped|failed|question|supported|low-confidence|unusable|known-gap|gap`.

### D2 — Where it runs

In `AskCopilotService.AskAsync`, after the gate and the scope resolution, before the switch —
so before the interview, the planner, any pack and the answer role — and only when the gate
label is `InDomain` **and** the turn is fresh: typed by the user, not an interview option, a
consent or a decline resolved by key (`AskTurnHints` carry a forced intent/contract/supplier or
a consent). Greeting, off-domain, legal, capability, needs-document and catalog-gap turns keep
their zero gateway calls (R-ASK-02/03, the golden set). The investigator is serial: running it
beside the pack would let a discarded in-domain path write its side effects (negotiation todos,
savings opportunities).

The gate also stops reading role and metric acronyms as supplier names (`CFO`, `CEO`, `Board`,
`KPI`, …): the screenshot's first turn was a needs-document redirect for a "CFO contract".

### D3 — The reply

A discovered gap is answered exactly like an ADR-030 redirect: `kind: redirect`,
`payload.gap` + `payload.feedbackOffer`, no retrieval. The markdown is
`CapabilityGapCopy.Preface` with the investigator's operation and a **server-authored**
alternative for the nearest screen (`CapabilityGapCopy.NearestAlternative`), then
`DiscoveredLeadIn` ("there is no feature for this yet … a person on the team approves it before
it is built"). The one action opens that screen (Contract 360 without a contract falls back to
Portfolio; nothing when the nearest thing is Ask itself — the Ask screen never links to itself);
the follow-ups are the investigator's alternative questions. The card asks to *propose* the
feature by name ("Vuoi proporre «Report per il management» come nuova funzionalità?") and its
first question is prefilled with the description.

`payload.gap` gains an optional `discovery` object (`titleEn`, `descriptionEn`,
`nearestCapability`, `confidence`, `investigatorVersion`) — additive on the wire; turns stored
before this ADR omit it.

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

- **Good**: the screenshot's question becomes an honest "not yet", the nearest screen, two
  questions Ask can answer now and an offer to propose the feature; the regex's misses of the
  five known gaps are caught too; nothing reaches the backlog without a person's decision.
- **Bad**: one extra `analyst` call (≈ 2–3k input tokens) and its latency on every fresh
  in-domain turn, bounded by the timeout; an interview or a notice fallback is no longer
  model-free (it is still retrieval-free and answer-free — the tests now assert exactly one
  capability check and nothing else); many users may propose the same feature under different
  slugs — triage deduplicates on GitHub.
- **Neutral**: no schema change (`feature_request` stores `discovered:<slug>` in `gap_key`); the
  fixture gateway carries a deterministic double (a request for a report-like deliverable is the
  one discovered gap) so CI and the golden set stay stable.

## Verification

- `Raffa.Chat.Tests`: `CapabilityInvestigatorTests` (verdicts, scrub, veto, threshold, fail-open
  incl. timeout, the fixture double, the strict schema), `CapabilityInvestigatorPromptTests`
  (drift, privacy law), `FeatureRequestIssueTextTests` (approval section, labels, proposal
  title), `DomainGateCapabilityGapTests` (roles are not suppliers).
- `Raffa.Api.Tests`: `AskCapabilityGapTests` (the screenshot question end to end; the kill
  switch), `ConversationFeedbackEndpointTests` (a discovered gap published as a proposal), and
  the zero-call assertions of the interview / notice / web-consent / abstain tests restated as
  "one capability check, nothing else".
- `Raffa.AiEval`: `seeded-capability_gap-discovered-report-it`.
