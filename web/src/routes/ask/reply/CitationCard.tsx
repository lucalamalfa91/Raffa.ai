import ActionRow from "./ActionRow";
import { getCorpusBadge, citationOpenLabel, type ReplyCitation } from "./replyTypes";

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

function isViewerHref(href: string | null | undefined): href is string {
  return typeof href === "string" && href.startsWith(VIEWER_ROUTE_PREFIX);
}

/**
 * One citation, laid out as the V2 prototype's quote block (`Raffa.ai V2.dc.html`, `b.isQuote`):
 *
 *   <div style="border:1px solid var(--color-divider);background:#fff;max-width:720px">
 *     <div style="display:flex;justify-content:space-between;...;padding:8px 16px;border-bottom:1px solid var(--color-divider);font-size:11px">
 *       <span style="letter-spacing:.08em;text-transform:uppercase;color:neutral-600">{{ b.label }}</span>
 *       <span style="color:neutral-600;white-space:nowrap">{{ b.doc }} · p.{{ b.page }} · §{{ b.sec }}</span>
 *     <p style="padding:16px 18px;font-family:Georgia,serif;font-size:13px;line-height:1.65">{{ b.before }}<mark>{{ b.quote }}</mark>{{ b.after }}</p>
 *     <div style="display:flex;gap:16px;padding:8px 16px 10px;border-top:1px solid var(--color-divider)"><button class="btn btn-ghost" style="padding:0;font-size:12px">Open in Contract 360 →</button></div>
 *
 * The label slot carries this citation's `[n]` marker (the target of the inline `[n]` in the
 * answer) and its corpus name (`getCorpusBadge`: "Validated contract" / "Market · representative" /
 * "Raffa" -- text, never colour alone, ADR-019); the right-hand slot the citation's `title` and
 * `subtitle` ("Salesforce · MSA 2024 · p.12 §8.4"). The wire carries the cited passage alone (no
 * surrounding text), so the whole quote is the passage. A first-page preview (requirements.md §6,
 * `previewUrl`) renders under the quote when one exists -- the prototype's fixtures have none.
 *
 * Two shapes, one look: the single-interaction card is one `<button>` whose footer label is a
 * plain span; the two-CTA card (NW-83/NW-93, a viewer `href` plus a known `contractId`) is a
 * `<div>` whose footer holds two real `<Link>`s (`ActionRow`), never nested buttons.
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
  const cta = citationOpenLabel(href);

  const header = (
    <div className="citation-card-header">
      <span className="citation-card-label">
        <span className="citation-card-index">[{n}]</span> {badge.label}
      </span>
      <span className="citation-card-source">
        <span className="citation-card-title">{title}</span>
        {subtitle ? (
          <>
            <span aria-hidden="true"> · </span>
            <span className="citation-card-subtitle">{subtitle}</span>
          </>
        ) : null}
      </span>
    </div>
  );

  const body = (
    <>
      <blockquote className="citation-card-snippet">{snippet}</blockquote>
      {previewUrl ? <img className="citation-card-preview" src={previewUrl} alt={`${title} -- page preview`} /> : null}
    </>
  );

  if (isViewerHref(href) && contractId) {
    return (
      <div className="citation-card citation-card-two-cta" data-corpus={corpus}>
        {header}
        {body}
        <div className="citation-card-footer">
          <ActionRow
            actions={[
              { label: "Open contract", href: `/contracts/${contractId}`, kind: "primary" },
              { label: "Open at this span", href, kind: "secondary" },
            ]}
          />
        </div>
      </div>
    );
  }

  return (
    <button type="button" className="citation-card" data-corpus={corpus} onClick={onOpen}>
      {header}
      {body}
      <span className="citation-card-footer">
        <span className="citation-card-open">{cta}</span>
      </span>
    </button>
  );
}
