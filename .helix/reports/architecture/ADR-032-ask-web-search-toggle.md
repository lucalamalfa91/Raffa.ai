# ADR-032 — Ask Raffa: a web-search toggle in the composer — the web and Raffa's own data together, the procurement-only filters lifted

- **Status**: accepted
- **Date**: 2026-09-23
- **Deciders**: software-architect (owner) + product-owner (the request: "a toggle in Ask Raffa to use the web research agent — it removes every filter and searches freely, on the web and on our internal RAGs; a question completely out of context, like the carbonara recipe, is pointed at Google or an AI search tool") + security-architect (isolation, audit) + client-architect (wire flag, settings field)
- **Locked citations**: ADR-030 §B (research role, structural isolation, three gates, per-question consent, guards, audit); ADR-024 §Engine; ADR-011 (hashes and counts in audit, never content); R-AI-03; R-SYS-02
- **Amends**: ADR-030 §B clauses 9 and 10 (topic lexicon and per-question consent) **for toggle turns only**; ADR-024 §Engine (the domain gate's off-domain/legal/needs-document redirects and the interview do not apply to toggle turns)

## Context and problem statement

ADR-030 made web research a narrow exception: offered only for a procurement topic (four
purposes), only after an alert-dialog consent on each question. In use that is two clicks and a
refusal for most real questions ("what do the new EU rules require from cloud suppliers?" is not
one of the four purposes), and the domain gate turns away anything outside procurement before the
web is even considered. The product owner asked for an explicit mode the user switches on: every
question goes to the web and to Raffa's own store, with the procurement-only filters lifted, and
only a question that has nothing to do with work is sent elsewhere.

## Decision

1. **The toggle is the consent.** `POST /api/conversations/{id}/messages` accepts
   `webResearch: true` (ignored on an interview answer). It becomes `AskTurnHints.WebMode`; there
   is no per-question consent dialog for such a turn. The toggle lives in the composer, beside
   "Ask", and its tooltip says what leaves Raffa (the question's words) and what does not (the
   contracts).
2. **What is lifted**: the topic lexicon (`WebResearchTopicLexicon.Classify`), the interview, and
   the domain gate's off-domain, legal and needs-document redirects. The research role runs a
   separate persona, `research-open-v1` (`WebResearchPrompt.OpenSystemPrompt`, purpose `Open`):
   any topic with a plausible link to the team's work.
3. **What stays**: the kill switch (`Chat:WebResearch:Enabled`), the workspace Admin's opt-in and
   the daily budget — checked on every toggle turn, a closed gate is the same redirect that names
   it. The isolation of ADR-030 §B clauses 7–8 is untouched: the query is `WebQuerySanitizer`'s
   (the user's words minus amounts, dates, e-mails, URLs), `AiResearchRequest` has no pack slot,
   `WebResearchComposer` is still the only caller of the research role and the only producer of
   the `web` corpus (`WebResearchIsolationTests` unchanged and green). `WebGuard`, `NumericGuard`
   and `GroundingGuard` hold the web answer exactly as before; it is always labelled unverified.
4. **The one content filter**: `WebModeLexicon.IsOffContext` — plainly personal or leisure
   questions (recipes, match results, weather, jokes, films, horoscopes, leisure travel), IT/EN,
   and never when the same sentence carries a work word ("catering supplier for the canteen:
   recipes and prices" is a work question). Such a turn is a `redirect` with **two server-authored
   `external` actions** — a Google search and a Perplexity search for the question's own words
   (sanitised, punctuation dropped) — zero retrieval, zero model calls, audited as
   `chat.web_research_refused gate=OffContext`. The open persona's own `offTopic` verdict gives the
   same redirect. The SPA renders both outbound actions of such a redirect (every other redirect
   keeps its one CTA).
5. **Web and store, side by side, never merged.** A toggle turn has two halves:
   - the normal contracts-only pipeline (`BuildInDomainReplyAsync`, interview suppressed, the
     "search the web" phrase stripped) — skipped when the domain gate found the question
     off-domain, since nothing in the store speaks to it;
   - one budgeted open-mode research call.
   Only an `answer` from the store is kept. `WebModeReplyBuilder.Combine` places the two under
   their own headings ("From your contracts and Raffa's data" / "From the public web ·
   unverified", IT/EN), renumbers the web citations after the store's so every `[n]` resolves to
   its own card, unions the provenance sources and sets `unverified`. **ADR-030 §B clause 11's
   "never merged into an `answer`-role pack" holds**: the answer role never sees a web source; the
   halves only share a message. When the web adds nothing, the store's answer stands with one
   honest sentence; when the store has no answer, the web answer (or its guarded abstain) is the
   reply.
6. **Discoverability.** `GET/PATCH /api/workspaces/{id}/settings` gain `webResearchAvailable`
   (the kill switch). The SPA shows the toggle only where it is true; before the workspace opts
   in, clicking it explains where to switch it on instead of toggling. While on, the placeholder,
   the composer note and the thinking row say so, the question's bubble carries a "Web search" tag,
   and a mixed reply's banner says only the web part is unverified.
7. **Audit** (ADR-011): toggle turns write the ADR-030 rows with `mode=toggle` and
   `purpose=Open`; the per-turn row gains `webMode=`.

## Consequences

- Positive: one click turns Ask into a work-scoped web + store assistant; no consent friction per
  question; the carbonara case gets a useful pointer instead of a warm decline.
- Negative: a toggle turn can cost two model calls (answer + research) and one budget unit;
  latency is their sum (sequential: the store half and the budget share the request's scoped
  database contexts).
- Neutral: without the toggle, Ask behaves exactly as before (ADR-030 consent path unchanged).

## Verification

- `Raffa.Chat.Tests`: `WebModeLexiconTests`, `WebModeReplyBuilderTests`,
  `WebResearchComposerTests` (open persona, open refusal), `WebResearchPromptTests` (open twin),
  `WebResearchIsolationTests` (unchanged).
- `Raffa.Api.Tests`: `AskWebModeTests` (off-context pointer with no model call, explicit request
  with no consent dialog, off-domain work question answered from the web, store + web side by
  side with contiguous numbering, persona off-topic, bare greeting, kill switch, opt-in, budget,
  toggle off unchanged), `WorkspaceSettingsEndpointTests` (`webResearchAvailable`).
- Web: `AskRoute`, `askSessions`, `askViewModel`, `ReplyBody` tests for the toggle, the request
  flag, the tag, the not-opted-in hint, the two outbound links and the mixed banner.
