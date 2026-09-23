import type {
  ApiClient,
  CapabilityBody,
  Contract360HeaderBody,
  ConversationActionBody,
  ConversationCitationBody,
  ConversationDetailBody,
  ConversationInterviewBody,
  ConversationMessageBody,
  ConversationPayloadBody,
  ConversationReplyBody,
  ConversationReplyKind,
  DocumentListPageBody,
  PostMessageRequest,
} from "../../api/client";
import type {
  CitationCorpus,
  FeedbackOffer,
  FeedbackResult,
  InterviewQuestion,
  InterviewReply,
  Reply,
  ReplyAction,
  ReplyCitation,
  ReplyGap,
} from "./reply/replyTypes";
import type { WorkspaceRole } from "../../components/shell/navItems";
import { formatSupplier, getContractTypeLabel } from "../contracts/portfolioTableFormatters";

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
 * `citations[].corpus` on the wire is `"tenant" | "market" | "raffa" | "calc"` -- the three ADR-024
 * §2 sources plus `PackCorpus.Calc`, a deterministic calculator's own output (a criticality score,
 * a lever, an aggregate such as "Annual spend total"; `AskCopilotService`'s `PackItem`
 * constructions). Every value is kept as-is so `EvidenceCard` can file it in the right section; an
 * unknown value folds into `"tenant"` -- a citation is about this tenant's own data unless it says
 * otherwise -- rather than throwing on a future backend addition.
 */
export function toCitationCorpus(wireCorpus: string): CitationCorpus {
  if (wireCorpus === "tenant" || wireCorpus === "market" || wireCorpus === "raffa" || wireCorpus === "calc" || wireCorpus === "web") {
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

export function citationDocumentId(documentId: string | null | undefined): string | null {
  if (documentId === null || documentId === undefined) return null;
  const trimmed = documentId.trim();
  if (trimmed === "") return null;
  const prefix = "Document:";
  return trimmed.startsWith(prefix) ? trimmed.slice(prefix.length) : trimmed;
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
    documentId: citationDocumentId(body.documentId),
    page: body.page,
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
  // ADR-030 D6: an `external` action keeps its position-derived prominence but is flagged so
  // `ActionRow` renders a real outbound anchor instead of a router `<Link>`.
  return body.kind === "external"
    ? { label: body.label, href: body.href, kind: toReplyActionKind(index), external: true }
    : { label: body.label, href: body.href, kind: toReplyActionKind(index) };
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
  /** ADR-030 D2: the wire's own `payload` (live reply and stored message alike), or `null`. */
  payload: ConversationPayloadBody | null;
  /** ADR-030: the interview payload (kind "interview" only) and the server id of this turn. */
  interview: ConversationInterviewBody | null;
  messageId: string | null;
  /** ADR-030: the wire's `provenance.unverified` (a live reply) -- a stored message has no
   * provenance, so `buildReply` also infers it from a `web` citation. */
  unverified: boolean;
}

/** The wire's `payload.gap`, `payload.feedbackOffer` and `payload.feedbackResult` are already the
 * presentational shapes `./reply/replyTypes.ts` declares (the server localises every string), so
 * these pass through structurally -- kept as named readers so a future wire change has one place
 * to land. */
function payloadGap(payload: ConversationPayloadBody | null): ReplyGap | null {
  return payload?.gap ?? null;
}

function payloadFeedbackOffer(payload: ConversationPayloadBody | null): FeedbackOffer | null {
  return payload?.feedbackOffer ?? null;
}

function payloadFeedbackResult(payload: ConversationPayloadBody | null): FeedbackResult | null {
  return payload?.feedbackResult ?? null;
}

function mapInterviewQuestion(question: ConversationInterviewBody["questions"][number]): InterviewQuestion {
  return {
    key: question.key,
    prompt: question.prompt,
    presentation: question.presentation === "consent" ? "consent" : "choice",
    allowFreeText: question.allowFreeText,
    options: question.options.map((option) => ({ key: option.key, label: option.label, hint: option.hint ?? null })),
  };
}

function buildReply(turn: NormalizedTurnBody): Reply {
  switch (turn.kind) {
    case "answer": {
      const citations = turn.citations.map(mapConversationCitation);
      const unverifiedWeb = turn.unverified || citations.some((citation) => citation.corpus === "web");
      return {
        kind: "answer",
        answerMarkdown: turn.text,
        citations,
        actions: turn.actions.map(mapConversationAction),
        followUps: turn.followUps,
        feedbackResult: payloadFeedbackResult(turn.payload),
        ...(unverifiedWeb ? { unverifiedWeb: true } : {}),
      };
    }
    case "draft": {
      const draft = turn.payload?.draft ?? null;
      const gap = payloadGap(turn.payload);
      if (draft === null || gap === null) {
        return {
          kind: "answer",
          answerMarkdown: turn.text,
          citations: turn.citations.map(mapConversationCitation),
          actions: turn.actions.map(mapConversationAction),
          followUps: turn.followUps,
        };
      }
      return {
        kind: "draft",
        answerMarkdown: turn.text,
        draft: { subject: draft.subject, body: draft.body },
        gap,
        feedbackOffer: payloadFeedbackOffer(turn.payload),
        citations: turn.citations.map(mapConversationCitation),
        actions: turn.actions.map(mapConversationAction),
        followUps: turn.followUps,
      };
    }
    case "redirect":
    case "refusal":
      return {
        kind: turn.kind,
        answerMarkdown: turn.text,
        actions: turn.actions.map(mapConversationAction),
        followUps: turn.followUps,
        gap: payloadGap(turn.payload),
        feedbackOffer: payloadFeedbackOffer(turn.payload),
      };
    case "interview":
      return {
        kind: "interview",
        prompt: turn.text,
        questions: (turn.interview?.questions ?? []).map(mapInterviewQuestion),
        answered: turn.interview?.answered ?? false,
        messageId: turn.messageId,
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
      // Next-step questions ride along only when the server sent some, so an abstain without
      // any keeps exactly its old shape.
      return {
        kind: "abstain",
        reason: turn.text,
        actions: turn.actions.map(mapConversationAction),
        ...(turn.followUps.length > 0 ? { followUps: turn.followUps } : {}),
      };
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
    payload: body.payload,
    interview: body.interview ?? null,
    messageId: body.messageId,
    unverified: body.provenance.unverified === true,
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
    payload: message.payload,
    interview: message.interview ?? null,
    messageId: message.id,
    unverified: false,
  });
}

// ---------------------------------------------------------------------------------------------
// Interview (ADR-030): answering by option or by typing
// ---------------------------------------------------------------------------------------------

/** The interview the next typed message answers, if the last Raffa turn is one still waiting:
 * its message id and the first question that accepts free text. Null otherwise -- a typed
 * question then posts as a plain question, exactly as before the interview existed. */
export function pendingInterview(turns: readonly AskTurnView[]): { messageId: string; questionKey: string } | null {
  const last = turns[turns.length - 1];
  if (!last || last.role !== "raffa" || last.reply.kind !== "interview") return null;
  const { reply } = last;
  if (reply.answered || reply.messageId === null) return null;
  const question = reply.questions.find((q) => q.allowFreeText);
  return question ? { messageId: reply.messageId, questionKey: question.key } : null;
}

/** ADR-030: the consent the screen must put in front of the user -- the last Raffa turn is an
 * interview still waiting whose question carries `presentation: "consent"`. Null otherwise. The
 * dialog renders only for this shape; a plain choice interview stays inline. */
export function pendingConsent(
  turns: readonly AskTurnView[],
): { reply: InterviewReply; question: InterviewQuestion } | null {
  const last = turns[turns.length - 1];
  if (!last || last.role !== "raffa" || last.reply.kind !== "interview") return null;
  const { reply } = last;
  if (reply.answered || reply.messageId === null) return null;
  const question = reply.questions.find((q) => q.presentation === "consent");
  return question ? { reply, question } : null;
}

/** The wire request for an interview answer: the label (or typed text) as the transcript line,
 * the keys as what the server acts on. */
export function buildInterviewAnswerRequest(
  question: string,
  messageId: string,
  questionKey: string,
  optionKey: string | null,
): PostMessageRequest {
  return {
    question,
    interviewAnswer: optionKey === null ? { messageId, questionKey, freeText: true } : { messageId, questionKey, optionKey },
  };
}

/** Marks the interview turn with that message id as answered (chips disabled) the moment the
 * user answers it, without waiting for the server's own `answered` on a later resume. */
export function markInterviewAnswered(turns: readonly AskTurnView[], messageId: string): readonly AskTurnView[] {
  return turns.map((turn) =>
    turn.role === "raffa" && turn.reply.kind === "interview" && turn.reply.messageId === messageId && !turn.reply.answered
      ? { ...turn, reply: { ...turn.reply, answered: true } }
      : turn,
  );
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
  | {
      id: string;
      role: "raffa";
      reply: Reply;
      wireCitations: readonly ConversationCitationBody[];
      /** ADR-030 D5: the server's own message id (`messageId` on a live reply, `id` on a stored
       * message) -- what `POST …/feedback` names; `null` for a client-built error turn. */
      messageId: string | null;
    };

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
  return { id, role: "raffa", reply: mapConversationReplyToReply(body), wireCitations: body.citations, messageId: body.messageId };
}

export function buildRaffaTurnFromMessage(message: ConversationMessageBody): AskTurnView {
  return {
    id: message.id,
    role: "raffa",
    reply: mapConversationMessageToReply(message),
    wireCitations: message.citations,
    messageId: message.id,
  };
}

/** ADR-030 D5: the ids of every Raffa turn whose feedback offer was already answered -- read off
 * the confirmation turns' own `feedbackResult.forMessageId`, so a resumed conversation hides the
 * card the same way the live one did after submitting. */
export function feedbackSubmittedMessageIds(turns: readonly AskTurnView[]): ReadonlySet<string> {
  const ids = new Set<string>();
  for (const turn of turns) {
    if (turn.role === "raffa" && turn.reply.kind === "answer" && turn.reply.feedbackResult) {
      ids.add(turn.reply.feedbackResult.forMessageId);
    }
  }
  return ids;
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
  return { id, role: "raffa", reply: { kind: "error", reason }, wireCitations: [], messageId: null };
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
// Scope line (R-ASK-10) -- "Answers only from N validated contracts (…) · cites its sources"
// ---------------------------------------------------------------------------------------------

/** `app.jsx` `askScope`: `'Answers only from '+askable+' validated contract'+
 * (askable===1?'':'s')+' ('+kbNames.join(', ')+') · cites or abstains'`, with the closing promise
 * now "cites its sources" (persona v2.5: Ask never abstains; it names the gap and answers from the
 * market) --
 * and one further departure: the parenthetical name list is omitted entirely (never rendered as an empty `()`) when `supplierNames` is empty, an
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
  return `Answers only from ${validatedContractCount} validated ${plural}${names} · cites its sources`;
}

/** Screens-v2.md #2's own trailing sentence, appended after the scope line on the "new chat" state
 * only (`app.jsx` `askScope+'. Structured questions...'` is one concatenated paragraph in the
 * prototype; this module keeps the two halves separate so `index.tsx` can render `askScope` alone
 * for the conversation-view's own smaller usage without repeating this sentence there). Its
 * closing "or says it cannot answer" is persona v2.5's honest gap plus the market estimate. */
export const NEW_CHAT_TRAILER =
  "Structured questions run on validated fields, legal questions retrieve clauses — every answer cites its page, and what your contracts lack I estimate from market data, saying so.";

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

/** The Ask screen's own composer placeholder (`Raffa.ai V2.dc.html` `askPlaceholder`, quoted). */
export const ASK_INPUT_PLACEHOLDER = "Ask Raffa.ai — spend, dates, clauses, liability…";

// ---------------------------------------------------------------------------------------------
// Conversation chrome (`Raffa.ai V2.dc.html` "ASK RAFFA.AI — conversation with rich answers"):
// the header line above the thread, the new-chat intro and starter groups, the composer's note.
// Copy is quoted from that block's markup and `renderVals()`; nothing below is invented.
// ---------------------------------------------------------------------------------------------

/** `convTitle` while no conversation is open. */
export const ASK_NEW_CHAT_TITLE = "Ask Raffa.ai · new chat";

/** The AI turn's kicker and the thinking row's kicker (`Raffa.ai` in the V2 markup). */
export const ASK_RAFFA_KICKER = "Raffa.ai";

/** The new-chat intro paragraph under `askHello` -- the prototype's own, except its closing
 * "— or I say I cannot answer.", which persona v2.5 retired: when something is missing, Ask says
 * so and fills the gap with market data, labelled as an estimate, instead of declining. */
export const NEW_CHAT_INTRO =
  "I work on procurement only: what you bought, what you pay, when to act, where to save and how to negotiate. Every answer comes from your validated contracts and cites its page — when something is missing, I tell you and fill the gap with market data.";

/** The composer's right-hand note -- the prototype's "cites or abstains", now "cites its sources"
 * (persona v2.5: Ask never abstains). */
export const COMPOSER_NOTE = "Procurement only · cites its sources";

/**
 * `askScopeShort`: `askable + ' validated contract(s)' + ' · ' + kbNames.join(', ')`. The supplier
 * clause is dropped (not printed as a dangling " · ") while no validated contract names a supplier.
 */
export function buildScopeShort(validatedContractCount: number, supplierNames: readonly string[]): string {
  const plural = validatedContractCount === 1 ? "contract" : "contracts";
  const names = supplierNames.length > 0 ? ` · ${supplierNames.join(", ")}` : "";
  return `${validatedContractCount} validated ${plural}${names}`;
}

export interface StarterGroup {
  label: string;
  items: readonly string[];
}

/**
 * `starterGroups`: four labelled pairs (Save · Dates · Negotiate · Risk). The prototype names its
 * key fixture ("Salesforce") in five of the eight questions; here that slot is the workspace's own
 * first validated supplier (`useValidatedSuppliers.ts`, soonest notice first). Without a supplier
 * name those five questions are left out rather than printed with a placeholder -- a starter is a
 * real question the user can send as-is.
 */
export function buildStarterGroups(supplierName: string | null): readonly StarterGroup[] {
  const name = supplierName !== null && supplierName.trim() !== "" ? supplierName.trim() : null;
  const named = (question: string): string | null => (name === null ? null : question.replace("{supplier}", name));
  const groups: readonly { label: string; items: readonly (string | null)[] }[] = [
    { label: "Save", items: ["Where can I save the most this quarter?", named("What do we pay {supplier} per user vs the market?")] },
    { label: "Dates", items: ["Which contracts renew in the next 120 days?", named("When must we give notice to {supplier}?")] },
    { label: "Negotiate", items: [named("What are my levers with {supplier}?"), named("Prepare the renegotiation email for {supplier}")] },
    { label: "Risk", items: ["Which contracts have uncapped liability?", named("What are our obligations with {supplier}?")] },
  ];
  return groups
    .map((group) => ({ label: group.label, items: group.items.filter((item): item is string => item !== null) }))
    .filter((group) => group.items.length > 0);
}

/** screens-v2.md #2 "Thinking": V1 copy retained verbatim until the reply streams. */
/** ADR-030 D2: the draft card's English chrome (the email body itself arrives in the question's
 * language from the server). */
export const DRAFT_CARD_TITLE = "Draft email";
export const DRAFT_SUBJECT_LABEL = "Subject";
export const COPY_EMAIL_LABEL = "Copy email";
export const COPIED_LABEL = "Copied";

export const THINKING_COPY = "Authorising scope → detecting intent → retrieving evidence";

// ---------------------------------------------------------------------------------------------
// Bound-contract chip (task E27/F04/US01/T01, NW-78, wave w19; ADR-012 cl. 49 / ADR-020 37.2 per
// `reports/architecture/waves/w19.md` -- the w19 council-close footer that names those clause
// numbers was not actually appended to either ADR file in this worktree as of this task (only a
// w18 footer exists on each); `reports/architecture/waves/w19.md` NW-78's own row is the real,
// on-disk decision text this citation stands for, the same "cite the wave file, not an
// unwritten ADR clause" convention `../ask-bar/askSuggestions.test.ts`'s own NW-77 doc comment
// already established for this exact wave). screens-v2.md #2's "scope line" (`askScope`) is cited
// as this feature's anchor -- that line predates the chip and has no literal copy of its own for
// it (the same honest gap `buildScopedBrief`'s doc comment above already named for NW-56).
// ---------------------------------------------------------------------------------------------

export interface BoundContractChip {
  /** `/contracts/{id}` -- never a bare id rendered as the label (R-SUP-04 "never a guid"). */
  href: string;
  /** `{supplierName} · {type}`, e.g. "Salesforce · MSA". */
  label: string;
}

/**
 * AC-1: `{supplierName} · {type}` linking `/contracts/{id}`. A blank/absent `supplierName` falls
 * back to the same id-fragment label `contract360ViewModel.ts#resolveSupplierLabel` already
 * established for the 360 header's own kicker (`formatSupplier`, reused rather than re-derived --
 * R-SUP-04 "never a guid" applies here exactly as it does there) -- a supplier-less contract is
 * still resolvable and the chip still renders, it just names what it honestly has.
 */
export function buildBoundContractChip(
  contractId: string,
  header: Pick<Contract360HeaderBody, "supplierName" | "supplierId" | "type">,
): BoundContractChip {
  const supplierLabel =
    header.supplierName !== null && header.supplierName.trim() !== ""
      ? header.supplierName.trim()
      : formatSupplier(header.supplierId).label;
  return { href: `/contracts/${contractId}`, label: `${supplierLabel} · ${getContractTypeLabel(header.type)}` };
}

/**
 * AC-2/AC-3: the chip's own "supplier/type fetch" -- rebuilt from an explicit `contractId` (the
 * conversation's own persisted `scopeContractId`, sourced by the caller from the conversation
 * detail wire or the create response, **never** from `location.search`; this function has no URL
 * of any kind in its signature and cannot read one) plus a fresh `getContract360` read, the same
 * 360-header source `index.tsx`'s pre-existing `scopedSupplierName` effect already reads for the
 * new-chat brief (NW-56). Returns `null` -- "not yet resolvable" -- on a transport failure, a 404
 * (deleted or cross-tenant contract) or an `ok` response carrying no `contract`; `index.tsx` renders
 * nothing at all for `null` (AC-3), never a stale or placeholder chip.
 */
export async function fetchBoundContractChip(
  apiClient: ApiClient,
  tenantId: string,
  contractId: string,
): Promise<BoundContractChip | null> {
  const result = await apiClient.getContract360(tenantId, contractId);
  if (!result.ok || !result.contract) return null;
  return buildBoundContractChip(contractId, result.contract.header);
}

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
  /** ADR-030: a web source -- opened in a new tab (`noopener,noreferrer`), never an in-app
   * navigation to a public URL. */
  | { kind: "external"; url: string }
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
  if (citation.corpus === "web") {
    return citation.href && /^https:\/\//i.test(citation.href) ? { kind: "external", url: citation.href } : { kind: "none" };
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
 * fails -- the conversation still exists and is worth resuming.
 *
 * NW-78/AC-2: the success result also carries `scopeContractId` straight off `created.conversation`
 * -- the just-created conversation's **own persisted field**, echoed back by the server -- not the
 * `scopeContractId` parameter this function was called with. The two are equal on a well-behaved
 * backend, but `index.tsx`'s bound-contract chip must key off the former: the caller's own
 * `scopeContractId` local goes back to `undefined` the very next render (it is
 * `currentConversationId === null ? parseScopeContractId(...) : undefined`, and
 * `createdConversationId.current` is set synchronously before this promise's caller ever sees this
 * value), so it is gone before a second render could read it -- exactly the "never the transient
 * `?scope=` query" rule this same field's own doc comment on `BoundContractChip` above states.
 *
 * `onCreated` runs between the two calls, as soon as the conversation exists: `askSessions.ts`
 * uses it to move a new chat to its real id while the (slow) reply is still being written, so the
 * rail lists it and the user can open another chat in parallel. */
export async function createConversationAndAsk(
  apiClient: ApiClient,
  tenantId: string,
  question: string,
  scopeContractId: string | undefined,
  onCreated?: (conversation: { id: string; scopeContractId: string | null }) => void,
): Promise<
  | { ok: true; conversationId: string; reply: ConversationReplyBody; scopeContractId: string | null }
  | { ok: false; conversationId: string | null; reason: string }
> {
  const created = await apiClient.createConversation(tenantId, scopeContractId ? { scopeContractId } : {});
  if (!created.ok || !created.conversation) {
    return { ok: false, conversationId: null, reason: created.error ?? TRANSPORT_ERROR_REASON };
  }

  const conversationId = created.conversation.id;
  onCreated?.({ id: conversationId, scopeContractId: created.conversation.scopeContractId });
  const posted = await apiClient.postMessage(tenantId, conversationId, { question });
  if (!posted.ok || !posted.reply) {
    return { ok: false, conversationId, reason: posted.error ?? TRANSPORT_ERROR_REASON };
  }

  return { ok: true, conversationId, reply: posted.reply, scopeContractId: created.conversation.scopeContractId };
}
