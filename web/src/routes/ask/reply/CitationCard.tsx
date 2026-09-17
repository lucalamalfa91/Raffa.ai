import { getCorpusBadge, citationOpenLabel, type ReplyCitation } from "./replyTypes";

export interface CitationCardProps
  extends Pick<ReplyCitation, "n" | "corpus" | "title" | "subtitle" | "snippet" | "previewUrl" | "href"> {
  /** The card's own single interaction (task text: "click -> onOpen"). Never a direct
   * `<Link to={href}>`: `href` is optional (a not-yet-resolved tenant citation has none yet) and
   * some opens need caller-side work first (an async resolve, extra router state) -- see
   * `replyTypes.ts#ReplyCitation.href`'s own comment. `href` is still part of this component's
   * props (spread in verbatim by `ReplyBody` from the same `ReplyCitation` object) so a caller that
   * only has this component's props in hand still has it available; this component itself just
   * never turns it into a competing native link. */
  onOpen: () => void;
}

/**
 * One citation card (task text; R-WEB-04; requirements.md §6): corpus badge + title + subtitle +
 * the grounded quote (the pack snippet / page excerpt) at readable size, an optional page preview,
 * and a CTA to open the source. The whole card is one native `<button>` (ADR-019 accessibility
 * baseline: "every interactive control is native"). The CTA label is a plain, non-interactive
 * `<span>`, never a nested `<button>`/`<a>`, so the card keeps exactly one interaction (AC-3).
 *
 * A missing preview must not leave an empty dashed void — the quote is the card. `previewUrl` is
 * an authenticated object URL when Ask has already fetched the PNG (`useCitationPreviews`); a raw
 * `/api/documents/.../preview` path cannot carry tenant/auth headers as `<img src>`.
 */
export default function CitationCard({ n, corpus, title, subtitle, snippet, previewUrl, href, onOpen }: CitationCardProps) {
  const badge = getCorpusBadge(corpus);
  const cta = citationOpenLabel(href);

  return (
    <button type="button" className="card citation-card" onClick={onOpen}>
      <div className="citation-card-header">
        <span className={`tag tag-${badge.variant}`}>{badge.label}</span>
        <span className="citation-card-index">[{n}]</span>
      </div>
      <p className="citation-card-title">{title}</p>
      {subtitle ? <p className="citation-card-subtitle micro-meta">{subtitle}</p> : null}
      {previewUrl ? (
        <img className="citation-card-preview" src={previewUrl} alt={`${title} -- page preview`} />
      ) : null}
      <blockquote className="citation-card-snippet">{snippet}</blockquote>
      <span className="citation-card-open">
        <span className="btn btn-secondary">{cta}</span>
      </span>
    </button>
  );
}
