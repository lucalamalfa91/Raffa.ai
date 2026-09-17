import type {
  ApiClient,
  CapabilityBody,
  ConversationActionBody,
  ConversationCitationBody,
  ConversationDetailBody,
  ConversationMessageBody,
  ConversationReplyBody,
  ConversationReplyKind,
  DocumentListPageBody,
} from "../../api/client";
import type { CitationCorpus, Reply, ReplyAction, ReplyCitation } from "./reply/replyTypes";
import type { WorkspaceRole } from "../../components/shell/navItems";

/**
 * V2 view-model for the Ask Raffa screen (route `/ask`, `/ask/:conversationId`; ADR-024;
 * ADR-020 V2 amendment "screen 2"; task E13/F09/US01/T04, us-01-web-v2 AC-1/AC-3/AC-5/AC-6). Same
 * one-concern-per-file split the V1 module this file replaces already established: `index.tsx`
 * orchestrates React state/effects/navigation, this module decides what a turn/screen looks like
 * from the real wire shapes `../../api/client.ts` exposes (`ConversationReplyBody`,
 * `ConversationMessageBody`, ...).
 *
 * **Wire reply -> presentational `Reply`, not two competing shapes.** `../../api/client.ts`'s
 * `ConversationReplyBody`/`ConversationMessageBody` are the wire-exact ADR-024 §6 contract;
 * `./reply/replyTypes.ts`'s `Reply` (task E13/F09/US01/T02, phase-2, out of this task's own "Files
 * to create or modify") is the pure, API-agnostic union `ReplyBody`/`CitationCard`/`ActionRow`
 * actually render. This module is the one place that maps one onto the other -- exactly the "F09/T04
 * owns mapping the generated wire type onto `Reply`" hand-off `replyTypes.ts`'s own header comment
 * already names.
 */

// ---------------------------------------------------------------------------------------------
// Citations: corpus normalisation + tenant-citation deep-link enrichment
// ---------------------------------------------------------------------------------------------

/**
 * `citations[].corpus` is usually `"tenant" | "market" | "raffa"` (requirements.md §6 / R-WEB-04's
 * three-corpus citation-badge vocabulary; `./reply/replyTypes.ts#CitationCorpus`, closed to exactly
 * those three). The real backend admits a fourth internal value, `"calc"`
 * (`Raffa.Chat.Application.Pack.PackCorpus.Calc` -- a deterministic-calculator-derived fact: a
 * renewal date, a negotiation lever, a criticality score), with no remapping step anywhere before
 * the wire (`ReplyCitation.Corpus`'s own doc comment: "Echoes `PackItem.Corpus`"); confirmed by
 * reading `backend/src/Raffa.Api/AskCopilotService.cs`'s own `PackItem` constructions for
 * `when-you-must-move`/lever/target/criticality items, which set a real `/contracts/{id}` `Href`
 * despite `PackItem.Href`'s own doc comment claiming `Href` is null for `PackCorpus.Calc`. A
 * calculator fact is always about *this tenant's own contract*, never a market/feature fact, so an
 * unrecognised value folds into `"tenant"` -- the closer honest bucket -- rather than the more
 * surprising `"raffa"` (a static feature card) or a thrown exception. Named here as a real
 * backend/frontend contract gap, not a guess (see `web/openapi/raffa-api.v1.json`'s
 * `postConversationMessage` operation description for the same note).
 */
export function toCitationCorpus(wireCorpus: string): CitationCorpus {
  if (wireCorpus === "tenant" || wireCorpus === "market" || wireCorpus === "raffa") {
    return wireCorpus;
  }
  return "tenant";
}

/**
 * Trusts the backend's own citation `href` -- there is nothing left to synthesise (task
 * E28/F03/US02/T01, NW-83). Before that task a tenant citation's real `href` was always the bare
 * `/contracts/{contractId}` (`AskCopilotService.cs`'s own `PackItem` constructions), never
 * `?clause=...` despite requirements.md §6's own illustrative example, so the citation-landing deep
 * link had to be finished client-side by appending `?page=` here. `AskCopilotService
 * .ResolveTenantClauseLinks` now stamps the full link server-side instead: the W18 viewer's
 * `?page=&clause=` pair when a clause/span resolves, the viewer's `?page=` alone when only a
 * document page does, or the 360 route's own `?clause=`/`?page=` half otherwise -- three tiers, and
 * every one of them already carries whatever query string applies. `CitationCard.tsx` reads that
 * shape directly (`isViewerHref`) to decide whether a citation "carries a clause/span id" at all,
 * so the `?page=`-appending branch below no longer fires against real backend data.
 *
 * Kept, not deleted: a defensive fallback for a builder that still sends a bare `href` alongside a
 * known `page` without going through `ResolveTenantClauseLinks` (`BuildContractFactItem` does
 * today, though its own `page` is always `null`, so even that call site never actually reaches the
 * append branch). A `href` that already carries a query string is trusted as-is, never
 * double-appended.
 */
export function buildTenantCitationHref(href: string | null, page: number | null): string | null {
  if (href === null) return null;
  if (href.includes("?")) return href;
  return page !== null ? `${href}?page=${page}` : href;
}

export function mapConversationCitation(body: ConversationCitationBody): ReplyCitation {
  const corpus = toCitationCorpus(body.corpus);
  return {
    n: body.n,
    corpus,
    title: body.title,
    subtitle: body.subtitle ?? "",
    snippet: body.snippet,
    previewUrl: body.previewUrl,
    href: corpus === "tenant" ? buildTenantCitationHref(body.href, body.page) : body.href,
    // Task E28/F03/US02/T01 (NW-83/NW-93): echoed verbatim so `CitationCard.tsx` can build its
    // two-CTA card's primary "Open contract" action once `href` above has resolved to the W18
    // viewer route. No extra corpus gate needed -- the wire's own `contractId` is already `null`
    // for market/raffa citations and for an NW-81 peer hit.
    contractId: body.contractId,
  };
}

/** `actions[].kind` is really `"navigate" | "upload"` (`Raffa.Chat.Application.Capabilities
 * .CopilotActionKind`) -- requirements.md §6's own illustrative JSON example shows `"primary"`/
 * `"secondary"` instead, a visual-priority label the real backend never emits (confirmed reading
 * `ConversationsEndpointExtensions.ToActionJson`). `navigate`/`upload` is an orthogonal axis (*what*
 * the action does) from primary/secondary (*how prominent* the button is) -- that example's own
 * ordering (`"Open Contract 360 →"` primary, `"Track it in Renewals"` secondary, both `navigate`)
 * proves the two axes are independent. This client reproduces the visual axis the only way the wire
 * actually supports it: by position -- the first action in `actions[]` is primary, every other one
 * is secondary -- which also gives R-ASK-07's "redirect/refusal: one CTA" a primary-styled button for
 * free, since `ReplyBody.tsx` (phase-2, out of this task's scope) already slices that array to its
 * first entry only for those two kinds. */
export function toReplyActionKind(index: number): ReplyAction["kind"] {
  return index === 0 ? "primary" : "secondary";
}

export function mapConversationAction(body: ConversationActionBody, index: number): ReplyAction {
  return { label: body.label, href: body.href, kind: toReplyActionKind(index) };
}

/** The common shape every turn boils down to, whichever wire object it came from (a live
 * `ConversationReplyBody` carries `answerMarkdown`/`followUps`; a stored `ConversationMessageBody`
 * carries `markdown` and no `followUps` at all -- see `mapConversationMessageToReply`'s own doc
 * comment). Kept private: callers only ever see the two named `map*` functions below. */
interface NormalizedTurnBody {
  kind: ConversationReplyKind;
  text: string;
  citations: readonly ConversationCitationBody[];
  actions: readonly ConversationActionBody[];
  followUps: readonly string[];
}

function buildReply(turn: NormalizedTurnBody): Reply {
  switch (turn.kind) {
    case "answer":
      return {
        kind: "answer",
        answerMarkdown: turn.text,
        citations: turn.citations.map(mapConversationCitation),
        actions: turn.actions.map(mapConversationAction),
        followUps: turn.followUps,
      };
    case "redirect":
    case "refusal":
      return {
        kind: turn.kind,
        answerMarkdown: turn.text,
        actions: turn.actions.map(mapConversationAction),
      };
    case "abstain":
      // The backend's own abstain branch stores the reason *as* answerMarkdown/markdown
      // (Raffa.Chat.Application.Reply.CopilotReplyBuilder's own abstain construction:
      // `new(ReplyKind.Abstain, guarded.AbstainReason ?? "...", [], recoveryActions, ..., [])`) --
      // there is no separate "reason" field on the wire to read instead. `recoveryActions` (task
      // E25/F05/US01/T01, backend) lands on the wire's own generic `actions[]`, mapped here with
      // the same `mapConversationAction` the answer/redirect/refusal branches already use (task
      // E25/F05/US02/T01) -- `ReplyBody.tsx` is the layer that forces the result to render
      // secondary-only (ADR-024), never primary, the same defensive posture it already applies to
      // redirect/refusal's own action slice below.
      return { kind: "abstain", reason: turn.text, actions: turn.actions.map(mapConversationAction) };
    default: {
      // Exhaustiveness guard: a future wire `kind` value fails this file's own build instead of
      // silently rendering nothing for it (same convention `./reply/ReplyBody.tsx` already uses).
      const exhaustive: never = turn.kind;
      return exhaustive;
    }
  }
}

/** Maps a live `POST /api/conversations/{id}/messages` response (has `followUps`) onto `Reply`. */
export function mapConversationReplyToReply(body: ConversationReplyBody): Reply {
  return buildReply({
    kind: body.kind,
    text: body.answerMarkdown,
    citations: body.citations,
    actions: body.actions,
    followUps: body.followUps,
  });
}

/** Maps one stored `GET /api/conversations/{id}` message (resume) onto `Reply`. `followUps` is
 * always empty -- `ConversationMessage` has no such column (`ConversationsEndpointExtensions
 * .ToMessageResponse`'s own field list), so a resumed conversation's past Raffa turns render
 * without follow-up chips, an honest, real limitation (see that operation's own OpenAPI
 * description), not an oversight this function papers over. */
export function mapConversationMessageToReply(message: ConversationMessageBody): Reply {
  return buildReply({
    kind: message.kind,
    text: message.markdown,
    citations: message.citations,
    actions: message.actions,
    followUps: [],
  });
}

// ---------------------------------------------------------------------------------------------
// Turns -- what index.tsx actually renders, one per message
// ---------------------------------------------------------------------------------------------

/** A "you" turn is plain text -- there is no reply contract on the caller's own side of the
 * conversation. A "raffa" turn carries both the mapped, presentational `Reply` (what
 * `ReplyBody.tsx` renders) and the original wire `citations[]` (`wireCitations`) side by side --
 * `ReplyCitation` (the presentational type) deliberately does not carry `contractId`/`recordId`
 * (R-ASK-08 "no guids rendered"; `replyTypes.ts`'s own doc comment), so `index.tsx#openCitation`
 * looks the clicked citation's `n` back up in `wireCitations` to recover the one field it actually
 * needs to act (a market citation's `recordId`) -- see that function's own doc comment. */
export type AskTurnView =
  | { id: string; role: "you"; text: string }
  | { id: string; role: "raffa"; reply: Reply; wireCitations: readonly ConversationCitationBody[] };

let turnIdCounter = 0;

/** Monotonic, test-friendly id generator for a locally-built turn (the optimistic "you" bubble, or
 * a transport-error turn with no real message id yet) -- not `crypto.randomUUID()`, the same
 * reasoning the V1 `askViewModel.ts#nextMessageId` this file replaces already gave: nothing here
 * needs global uniqueness, only "never repeats within one mounted screen". */
export function nextTurnId(): string {
  turnIdCounter += 1;
  return `ask-turn-${turnIdCounter}`;
}

export function buildYouTurn(id: string, text: string): AskTurnView {
  return { id, role: "you", text };
}

export function buildRaffaTurnFromReply(id: string, body: ConversationReplyBody): AskTurnView {
  return { id, role: "raffa", reply: mapConversationReplyToReply(body), wireCitations: body.citations };
}

export function buildRaffaTurnFromMessage(message: ConversationMessageBody): AskTurnView {
  return { id: message.id, role: "raffa", reply: mapConversationMessageToReply(message), wireCitations: message.citations };
}

/** AC-5 "resume": every stored message, oldest first (the wire's own order, `GET
 * /api/conversations/{id}`'s own `messages` array -- `ConversationService.GetAsync`'s own
 * `OrderBy(m => m.CreatedAt)`), turned into the same `AskTurnView` shape a live turn produces. */
export function buildTurnsFromConversation(detail: ConversationDetailBody): readonly AskTurnView[] {
  return detail.messages.map((message) =>
    message.role === "you" ? buildYouTurn(message.id, message.markdown) : buildRaffaTurnFromMessage(message),
  );
}

/** A transport/network failure or a genuine 400/404 -- distinct from an honest AI abstention, the
 * same rule the V1 `askViewModel.ts#ChatMessageKind` this file replaces already documented. */
export function buildErrorTurn(id: string, reason: string): AskTurnView {
  return { id, role: "raffa", reply: { kind: "error", reason }, wireCitations: [] };
}

export const TRANSPORT_ERROR_REASON =
  "Raffa.ai's Q&A service is temporarily unavailable. Try again in a moment.";

// ---------------------------------------------------------------------------------------------
// Off state (screens-v2.md #2; R-ASK-10) -- "Ask needs at least one validated contract."
// ---------------------------------------------------------------------------------------------

export interface AskOffCopy {
  reason: string;
  ctaLabel: string;
}

/**
 * Which off-copy variant applies (task E16/F03/US01/T01, wave w15; ADR-020 w15 §2.1, ADR-012 w15
 * §4). Three states, read off the server's own `counts` (`GET /api/documents`, ADR-027 §D7) --
 * never off `totalCount > 0`, which is documents in *any* state and told a tenant whose only
 * document had failed that it was "still processing": the fabricated fact A15-3 forbids.
 *
 * - `"no-documents"`: Raffa.ai holds no document. `counts.all` excludes `Rejected`, so a tenant
 *   holding only refused files lands here too -- "Upload a contract first" is true for them, and
 *   each refusal was already explained on its own row.
 * - `"processing"`: at least one document is `Uploaded`, `Processing` or `NeedsReview`.
 * - `"stalled"`: documents are held, none is in flight, none validated -- the third variant, which
 *   must never be collapsed back into the "still processing" sentence.
 */
export type AskOffReason = "no-documents" | "processing" | "stalled";

export function resolveAskOffReason(counts: DocumentListPageBody["counts"] | null): AskOffReason {
  if (!counts || counts.all === 0) return "no-documents";
  if (counts.processing > 0 || counts.needsReview > 0) return "processing";
  return "stalled";
}

/** `app.jsx`'s own ternary for the first two rows, quoted verbatim (`askOffReason`/`askOffCta`);
 * the third row is ADR-020 w15 §2.1's -- its second clause is the shipped sentence, verbatim, so
 * exactly one new sentence enters the product. Fixed grammatical number is this surface's existing
 * practice ("Your document", whatever the count), so no pluralisation logic is introduced. */
export function buildOffCopy(reason: AskOffReason): AskOffCopy {
  switch (reason) {
    case "no-documents":
      return {
        reason: "Upload a contract first. Raffa.ai extracts the facts, you sign off the weak ones, and Ask switches on.",
        ctaLabel: "Upload a contract",
      };
    case "processing":
      return {
        reason:
          "Your document is still processing or waiting for review. Ask only answers from facts that passed validation — so it never guesses.",
        ctaLabel: "Go to Documents",
      };
    case "stalled":
      return {
        reason:
          "Raffa.ai could not finish processing your documents. Ask only answers from facts that passed validation — so it never guesses.",
        ctaLabel: "Go to Documents",
      };
  }
}

// ---------------------------------------------------------------------------------------------
// Conversation title (header `convTitle` + "+ New chat", task text point (3))
// ---------------------------------------------------------------------------------------------

/** `ConversationService.DefaultTitle` ("New chat"), quoted verbatim -- the title a conversation
 * carries server-side until its first "you" message derives a real one. Used here only as this
 * screen's own client-side fallback for the brief window between "the you bubble appears" and "the
 * server's create-then-ask round trip resolves" -- `app.jsx`'s own fallback
 * (`'Ask Raffa · new chat'`) is a different string; this file follows the real backend constant
 * instead, since `index.tsx` also shows this exact text for a conversation that is still
 * genuinely titleless (a resumed one somehow has no messages at all yet -- not reachable today, but
 * an honest label rather than an invented one if it ever is). */
export const DEFAULT_CONVERSATION_TITLE = "New chat";

/** `ConversationService.DeriveTitle`'s own rule, reproduced client-side (task E13/F09/US01/T04's
 * own "duplicate a small pure predicate rather than cross-import between independent features"
 * convention, e.g. `useValidatedContractCount.ts`'s identical reasoning for
 * `isValidatedContractStatus`): collapse embedded whitespace/newlines to single spaces, then
 * hard-truncate at 48 chars, no ellipsis. Lets the conversation header show *a* title immediately
 * after the first question is typed, without waiting for the create-then-ask round trip to resolve
 * and tell this screen the server's own (identically-computed) title. */
const TITLE_MAX_LENGTH = 48;

export function deriveConversationTitle(questionText: string): string {
  const singleLine = questionText.split(/\s+/).filter((part) => part.length > 0).join(" ");
  return singleLine.length <= TITLE_MAX_LENGTH ? singleLine : singleLine.slice(0, TITLE_MAX_LENGTH);
}

// ---------------------------------------------------------------------------------------------
// Scope line (R-ASK-10) -- "Answers only from N validated contracts (…) · cites or abstains"
// ---------------------------------------------------------------------------------------------

/** `app.jsx` `askScope`, quoted verbatim: `'Answers only from '+askable+' validated contract'+
 * (askable===1?'':'s')+' ('+kbNames.join(', ')+') · cites or abstains'` -- except the parenthetical
 * name list is omitted entirely (never rendered as an empty `()`) when `supplierNames` is empty, an
 * honest degradation rather than the prototype's own always-present parens. `GET /api/contracts`
 * does resolve a real `supplierName` per item since task E13/F03/US01/T02, so the names now exist on
 * the wire; what is still missing is a caller that carries them this far. The shell's
 * `../../components/shell/useValidatedContractCount.ts` reduces that same portfolio page to a bare
 * count before `index.tsx` ever sees it, so the only call site passes `[]` and a real deployment
 * still renders the plain, still-truthful sentence -- until some task threads those names through,
 * at which point this same function starts rendering them with no further change here. */
export function buildScopeLine(validatedContractCount: number, supplierNames: readonly string[]): string {
  const plural = validatedContractCount === 1 ? "contract" : "contracts";
  const names = supplierNames.length > 0 ? ` (${supplierNames.join(", ")})` : "";
  return `Answers only from ${validatedContractCount} validated ${plural}${names} · cites or abstains`;
}

/** Screens-v2.md #2's own trailing sentence, appended after the scope line on the "new chat" state
 * only (`app.jsx` `askScope+'. Structured questions...'` is one concatenated paragraph in the
 * prototype; this module keeps the two halves separate so `index.tsx` can render `askScope` alone
 * for the conversation-view's own smaller usage without repeating this sentence there). */
export const NEW_CHAT_TRAILER =
  "Structured questions run on validated fields, legal questions retrieve clauses — every answer cites its page or says it cannot answer.";

/** screens-v2.md #2 "New chat": `askHello`, quoted verbatim. */
export const ASK_HELLO = "What do you want to know?";

/**
 * NW-56 (ADR-020 heading copy; ADR-024 scoped entry): `Contract360Header.tsx`'s "Ask about it"
 * (`/ask?scope=<contractId>`) used to open an empty chat headed by the generic `ASK_HELLO` -- a
 * scoped entry from a live contract must instead **brief** that contract. `screens-v2.md` has no
 * literal scoped-brief copy of its own (the export predates this gap; §5's Contract 360 header --
 * "supplier, name" -- is the closest anchor, reused below rather than invented, the same divergence
 * `Contract360Header.tsx`'s own header comment already takes for that screen's kicker); requirements
 * win over a silent prototype (ADR-024's own rule). Three strings replace, together,
 * `ASK_HELLO` + the generic `buildScopeLine` sentence in the new-chat block:
 *
 * - `kicker` -- the same `.screen-kicker` shape `Contract360Header.tsx:41-44`
 *   (`resolveSupplierLabel`) already renders over its own heading;
 * - `heading` -- "Ask about {supplier}", replacing `ASK_HELLO`;
 * - `scopeLine` -- a one-line, contract-specific scope, replacing the "Answers only from N
 *   validated contracts…" sentence, which would otherwise mis-describe a chat about one contract.
 *
 * `supplierName` is `index.tsx`'s already-fetched `scopedSupplierName` (`getContract360`), `null`
 * until that fetch resolves (or when the contract truly has none). The "this contract" fallback --
 * deliberately not `buildScopedSuggestions`' own mid-sentence "this supplier" -- keeps `heading` a
 * complete, honest sentence ("Ask about this contract") through that window, the same fallback
 * discipline `resolveSupplierLabel` already applies to the 360 kicker.
 */
export interface ScopedAskBrief {
  kicker: string;
  heading: string;
  scopeLine: string;
}

export function buildScopedBrief(supplierName: string | null): ScopedAskBrief {
  const name = supplierName !== null && supplierName.trim() !== "" ? supplierName.trim() : "this contract";
  return {
    kicker: name,
    heading: `Ask about ${name}`,
    scopeLine: "Answers cite this contract's pages.",
  };
}

/** ADR-024 §6 / screens-v2.md #2: the same placeholder the global Ask bar uses
 * (`components/ask-bar/askSuggestions.ts` READY_PLACEHOLDER). */
export const ASK_INPUT_PLACEHOLDER = "Ask Raffa — spend, dates, clauses, liability…";

/** screens-v2.md #2 "Thinking": V1 copy retained verbatim until the reply streams. */
export const THINKING_COPY = "Authorising scope → detecting intent → retrieving evidence";

// ---------------------------------------------------------------------------------------------
// Suggestion chips (task text point (2): "two suggestion chips from GET /api/capabilities
// (suggestionsFor("ask"))"; R-SYS-01)
// ---------------------------------------------------------------------------------------------

/** `app.jsx` `c360Chips`, quoted verbatim -- the one screen whose chips are supplier-templated
 * rather than catalog-sourced (`CapabilityCatalog.SuggestionsFor`'s own identical special case,
 * server-side, has no HTTP surface at all -- see `web/openapi/raffa-api.v1.json`'s
 * `getCapabilities` operation description). `supplierName` absent/blank falls back to "this
 * supplier", the same fallback that C# method uses. */
export function buildScopedSuggestions(supplierName: string | null): readonly [string, string] {
  const supplier = supplierName === null || supplierName.trim() === "" ? "this supplier" : supplierName;
  return [`When must we give notice to ${supplier}?`, `What is our liability cap with ${supplier}?`];
}

const ASK_CAPABILITY_KEY = "ask";

/** The plain, unscoped case: the "ask" capability's own `exampleQuestions` (`GET /api/capabilities`,
 * task text: "suggestionsFor(\"ask\")"), first two. Falls back to a small, static pair when the
 * catalog has not loaded yet (or the fetch failed) -- never a blank rail, the same "fallback to
 * static copy" contract `components/ask-bar/askSuggestions.ts#getAskBarCopy` already establishes for
 * the *global* bar's own chips. */
const ASK_SUGGESTIONS_FALLBACK: readonly [string, string] = [
  "When does a contract expire?",
  "What liabilities do we have?",
];

/**
 * Task E25/F01/US01/T01 (AC-1, ADR-022 S16-11): drops the "ask" capability's own `exampleQuestions`
 * for a non-Admin when its catalog entry carries `roleGate !== "any"`, the same predicate
 * `components/ask-bar/askSuggestions.ts#suggestionsFromCapabilityCatalog` applies for the global
 * bar -- duplicated rather than imported, the same "small pure predicate, independent screens"
 * convention this file already follows elsewhere (see `resolveAskOffReason`'s neighbours). Falls
 * back to `ASK_SUGGESTIONS_FALLBACK`, never an empty pair. `role` is optional so an unscoped caller
 * that has not threaded a role through yet still gets today's behaviour unchanged -- in the real
 * catalog the "ask" capability is always `roleGate: "any"` (`CapabilityCatalog.cs`), so this never
 * hides a chip in production; an absent `role` is still read as "not Admin", never as Admin.
 */
function isChipVisibleForRole(roleGate: CapabilityBody["roleGate"], role: WorkspaceRole | undefined): boolean {
  return roleGate === "any" || role === "admin";
}

export function suggestionsFor(
  capabilities: readonly CapabilityBody[] | null,
  supplierName?: string | null,
  role?: WorkspaceRole,
): readonly [string, string] {
  if (supplierName !== undefined) {
    return buildScopedSuggestions(supplierName);
  }
  const match = capabilities?.find((capability) => capability.key === ASK_CAPABILITY_KEY);
  if (match && match.exampleQuestions.length >= 2 && isChipVisibleForRole(match.roleGate, role)) {
    return [match.exampleQuestions[0], match.exampleQuestions[1]];
  }
  return ASK_SUGGESTIONS_FALLBACK;
}

// ---------------------------------------------------------------------------------------------
// Citation click resolution (AC-3; R-EVD-02) -- tenant navigates, market opens the panel, raffa
// navigates
// ---------------------------------------------------------------------------------------------

export type CitationOpenAction =
  | { kind: "navigate"; href: string }
  | { kind: "market-panel"; recordId: string }
  | { kind: "none" };

/**
 * `index.tsx#openCitation`'s own decision table (task text point (3)): "clicking a tenant citation
 * -> `/contracts/<contractId>?clause=<clauseId>` (or `?page=`) with `state.from = "ask"`" (already
 * baked into `citation.href` by `mapConversationCitation`); "a market citation opens a side panel
 * loading `GET /api/market/records/{id}`" (needs `wireCitation.recordId`, not on the presentational
 * `ReplyCitation`); "a Raffa feature card navigates to its href". Looks the clicked citation's `n`
 * up in `wireCitations` (the same array `AskTurnView.wireCitations` carries) rather than trusting
 * `citation` alone, since only the wire object still has `recordId`.
 */
export function resolveCitationOpenAction(
  citation: ReplyCitation,
  wireCitations: readonly ConversationCitationBody[],
): CitationOpenAction {
  if (citation.corpus === "market") {
    const wire = wireCitations.find((c) => c.n === citation.n);
    return wire?.recordId ? { kind: "market-panel", recordId: wire.recordId } : { kind: "none" };
  }
  return citation.href ? { kind: "navigate", href: citation.href } : { kind: "none" };
}

// ---------------------------------------------------------------------------------------------
// Conversation-create request (?scope=)
// ---------------------------------------------------------------------------------------------

/** AC-5 `/ask?scope=<contractId>` "opens a scoped new chat" -- validated defensively (a malformed
 * query value must never reach `POST /api/conversations` as a bad request; `undefined` here means
 * "plain new chat", the same optional-field convention `CreateConversationRequest` itself uses). */
export function parseScopeContractId(rawScope: string | null): string | undefined {
  if (rawScope === null) return undefined;
  const trimmed = rawScope.trim();
  return trimmed === "" ? undefined : trimmed;
}

/** Thin, testable wrapper around the two-call "create, then ask" sequence every new chat runs
 * (task text point (2): "a question creates a conversation ... then posts the message"). Returns
 * the created conversation id on success so `index.tsx` can navigate to `/ask/<conversationId>`
 * (task text: "the URL becomes `/ask/<conversationId>`") even when the first message itself somehow
 * fails -- the conversation still exists and is worth resuming. */
export async function createConversationAndAsk(
  apiClient: ApiClient,
  tenantId: string,
  question: string,
  scopeContractId: string | undefined,
): Promise<
  | { ok: true; conversationId: string; reply: ConversationReplyBody }
  | { ok: false; conversationId: string | null; reason: string }
> {
  const created = await apiClient.createConversation(tenantId, scopeContractId ? { scopeContractId } : {});
  if (!created.ok || !created.conversation) {
    return { ok: false, conversationId: null, reason: created.error ?? TRANSPORT_ERROR_REASON };
  }

  const conversationId = created.conversation.id;
  const posted = await apiClient.postMessage(tenantId, conversationId, { question });
  if (!posted.ok || !posted.reply) {
    return { ok: false, conversationId, reason: posted.error ?? TRANSPORT_ERROR_REASON };
  }

  return { ok: true, conversationId, reply: posted.reply };
}
