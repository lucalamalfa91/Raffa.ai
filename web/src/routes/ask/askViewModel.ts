import type { ApiClient, AskContigoCitationBody, AskContigoIntent, AskContigoResponseBody } from "../../api/client";

/**
 * Pure(ish) view-model helpers for the Ask Contigo screen (route `/ask`, ADR-018; screens.md #7;
 * ADR-020 screen 7; task E07/F04/US01/T01, us-01-ask-contigo AC-1/AC-2/AC-3/AC-4). Same
 * one-concern-per-file split `../contracts/contract360/contract360ViewModel.ts` already established
 * for this app: `index.tsx` orchestrates React state/effects, this module decides what a chat turn
 * looks like from a raw `AskContigoResponseBody`.
 *
 * **Why "abstain" and "unknown question fallback" (ADR-020 screen 7's two named failure states)
 * share one rendering path**: the compiled prototype's own chat-message template
 * (`inputs/design/prototypes/day1-demo.html`, the `scr.ask` block) renders both through the exact
 * same markup -- a bold "Cannot determine reliably." followed by `{{ m.reason }}`, inside one
 * `border-left:2px solid var(--color-accent);background:var(--color-accent-100)` block (the
 * `.abstain-block` class in `../../styles/components.css`) -- varying only the `reason`/`route`
 * text (the prototype's own canned "no match" fallback entry is itself `abstain:true` with
 * `route:'Intent detected: unknown ...'`). This module follows the same rule: both cases set
 * `ChatMessageView.abstain = true`; `route` (AC-1's own two named examples, "Structured query…" /
 * "Clause retrieval…") is what actually distinguishes a Structured-not-wired turn from a genuine
 * Semantic no-evidence abstain.
 */

/** AC-1's own two named route-line examples ("Chat with route line ('Structured query…', 'Clause
 * retrieval…')"), keyed by the real backend's `intent` enum (`ChatEndpointExtensions`). Used
 * verbatim, not paraphrased -- the task text names these two exact strings. */
export const ROUTE_LINE_BY_INTENT: Readonly<Record<AskContigoIntent, string>> = {
  Structured: "Structured query…",
  Semantic: "Clause retrieval…",
};

/**
 * The one case the real API cannot explain per-query: a Semantic question that came back
 * `canDetermine: false` always has `message: null` on the wire (`ChatEndpointExtensions
 * .ToAnsweredResponse` hard-codes it) -- `AbstainGuard`'s own free-text `Reason` is deliberately
 * excluded from the response and the audit trail alike (that type's own doc comment). This is a
 * fixed, honest line rather than a fabricated per-query reason: under the fixture `IAiGateway`
 * wired today, `canDetermine` is `false` in the Semantic branch in exactly one situation -- tenant
 * -scoped retrieval found zero evidence at all (`FixtureAiGateway.AnswerAsync`) -- and every reason
 * `AbstainGuard` itself can produce for a real model (zero citations, an empty answer, or an
 * ungrounded citation) still reduces to the same honest claim: no reliable, evidenced answer was
 * found. See `openapi/contigo-api.v1.json`'s `askContigo` operation description for the full
 * provenance.
 */
export const SEMANTIC_ABSTAIN_REASON =
  "Contigo found no supporting evidence in your accessible contracts for this question.";

/** A transport/backend failure (network error, or a genuine 400) -- distinct from an honest AI
 * abstention, see `ChatMessageView.kind`'s own doc comment. */
export const TRANSPORT_ERROR_REASON = "Contigo's Q&A service is temporarily unavailable. Try again in a moment.";

export type ChatRole = "you" | "contigo";

/**
 * `answer` = a real, cited (or citation-less) grounded answer. `abstain` = the AI honestly could not
 * determine an answer (Semantic no-evidence) or the question was routed Structured and is not
 * wired to live data yet (screens.md #7's two named failure states -- see this module's own header
 * comment for why they share one visual treatment). `error` = the request itself failed (network/
 * 400) -- never conflated with "the AI could not determine an answer": that is always `abstain`.
 */
export type ChatMessageKind = "answer" | "abstain" | "error";

export interface ChatCitationView {
  /** 1-based position within *this message's own* citation list (day1-demo.html's own `{n:i+1}`
   * mapping) -- not a global counter across the whole conversation. */
  n: number;
  /** The raw composite id the API returned (`SourceType:SourceId`) -- shown verbatim as the chip's
   * "doc" label. Resolving it to a human-readable filename would need an extra round trip per
   * citation before the message can even render; `../../ask/index.tsx#resolveCitationContractId`
   * resolves it lazily, only when the user actually clicks a chip. */
  documentId: string;
  /** Parsed `SourceType` half of `documentId` (e.g. `"Document"`, `"Clause"`), or `null` if
   * `documentId` did not have the expected `type:id` shape at all. */
  sourceType: string | null;
  /** Parsed `SourceId` half of `documentId`, or `null` -- see `sourceType`. */
  sourceId: string | null;
  page: number | null;
  section: string | null;
}

export interface ChatMessageView {
  /** Stable id for React keys -- an incrementing counter (`index.tsx`'s own `nextId` ref), not the
   * array index, so a message never changes identity as the log grows. */
  id: string;
  role: ChatRole;
  /** Empty string for every `abstain`/`error` turn (the block itself carries the message -- see
   * `kind`'s own doc comment) and for a still-pending "You" bubble is never empty (the user's own
   * typed text). */
  text: string;
  /** AC-1's route line, or `null` for a "You" message (a route is something Contigo decides, not
   * the user). */
  route: string | null;
  kind: ChatMessageKind;
  /** Populated only when `kind !== "answer"`. */
  reason: string | null;
  /** Populated only when `kind === "answer"` and the response actually carried citations. */
  citations: readonly ChatCitationView[];
}

/** Parses the composite `documentId` the backend returns (`ChatEndpointExtensions
 * .ToEvidenceSnippet`'s `${SourceType}:${SourceId}`) into its two halves. Returns `null` rather
 * than guessing when the string does not have that shape at all -- an honest "cannot parse", not a
 * silently wrong split. */
export function parseCitationSource(documentId: string): { sourceType: string; sourceId: string } | null {
  const separatorIndex = documentId.indexOf(":");
  if (separatorIndex <= 0 || separatorIndex === documentId.length - 1) return null;
  return {
    sourceType: documentId.slice(0, separatorIndex),
    sourceId: documentId.slice(separatorIndex + 1),
  };
}

export function buildCitationViews(citations: readonly AskContigoCitationBody[]): ChatCitationView[] {
  return citations.map((citation, index) => {
    const parsed = parseCitationSource(citation.documentId);
    return {
      n: index + 1,
      documentId: citation.documentId,
      sourceType: parsed?.sourceType ?? null,
      sourceId: parsed?.sourceId ?? null,
      page: citation.page,
      section: citation.section,
    };
  });
}

let messageIdCounter = 0;

/** Monotonic, test-friendly id generator -- exported so `index.tsx` and tests share one counter
 * shape instead of each re-deriving one. Not `crypto.randomUUID()`: nothing here needs global
 * uniqueness, only "never repeats within one mounted screen". */
export function nextMessageId(): string {
  messageIdCounter += 1;
  return `ask-message-${messageIdCounter}`;
}

export function buildYouMessage(id: string, text: string): ChatMessageView {
  return { id, role: "you", text, route: null, kind: "answer", reason: null, citations: [] };
}

/**
 * Turns a real `POST /api/chat/query` 200 response into a chat turn. `response.canDetermine ===
 * true` is the only path that ever surfaces `response.answer`/citations; every other outcome is an
 * honest `abstain` turn (see this module's header comment for why Structured-not-wired and Semantic
 * no-evidence share the same `kind`).
 */
export function buildContigoMessage(id: string, response: AskContigoResponseBody): ChatMessageView {
  const route = ROUTE_LINE_BY_INTENT[response.intent] ?? null;

  if (response.canDetermine && response.answer !== null) {
    return {
      id,
      role: "contigo",
      text: response.answer,
      route,
      kind: "answer",
      reason: null,
      citations: buildCitationViews(response.citations),
    };
  }

  return {
    id,
    role: "contigo",
    text: "",
    route,
    kind: "abstain",
    reason: response.message ?? SEMANTIC_ABSTAIN_REASON,
    citations: [],
  };
}

export function buildErrorMessage(id: string, reason: string): ChatMessageView {
  return { id, role: "contigo", text: "", route: null, kind: "error", reason, citations: [] };
}

/** AC-4 "thinking" state copy, quoted verbatim from the compiled prototype's own chat-log block
 * (day1-demo.html: "Authorising scope → detecting intent → retrieving evidence") -- matches
 * screens.md #7's own parenthetical, "thinking (authorise → intent → retrieve)". */
export const THINKING_COPY = "Authorising scope → detecting intent → retrieving evidence";

/** AC-4 "empty" state copy -- adapted from the compiled prototype's own `chatEmpty` block
 * (day1-demo.html), which states the same routing/citation/abstain contract this screen implements
 * rather than a generic "ask me anything" placeholder. */
export const EMPTY_STATE_COPY =
  "Structured questions run as deterministic queries on validated fields; legal and semantic questions retrieve clauses. Every answer cites its source, or says it cannot determine reliably.";

/** Right-rail "Try" suggestions, quoted verbatim from the compiled prototype's own `suggestionsTxt`
 * array (day1-demo.html) -- not invented copy (ADR-019: consume the prototype, do not fork it). */
export const ASK_SUGGESTIONS: readonly string[] = [
  "Which contracts renew in the next 120 days?",
  "What is our Microsoft annual spend?",
  "What liability do we have with AWS?",
  "Which contracts contain unlimited liability?",
  "What is our total liability exposure across all contracts?",
];

export type CitationOpenResult = { ok: true; contractId: string } | { ok: false; reason: string };

/**
 * Resolves a citation chip to a Contract 360 id, or an honest reason it cannot (AC-2 "opening
 * Contract 360 > Clauses"). Only a `Document:<id>` citation is resolvable today: `GET
 * /api/documents/{id}` (already wrapped as `apiClient.getDocument`) is the one existing endpoint
 * that maps a source id back to a `contractId`. A `Clause:<id>` citation (or any other/unparsable
 * `sourceType`) has no equivalent lookup anywhere in this backend yet (`Embedding.SourceType`'s own
 * doc comment: "Document or Clause content today" is a loose pointer, not a foreign key) -- named
 * here as a real gap rather than guessed at, per `openapi/contigo-api.v1.json`'s `askContigo`
 * operation description.
 */
export async function resolveCitationContractId(
  apiClient: ApiClient,
  tenantId: string,
  citation: ChatCitationView,
): Promise<CitationOpenResult> {
  if (citation.sourceType !== "Document" || citation.sourceId === null) {
    return {
      ok: false,
      reason: "Contigo can't jump to Contract 360 from a clause-level citation yet — open the Documents tab to find the source contract.",
    };
  }

  const result = await apiClient.getDocument(tenantId, citation.sourceId);
  if (!result.ok || !result.document) {
    return {
      ok: false,
      reason: result.error ?? "This citation could not be opened right now. Try again in a moment.",
    };
  }

  if (result.document.contractId === null) {
    return {
      ok: false,
      reason: "This document is not linked to a contract yet.",
    };
  }

  return { ok: true, contractId: result.document.contractId };
}
