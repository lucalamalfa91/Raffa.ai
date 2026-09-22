/**
 * Reply contract — presentation-level types for the V2 rich reply (route `/ask`, ADR-024;
 * ADR-020 V2 amendment "screen 2 Ask Raffa"; requirements.md §6 "kind decides the layout";
 * R-WEB-04; task E13/F09/US01/T02, us-01-web-v2 AC-3).
 *
 * Deliberately **not** generated from `web/openapi/raffa-api.v1.json` /
 * `web/src/api/generated/schema.ts` (out of this task's own "Files to create or modify" -- the
 * OpenAPI contract does not carry the V2 conversations/messages shape yet, only the V1
 * `POST /api/chat/query` reply `../askViewModel.ts` already consumes). `ReplyBody` /
 * `CitationCard` / `ActionRow` / `ReplyMarkdown` are "pure, API-agnostic" components (this task's
 * own coding objective): they render whatever satisfies this module's types. Task F09/T04 ("wired
 * to the real reply contract") owns mapping the generated wire type onto `Reply` once the V2
 * endpoints land -- a structural, not a nominal, mapping, since every field name below is quoted
 * verbatim from requirements.md §6's own JSON example.
 *
 * A discriminated union on `kind`, not one flat optional-everything shape: `answer` is the only
 * variant that ever carries citations; `redirect`/`refusal` always carry their one primary CTA;
 * `abstain` optionally carries a **secondary-only** recovery action (ADR-024 "every abstain has a
 * clickable next step", task E25/F05/US02/T01); `error` never carries any action. Illegal
 * combinations (e.g. an abstain with citation cards) are unrepresentable rather than merely
 * undocumented -- `ReplyBody.tsx`'s own `switch (reply.kind)` is exhaustive over this union
 * (`noFallthroughCasesInSwitch`, tsconfig.json).
 */

/** `citations[].corpus` (requirements.md §6): which of the three sources (ADR-024 §2) a citation
 * came from, plus the backend's own fourth value `"calc"` (`PackCorpus.Calc`: a deterministic
 * calculator's output -- a criticality score, a lever, an aggregate). Drives the evidence card's
 * section and badge (`getCorpusBadge` below) -- text, not colour alone, still carries the meaning
 * (ADR-019 accessibility baseline). */
export type CitationCorpus = "tenant" | "market" | "raffa" | "calc" | "web";

/**
 * One entry of `citations[]` (requirements.md §6 JSON example), narrowed to exactly the fields
 * `CitationCard.tsx` renders (task text: "props: n, corpus, title, subtitle, snippet, previewUrl?,
 * href?") plus `contractId` (task E28/F03/US02/T01, NW-83/NW-93 -- see that field's own doc
 * comment below). The wire shape also carries `documentId`/`page`/`section`/`recordId` --
 * identifiers this presentational card still never renders as visible text (R-ASK-08 "no guids
 * ever rendered") and does not need: `href` already arrives fully resolved (including a real
 * document's page and clause/span, when one exists -- `AskCopilotService
 * .ResolveTenantClauseLinks`, NW-83) and `contractId` is used only to build a second CTA's own
 * `href` attribute, never printed as text -- the same standing exception `href` itself already
 * relies on. F09/T04's adapter keeps the rest on its own richer type for navigation/resolution --
 * the V1 precedent for that split is `../askViewModel.ts`'s `ChatCitationView`, which also keeps
 * `sourceType`/`sourceId` off the rendered chip.
 */
export interface ReplyCitation {
  /** 1-based position within *this reply's own* citation list -- matches the `[n]` marker
   * `ReplyMarkdown` looks for inside `answerMarkdown` (requirements.md §6: `"...15 January
   * 2027** [1] …"`). Not a global counter across a whole conversation. */
  n: number;
  corpus: CitationCorpus;
  /** Human title (e.g. "Salesforce · MSA 2024", "Renewals") -- never a filename-as-id or a guid
   * (R-ASK-08). */
  title: string;
  /** e.g. "p.12 §8.4", "representative market data · mock feed · updated 2026-09-01", "/renewals"
   * -- already formatted by the pack (R-ASK-04); this card never re-derives it. Absent when the
   * pack only has a title + quote (the Ask card still renders the excerpt). */
  subtitle?: string | null;
  /** Quoted evidence text, rendered with the accent-left rule (task text; the same "accent left
   * rule" idiom `.abstain-block`/`.ai-recommendation` already use, `styles/components.css`). */
  snippet: string;
  /** First-page preview image URL (`GET /api/documents/{id}/preview`, requirements.md §6). Absent
   * (or `null`) renders the honest placeholder block instead of a fabricated image. Prefer an
   * authenticated object URL (`getDocumentPreviewUrl`) over the raw API path — `<img src>` cannot
   * send the tenant/auth headers that route needs. */
  previewUrl?: string | null;
  /** Tenant document this citation points at. Never rendered (R-ASK-08); used only to fetch a
   * real page preview when `previewUrl` is missing. */
  documentId?: string | null;
  /** 1-based page for `GET /api/documents/{id}/preview?page=`. */
  page?: number | null;
  /** In-app destination (e.g. `/contracts/…?clause=…`, `/renewals`). Carried on the citation for
   * the caller's own navigation decision (F09/T04's real `onOpenCitation`) -- `CitationCard` never
   * turns this into a competing native link itself; some citations (a not-yet-resolved tenant
   * citation) have no `href` at all and must resolve asynchronously first, the same shape
   * `../askViewModel.ts#resolveCitationContractId` already established for V1. This card's one
   * interaction is always "click -> onOpen" (task text) -- unless `contractId` below turns it into
   * the two-CTA card instead (`CitationCard.tsx`). */
  href?: string | null;
  /** This citation's own contract id, when it resolves to exactly one (task E28/F03/US02/T01,
   * NW-83/NW-93; echoes the wire's `contractId`, itself the composition root's
   * `PackItem.ContractId` verbatim -- never re-derived from a citation key). Used only to build
   * the two-CTA card's primary "Open contract" action (`/contracts/{contractId}`) once `href`
   * above has already resolved to the W18 viewer route (`CitationCard.tsx`'s own `isViewerHref`) --
   * never rendered as visible text (R-ASK-08 still holds: an id inside an `href` attribute is not
   * a guid "rendered", the same exception `href` itself already relies on). Absent/`null` for a
   * market/raffa citation and for an NW-81 "similar types" peer hit, exactly like the wire's own
   * field never resolves those either -- both then keep today's single-button fallback card. */
  contractId?: string | null;
}

/** `actions[].kind` (requirements.md §6) -> `.btn-primary` / `.btn-secondary` (task text). */
export type ReplyActionKind = "primary" | "secondary";

/** One entry of `actions[]` (requirements.md §6): `{ label, href, kind }`, rendered as a real
 * in-app link styled `.btn` -- there is no `onClick` field on the wire, these are plain
 * navigations, never a callback-driven side effect like a citation open. `external` (ADR-030 D6):
 * the wire's own `kind: "external"` -- an absolute https URL (today only the GitHub issue a
 * feedback submission opened) that `ActionRow` renders as a plain `<a target="_blank">`, never a
 * router `<Link>`. */
export interface ReplyAction {
  label: string;
  href: string;
  kind: ReplyActionKind;
  external?: boolean;
}

// ---------------------------------------------------------------------------------------------
// ADR-030: the reply's structured half (`payload`) -- capability gap, drafted email, feedback
// ---------------------------------------------------------------------------------------------

/** `payload.gap`: which catalog entry fired (`Raffa.Chat.Application.Gaps.CapabilityGapCatalog`)
 * and the language the server wrote the deterministic copy in. */
export interface ReplyGap {
  key: string;
  title: string;
  language: string;
}

/** `payload.draft`: the drafted negotiation email, plain text, rendered verbatim by `DraftCard`
 * (never through `ReplyMarkdown`/`humanizeReplyText`) so "Copy email" copies exactly what the
 * server wrote. */
export interface ReplyDraft {
  subject: string;
  body: string;
}

export interface FeedbackChoice {
  key: string;
  label: string;
}

/** One of the three interview questions of the feedback card (`payload.feedbackOffer.questions`). */
export interface FeedbackQuestion {
  key: string;
  kind: "text" | "choice";
  label: string;
  prefill?: string | null;
  choices?: readonly FeedbackChoice[] | null;
}

/** `payload.feedbackOffer`: everything the in-chat feedback card renders, already localised by
 * the server (ADR-030 D4/D5) -- this client owns no copy of its own for it. */
export interface FeedbackOffer {
  prompt: string;
  yesLabel: string;
  noLabel: string;
  nextLabel: string;
  backLabel: string;
  submitLabel: string;
  sendingLabel: string;
  thanksLabel: string;
  errorLabel: string;
  publicNotice: string;
  questions: readonly FeedbackQuestion[];
}

/** `payload.feedbackResult`: carried by the confirmation turn `POST …/feedback` appends -- tells
 * a resumed conversation which offer was already answered. */
export interface FeedbackResult {
  forMessageId: string;
  status: "recorded" | "issue_opened";
  issueNumber: number | null;
  issueUrl: string | null;
}

/** The three answers `FeedbackCard` submits (`FeedbackQuestion.key` -> value). */
export interface FeedbackAnswers {
  what: string;
  frequency: string;
  importance: string;
}

/** `reply.kind` (requirements.md §6) plus the client-only `"error"` a transport/network failure
 * produces (never conflated with an honest `"abstain"` -- the same rule `../askViewModel.ts
 * #ChatMessageKind` already documents for V1). */
export type ReplyKind = "answer" | "redirect" | "refusal" | "abstain" | "draft" | "interview" | "error";

/** `answer` = markdown body + citation cards + action buttons + follow-ups (task text) -- the only
 * kind with citations. */
export interface AnswerReply {
  kind: "answer";
  answerMarkdown: string;
  citations: readonly ReplyCitation[];
  actions: readonly ReplyAction[];
  followUps: readonly string[];
  /** ADR-030 D5: set only on the feedback confirmation turn. */
  feedbackResult?: FeedbackResult | null;
  /** ADR-030: this answer came from the public web after the user's consent -- nothing in it was
   * checked against the tenant's contracts. `ReplyBody` renders the "unverified" banner and the
   * evidence card files every `web` citation under its own labelled section. */
  unverifiedWeb?: boolean;
}

/** `draft` (ADR-030 D2): the honest preface as markdown, the drafted email as a verbatim card
 * with "Copy email", the pack items the email was written from as citation cards, the actions,
 * two follow-ups, and the feedback offer. Never the abstain block: the draft path cannot abstain. */
export interface DraftReply {
  kind: "draft";
  answerMarkdown: string;
  draft: ReplyDraft;
  gap: ReplyGap;
  feedbackOffer: FeedbackOffer | null;
  citations: readonly ReplyCitation[];
  actions: readonly ReplyAction[];
  followUps: readonly string[];
}

/** `redirect` (greeting / off-domain / needs_document) and `refusal` (legal) share one layout:
 * warm prose + **one** CTA, never the abstain block (R-ASK-07; task text). `actions` stays a plain
 * array rather than a one-tuple type -- the *contract* guarantees exactly one (R-ASK-07 / parent
 * AC-3 "one CTA"), and `ReplyBody` renders defensively (only ever the first) rather than trusting
 * every future caller to enforce it upstream; see that component's own comment. */
export interface RedirectReply {
  kind: "redirect" | "refusal";
  answerMarkdown: string;
  actions: readonly ReplyAction[];
  /** ADR-030: a capability-gap redirect ("which contract?") offers one validated supplier per
   * follow-up chip; every other redirect/refusal carries none. */
  followUps?: readonly string[];
  gap?: ReplyGap | null;
  feedbackOffer?: FeedbackOffer | null;
}

/** `abstain` — Raffa's own way forward when no grounded answer exists (persona v2.4: Ask never
 * answers "I don't have data I trust enough"): `reason` is the server's proposal -- a plan, a
 * ready-to-send draft, the screen to open -- rendered as plain reply markdown, never the old
 * accent-left "cannot determine" block, which read as an error. Never citations. May optionally
 * carry the server-selected recovery action (ADR-024 "every
 * abstain has a clickable next step"; task E25/F05/US02/T01, parent story us-02-abstain-recovery-web
 * AC-1/AC-3): `../askViewModel.ts#buildReply` maps it off the wire's own generic `actions[]`
 * (backend: `CopilotReplyBuilder`'s `recoveryActions`, task E25/F05/US01/T01). `ReplyBody.tsx`
 * renders it as a **secondary** `ActionRow`, never primary, and only when present -- an abstain
 * with no action still renders just its prose, never an empty screen (AC-3). May also carry the
 * server's next-step questions (`followUps`, only when non-empty) so a gap is never a dead end;
 * still never citations. */
export interface AbstainReply {
  kind: "abstain";
  reason: string;
  actions?: readonly ReplyAction[];
  followUps?: readonly string[];
}

/** ADR-030: one clarifying question (or two) with clickable options, asked before Raffa
 * retrieves anything. `presentation: "consent"` is the web-research authorization the SPA
 * renders as an alert dialog. Answered by key through `PostMessageRequest.interviewAnswer`;
 * `answered` disables the chips once the user moved on. `messageId` is the server message this
 * turn is, needed to answer it; null only for a turn the client could not identify. */
export type InterviewPresentation = "choice" | "consent";

export interface InterviewOption {
  key: string;
  label: string;
  hint: string | null;
}

export interface InterviewQuestion {
  key: string;
  prompt: string;
  presentation: InterviewPresentation;
  allowFreeText: boolean;
  options: readonly InterviewOption[];
}

export interface InterviewReply {
  kind: "interview";
  prompt: string;
  questions: readonly InterviewQuestion[];
  answered: boolean;
  messageId: string | null;
}

/** A transport/network failure -- not part of the wire's own `kind` enum, the same client-only
 * addition `../askViewModel.ts#ChatMessageKind` makes for V1. Renders the existing `.error-state`
 * (task text), never the abstain block. */
export interface ErrorReply {
  kind: "error";
  reason: string;
}

export type Reply = AnswerReply | DraftReply | RedirectReply | AbstainReply | InterviewReply | ErrorReply;

/** `CitationCard`'s corpus badge (task text: "*validated contract* / *market · representative* /
 * *Raffa*"), reusing the app-wide `{ variant, label }` shape `../../../styles/semantics.ts`
 * already establishes for every other semantic tag -- kept local to this module rather than added
 * to that shared file, which is outside this task's own "Files to create or modify" (ADR-019 owns
 * that file's locked confidence/status/risk catalogue; corpus is a new, reply-only mapping, not
 * one of ADR-019's own rows). Text, not colour alone, still carries the meaning (ADR-019
 * accessibility baseline): each variant below is always paired with its printed label. */
export interface CorpusBadge {
  variant: "neutral" | "accent" | "outline";
  label: string;
}

export function getCorpusBadge(corpus: CitationCorpus): CorpusBadge {
  switch (corpus) {
    case "tenant":
      return { variant: "neutral", label: "Validated contract" };
    case "market":
      return { variant: "outline", label: "Market · representative" };
    case "raffa":
      return { variant: "accent", label: "Raffa" };
    case "calc":
      return { variant: "accent", label: "Raffa · calculated" };
    case "web":
      return { variant: "outline", label: "Web · unverified" };
  }
}
