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
 * came from. Drives `CitationCard`'s badge (`getCorpusBadge` below) -- text, not colour alone,
 * still carries the meaning (ADR-019 accessibility baseline). */
export type CitationCorpus = "tenant" | "market" | "raffa";

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
   * -- already formatted by the pack (R-ASK-04); this card never re-derives it. */
  subtitle: string;
  /** Quoted evidence text, rendered with the accent-left rule (task text; the same "accent left
   * rule" idiom `.abstain-block`/`.ai-recommendation` already use, `styles/components.css`). */
  snippet: string;
  /** First-page preview image URL (`GET /api/documents/{id}/preview`, requirements.md §6). Absent
   * (or `null`) renders the honest placeholder block instead of a fabricated image. */
  previewUrl?: string | null;
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
 * navigations, never a callback-driven side effect like a citation open. */
export interface ReplyAction {
  label: string;
  href: string;
  kind: ReplyActionKind;
}

/** `reply.kind` (requirements.md §6) plus the client-only `"error"` a transport/network failure
 * produces (never conflated with an honest `"abstain"` -- the same rule `../askViewModel.ts
 * #ChatMessageKind` already documents for V1). */
export type ReplyKind = "answer" | "redirect" | "refusal" | "abstain" | "error";

/** `answer` = markdown body + citation cards + action buttons + follow-ups (task text) -- the only
 * kind with citations. */
export interface AnswerReply {
  kind: "answer";
  answerMarkdown: string;
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
}

/** `abstain` — the accent-left "Cannot determine reliably." block + `reason`, only for true
 * insufficiency (spec §10.4; screens-v2.md §2) -- never citations (task text: "the only place that
 * block appears"). May optionally carry the server-selected recovery action (ADR-024 "every
 * abstain has a clickable next step"; task E25/F05/US02/T01, parent story us-02-abstain-recovery-web
 * AC-1/AC-3): `../askViewModel.ts#buildReply` maps it off the wire's own generic `actions[]`
 * (backend: `CopilotReplyBuilder`'s `recoveryActions`, task E25/F05/US01/T01). `ReplyBody.tsx`
 * renders it as a **secondary** `ActionRow`, never primary, and only when present -- an abstain
 * with no action still renders just the block, never an empty screen (AC-3). */
export interface AbstainReply {
  kind: "abstain";
  reason: string;
  actions?: readonly ReplyAction[];
}

/** A transport/network failure -- not part of the wire's own `kind` enum, the same client-only
 * addition `../askViewModel.ts#ChatMessageKind` makes for V1. Renders the existing `.error-state`
 * (task text), never the abstain block. */
export interface ErrorReply {
  kind: "error";
  reason: string;
}

export type Reply = AnswerReply | RedirectReply | AbstainReply | ErrorReply;

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
  }
}
