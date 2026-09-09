import { getCorpusBadge, type ReplyCitation } from "./replyTypes";

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
 * quoted snippet (accent-left rule) + first-page preview or an honest placeholder. The whole card
 * is one native `<button>` (ADR-019 accessibility baseline: "every interactive control is
 * native") rather than a styled `<div onClick>`, so it is keyboard-operable for free.
 *
 * `.card` (ADR-019 / `styles/components.css`: "recommendation/provenance blocks only") is the
 * right shared base -- a citation card *is* a provenance block -- with this folder's own
 * `.citation-card` modifier (`reply.css`) resetting the native button chrome (font, text-align,
 * width) that `.card` alone does not cover.
 */
export default function CitationCard({ n, corpus, title, subtitle, snippet, previewUrl, onOpen }: CitationCardProps) {
  const badge = getCorpusBadge(corpus);

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
        <div className="citation-card-preview-placeholder">
          <span className="micro-meta">No page preview available</span>
        </div>
      )}
    </button>
  );
}
