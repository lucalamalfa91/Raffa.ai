# ADR-030 — Ask Raffa capability gaps: an honest preface, a drafted negotiation email, and an in-chat feedback loop that opens a GitHub issue

- **Status**: accepted
- **Date**: 2026-09-22
- **Deciders**: product-owner (the ruling below, the three interview questions, the privacy allow-list) + software-architect (gate placement, the fifth reply kind, the drafting workflow, the feedback seam) + security-architect (what a public issue may carry, the PAT in Key Vault, RLS on the new table) + client-architect (the `payload` contract, the card, the `external` action)
- **Wave**: w20 — "Ask says what it cannot do, and does the next best thing"
- **Items served**: the owner's ruling of 2026-09-22 on the Amazon screenshot (below); ADR-024's own "cites or abstains" promise; NW-58's precedent (mail transport deferred, not a non-goal)
- **Locked citations**: ADR-001 §1.2 (autonomous supplier communication is a non-goal — a **human-approved draft shown in the UI is not that**; a mail transport is deferred, not excluded — ADR-001 w14 footer); ADR-002 (Raffa.Chat's allow-list is `[SharedKernel, AiGateway]`; provider adapters live in the host); ADR-004 (structured no-tools roles behind `IAiGateway`; the `analyst` role of the council); ADR-009 (RLS on every tenant table); ADR-011 (Key Vault + managed identity, no secret in source, audit names never values); ADR-012 (the one OpenAPI contract → one generated client); ADR-021 (checked-in idempotent SQL); ADR-024 (the engine, the four reply kinds, the guards — amended, never rewritten). Appendix C rule 10 (uncertainty over fabricated precision) is honoured, not relaxed.

## Context and problem statement

A user asked Ask Raffa, from a chat about their Amazon Web Services order form:
*"I have to renegotiate with Amazon. Can you help me create an email based on
the negotiation leverage?"* The engine did everything ADR-024 says: the gate
admitted the turn (a savings signal, `leverage`), the planner routed it to
`Savings` (the lever lexicon), the pack and the council were built, and the
`answer` role wrote a good reply — with a zero-based `[0]` marker.
`GroundingGuard` rejected it, the retry repeated the mistake, and
`RegenerateOnce` replaced the whole reply with an abstain whose *reason* is a
dump of pack facts. The web rendered it as the pink block **"I don't have data
I trust enough to answer."**

The owner's verdict: *"non devi rispondere a caso o non dare alternative
all'utente."* The user asked for an **operation** — write/send an email — and
Raffa neither said that it cannot send email nor did the next best thing it
plainly can do: write the email from the contract's own facts. Nothing in the
engine could express "I cannot do X, but here is Y": there is no reply shape
for it, no lexicon recognises an operation request, and every path ends in
`answer` or `abstain`. Nor can a user tell the team what they wanted — there
is no feedback surface anywhere in the product.

## Decision drivers

- **Honesty first, then usefulness.** An operation Raffa cannot perform must be
  said in one sentence, in the question's language, and followed by the
  nearest real alternative — never by retrieval-then-abstain, never by the
  feature tour ("help" also opens a how-to question).
- **The drafted email must never be blanked.** The `answer` role's `[n]` gate
  is right for a fact answer and wrong for an email: an email has no citation
  apparatus. The draft needs its own guard, its own retry and a fallback that
  is always a usable email (Appendix C rule 10 — grounded, never fabricated).
- **Deterministic first, like the rest of the gate.** No model call decides
  whether a turn is a gap; regex only, IT + EN, conservative (a missed gap
  costs one honest abstain; a false positive hijacks a real question).
- **The feedback must reach the developers where they work** — a GitHub issue
  on the product repository — and the repository is **public**, so what an
  issue carries is an allow-list decided here, not a habit.
- **No multi-turn server state.** A conversational interview ("answer one
  question per turn") would need a per-chat state machine and a way to tell an
  answer from a new question; the card holds the interview until one submit.
- **Everything survives a resume**: the email verbatim, the offer's state, the
  confirmation — from any browser (ADR-028's rule).

## Considered options

1. **Teach the persona** to answer "I can't send email, but here is a draft" inside the `answer` role — rejected: the draft would still pass through the `[n]` gate that erased it, and a prompt is not a contract.
2. **A new planner intent** (`DraftEmail`) — rejected: the planner runs after the gate, `help me…` is already captured by the capability lexicon before it, and three of the five gaps have no pack to build; an intent is "which pack", a gap is "which alternative".
3. **A gate label + a fifth reply kind + a drafting workflow + an in-chat feedback card** (chosen).
4. **Conversational interview with server-side state** — rejected for the reasons above; kept as a follow-up if the card proves too rigid.
5. **Feedback stored only, issues opened by an operator workflow** — rejected: the value is the immediate, visible issue number in the chat; the stored-only path remains the behaviour of an environment without a token.

## Decision outcome

**Chosen: option 3**, because the honest sentence, the usable draft and the
feedback loop are three shapes the engine must be able to *produce*, not three
tones the model must be asked to *adopt*.

### D1 — A capability gap is a gate label, checked after Legal and before Capability

`Raffa.Chat.Application.Gaps.CapabilityGapCatalog` holds five entries, each an
IT/EN lexicon (an operation verb **and** its object, or an unmistakable noun),
a key, both languages of every user-facing string, and the alternative Raffa
offers instead: `send-supplier` and `email-draft` → **DraftEmail**; `reminder`
→ **Renewals**; `export-file` → **Portfolio**; `purchase-order` → **Contract
360**. `DomainGate.Classify` checks the catalog right after the legal lexicon
(a "letter to sue" stays a refusal) and before the capability/how-to lexicon
("can you help me send an email" is not the feature tour), returning
`GateLabel.CapabilityGap` with the entry and the supplier name it still
extracts. A timing question ("when must we send the notice?") is vetoed for
the send gap: it is a structured fact (NW-91), not a request to send. The
planner is untouched; the composition root branches on the label.

### D2 — A fifth reply kind, `draft`, and a structured `payload`

`ReplyKind.Draft` / wire `"draft"` / `ConversationMessageKind.Draft`. The
preface lives in `answerMarkdown` ("Al momento non posso creare o inviare
email da Raffa.ai, però posso aiutarti a scrivere la mail per il rinnovo.");
the email lives in `payload.draft { subject, body }`, plain text, rendered
verbatim by the client with a **Copy email** button, never through the
markdown subset; the pack items the email was written from are the
`citations`; the feedback offer is `payload.feedbackOffer`. Every reply and
every stored message now carries `payload` (`ReplyPayload`, one nullable
`jsonb` column `conversation_message.payload_json`), `null` for every turn that
carries none. A capability-gap `redirect` carries `payload.gap` and
`payload.feedbackOffer`, and — for the email gap with no contract to draft for
— one validated supplier per follow-up chip. "Cites, drafts from cited facts,
or abstains" replaces "cites or abstains".

### D3 — The drafting workflow: council → offer planner → negotiation writer → DraftGuard → template

`Raffa.Chat.Application.Drafting.NegotiationDraftingWorkflow` runs over the
same Q3 pack a scoped renewal-strategy turn gets (contract facts, lever
calculations, clause evidence, playbook, with the council's plays inserted).
Two agents through `IAiGateway.AnalyzeAsync` (`draft-v1`, prompts mirrored in
`Prompts/draft/v1.md` with a drift test): the **offer planner** returns the
position, the asks in value order with their citation keys, the trade, the
deadline anchor; the **negotiation writer** returns `{ subject, body,
usedCitationKeys }` in the question's language. `DraftGuard` polices the
result — no inline `[n]`, no link, no guid, no internal key, every number/date
in the pack (`NumericGuard`, unchanged), `usedCitationKeys` in the pack — one
retry names the violation, and a second failure, a gateway outage, a thin
pack or `Chat:Drafting:Enabled=false` fall back to
`NegotiationEmailTemplate`: an email written from the pack's own values with
every clause conditional on its fact, which passes the guard by construction.
**The draft path never abstains.** A template fallback or a retried writer is
audited as a guard intervention on the turn (`chat.drafted`,
`abstainGuardIntervened=True`), so the golden set's zero-intervention rule
catches a fixture or prompt regression; the fixture gateway carries
deterministic doubles for both agents.

### D4 — The language of a deterministic reply is decided by the server

`Application.Language.QuestionLanguage.Detect` (two lexicons of
language-exclusive function words, an accent bonus, ties → English) picks
Italian or English for the preface, the "which contract?" sentence, the
feedback card and the confirmation. The web's own chrome (labels, "Copy
email") stays English, as every other label of the screen; the body beside it
follows the question, as the persona's answers already do.

### D5 — The feedback loop: an in-chat card, one call, store first, publish best-effort, an allow-listed issue

The card ("Vuoi segnalarlo al team Raffa.ai perché lo implementi?" [Sì] [No])
asks **three questions one at a time** — what exactly Raffa should do (free
text, prefilled with the gap's own description, ≤ 500 chars), how often
(every renewal / weekly / now and then), how important (blocking / very
useful / nice to have) — then one `POST /api/conversations/{id}/feedback`.
`FeedbackService` validates the answers against the fixed vocabulary, checks
the message really carries an offer, **stores** a `feature_request` row
(tenant table, RLS, unique per message), **then** publishes through
`IFeatureRequestPublisher` — the host's `GitHubIssueFeatureRequestPublisher`
(`POST /repos/{owner}/{repo}/issues`, a fine-grained PAT with Issues: write on
the one repository, from Key Vault as `github-feedback-token`, handle
`gh-feedback`, API app only, product switch `Feedback__GitHub__Enabled`) or
the module's `NullFeatureRequestPublisher` — and appends a confirmation turn
("Grazie, ho aperto la segnalazione #123 …", or "…è stata registrata" when
nothing was published). A failed or missing publisher never fails the
request. **The issue carries only**: the gap key and title, the three answers,
the question language, the environment name and an opaque eight-hex hash of
the tenant id — never the question, a supplier name, a contract value or the
user's identity; the card says so before the free-text question. The audit
row carries the gap key and the outcome, never the answers.

### D6 — `external` is a server-authored action kind

`CopilotActionKind.External` renders as a plain `<a target="_blank"
rel="noopener noreferrer">`, never a router link. It is built only by
`CopilotAction.External`, which requires an absolute https URL, and only by
the feedback confirmation from the publisher's own response — never by the
model, never from the catalog.

### Consequences

- **Good**: the screenshot turns into an honest sentence plus an email the
  user can send; four more operations get the same honesty; the team gets a
  scoped, deduplicated issue per gap with a public-safe body; every new
  behaviour is deterministic where it can be and guarded where it cannot.
- **Bad**: a fifth kind touches the contract, the client, the store and the
  golden set at once; a draft turn costs up to five agent calls; the language
  heuristic and the gap lexicon are regex and will miss phrasings (a missed
  gap is still the old honest abstain); a template email is generic by
  design.
- **Neutral**: the `answer` role's `[0]` mistake is not fixed here — the
  draft path is immune to it and the fact path keeps the gate (known gap).

## Pros and cons of the options

### Option 1 — teach the persona
- Good: no contract change.
- Bad: the erased draft is exactly the failure being fixed; nothing enforces the sentence.

### Option 2 — a planner intent
- Good: reuses the intent switch.
- Bad: runs after the "help" collision; needs a pack for gaps that have none.

### Option 3 — gate label + kind + workflow + card (chosen)
- Good: each behaviour is a shape with its own guard and test.
- Bad: the widest change of the four.

### Option 4 — conversational interview
- Good: feels natural.
- Bad: a per-chat state machine and an ambiguity the engine cannot resolve deterministically.

## Implications for the decomposition

- A task adding a gap adds a catalog entry with both languages, its alternative, a positive and a negative phrase in `CapabilityGapCatalogTests`, and — when the alternative is a screen — nothing else.
- A task changing a drafting prompt bumps `DraftingAgents.Version`, updates `Prompts/draft/v1.md` (or its successor) and keeps `NegotiationDraftingWorkflowTests.Runs_planner_then_writer_on_the_fixture_and_passes_first_time` green: the fixture draft must pass `DraftGuard` first time or the golden set fails `main`.
- A task touching the reply contract edits `web/openapi/raffa-api.v1.json` first and regenerates the client; `payload` is required and nullable on every message.
- A task touching the issue body edits `FeatureRequestIssueText` and its test only; the type has no field for anything outside the allow-list, keep it that way.
- The PAT is rotated by an operator in the HCP workspace variable; a rotation needs no code change.

## Assumptions

- GitHub creates an issue for a token with Issues: write on a public repository and answers 422 for an unknown label; the publisher retries once without labels.
- A tie in the language heuristic is rare enough that English is the right default; an Italian question with no Italian function word at all ("Salesforce?") is not a gap turn.
- The Q3 pack is the right evidence for an email about any of the five gap phrasings that resolve to a contract; a future gap with different evidence adds its own pack.
