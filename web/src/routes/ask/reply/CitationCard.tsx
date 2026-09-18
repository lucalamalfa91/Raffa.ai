import ActionRow from "./ActionRow";
import { getCorpusBadge, type ReplyCitation } from "./replyTypes";

export interface CitationCardProps
  extends Pick<ReplyCitation, "n" | "corpus" | "title" | "subtitle" | "snippet" | "previewUrl" | "href" | "contractId"> {
  /** The card's own single interaction (task text: "click -> onOpen") -- for every citation that
   * does *not* resolve to the two-CTA layout below. Never a direct `<Link to={href}>`: `href` is
   * optional (a not-yet-resolved tenant citation has none yet) and some opens need caller-side work
   * first (an async resolve, extra router state) -- see `replyTypes.ts#ReplyCitation.href`'s own
   * comment. `href` is still part of this component's props (spread in verbatim by `ReplyBody` from
   * the same `ReplyCitation` object) so a caller that only has this component's props in hand still
   * has it available; this branch of the component just never turns it into a competing native
   * link. Unused (but still accepted) when `isViewerHref(href) && contractId` picks the two-CTA
   * branch instead -- that branch navigates through real `<Link>`s (`ActionRow`), never a callback. */
  onOpen: () => void;
}

const VIEWER_ROUTE_PREFIX = "/documents/";

/**
 * A citation "carries a clause/span id" (task text, NW-83/NW-93) exactly when its own `href` has
 * already resolved to the W18 viewer deep-link (`/documents/{documentId}/viewer?page=&clause=`)
 * rather than the 360 fallback. `AskCopilotService.ResolveTenantClauseLinks` (task
 * E28/F03/US01/T01, NW-83) never sends a third shape for a tenant citation -- the viewer route
 * (with or without `&clause=`) or a `/contracts/{contractId}` route (bare, or carrying its own
 * `?clause=`/`?page=` half) -- so this prefix check is exact, not a heuristic.
 */
function isViewerHref(href: string | null | undefined): href is string {
  return typeof href === "string" && href.startsWith(VIEWER_ROUTE_PREFIX);
}

/**
 * One citation card (task text; R-WEB-04; requirements.md §6): corpus badge + title + subtitle +
 * quoted snippet (accent-left rule) + first-page preview -- or, for a `raffa`/`market` citation
 * (never carries a real `previewUrl`; ADR-024 §2's three-source split), a `.btn`-styled
 * call-to-action card instead of an empty placeholder (us-02-citation-card-web AC-1/AC-2, closing
 * NW-55; supersedes the always-present "No page preview available" text this card used to render
 * for every corpus). The whole card is one native `<button>` (ADR-019 accessibility baseline:
 * "every interactive control is native") rather than a styled `<div onClick>`, so it is
 * keyboard-operable for free; the CTA label below is a plain, non-interactive `<span>`, never a
 * nested `<button>`/`<a>`, so the card keeps exactly one interaction either way (AC-3).
 *
 * **Two-CTA card** (task E28/F03/US02/T01, NW-83/NW-93; parent story us-02-citation-two-cta
 * AC-1/AC-2/AC-3; `screens-v2.md` §2 citation card actions): once a citation resolves to a real
 * document page/span, this renders the two-CTA half of the pair instead of the single button above
 * -- `.btn-primary` "Open contract" (`/contracts/{contractId}`) and `.btn-secondary` "Open at this
 * span" (the viewer `href` verbatim) -- reusing `ActionRow` rather than a second bespoke button, as
 * a plain, non-interactive `<div>` rather than this card's own native `<button>`: `ActionRow`
 * renders real `<a>` elements (`react-router-dom`'s `Link`), and an anchor nested inside a
 * `<button>` is invalid, doubly-interactive markup -- never two nested buttons. The preview image
 * (when present) renders too, never previewUrl-only: the two actions are never dropped in its
 * favour. Every other shape -- market, raffa, or a tenant citation that never resolved past the 360
 * fallback -- falls back to the original single-button card below, `href` unused directly exactly
 * as before.
 *
 * `.card` (ADR-019 / `styles/components.css`: "recommendation/provenance blocks only") is the
 * right shared base -- a citation card *is* a provenance block -- with this folder's own
 * `.citation-card` modifier (`reply.css`) resetting the native button chrome (font, text-align,
 * width) that `.card` alone does not cover; the two-CTA `<div>` reuses the same classes, which
 * apply equally well to a non-button flex container.
 */
export default function CitationCard({
  n,
  corpus,
  title,
  subtitle,
  snippet,
  previewUrl,
  href,
  contractId,
  onOpen,
}: CitationCardProps) {
  const badge = getCorpusBadge(corpus);

  if (isViewerHref(href) && contractId) {
    return (
      <div className="card citation-card citation-card-two-cta">
        <div className="citation-card-header">
          <span className={`tag tag-${badge.variant}`}>{badge.label}</span>
          <span className="citation-card-index">[{n}]</span>
        </div>
        <p className="citation-card-title">{title}</p>
        <p className="citation-card-subtitle micro-meta">{subtitle}</p>
        <blockquote className="citation-card-snippet">{snippet}</blockquote>
        {previewUrl && <img className="citation-card-preview" src={previewUrl} alt={`${title} -- first page preview`} />}
        <ActionRow
          actions={[
            { label: "Open contract", href: `/contracts/${contractId}`, kind: "primary" },
            { label: "Open at this span", href, kind: "secondary" },
          ]}
        />
      </div>
    );
  }

  return (
    <button type="button" className="card citation-card" onClick={onOpen}>
      <div className="citation-card-header">
        <span className={`tag tag-${badge.variant}`}>{badge.label}</span>
        <span className="citation-card-index">[{n}]</span>
      </div>
      <p className="citation-card-title">{title}</p>
      <p className="citation-card-subtitle micro-meta">{subtitle}</p>
      <blockquote className="citation-card-snippet">{snippet}</blockquote>
      {previewUrl ? (
        <img className="citation-card-preview" src={previewUrl} alt={`${title} -- first page preview`} />
      ) : (
        <div className="citation-card-cta">
          <span className="btn btn-secondary">View source →</span>
        </div>
      )}
    </button>
  );
}
