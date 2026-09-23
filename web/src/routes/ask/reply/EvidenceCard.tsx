import { useId, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { DocumentViewerLink } from "../../documents/viewer/DocumentViewerOverlay";
import { isDocumentViewerHref } from "../../documents/viewer/documentViewerViewModel";
import ActionRow from "./ActionRow";
import {
  buildEvidenceActions,
  describeEvidence,
  evidenceRowLabel,
  groupCitations,
  webSourceHost,
  type ContractEvidenceGroup,
} from "./evidenceGrouping";
import type { ReplyAction, ReplyCitation } from "./replyTypes";

export interface EvidenceCardProps {
  citations: readonly ReplyCitation[];
  /** The backend's `actions[]` for this reply; folded into the card's one action row. */
  actions: readonly ReplyAction[];
  /** Same callback the inline `[n]` markers use (`ReplyMarkdown`); a row's `[n]` opens the same
   * destination -- Contract 360, the viewer overlay or the market-record panel. */
  onOpenCitation: (citation: ReplyCitation) => void;
}

/** A snippet longer than this collapses to three lines with a More/Less toggle. */
export const SNIPPET_COLLAPSE_LENGTH = 240;

/** "1 source" / "4 sources" -- the collapsed row's lead. */
export function sourcesLabel(count: number): string {
  return count === 1 ? "1 source" : `${count} sources`;
}

/**
 * The one evidence block under an `answer` (replaces the one-card-per-citation stack), in the
 * Claude.ai "sources" shape: the answer stays prose-first, its sources sit behind **one collapsed
 * row** ("4 sources · Salesforce, Microsoft · 2 contracts · 1 Raffa item"), and the inline `[n]`
 * chips (`ReplyMarkdown`) preview each one on hover. Expanded, every contract the answer cites sits
 * under its supplier with the row's own fact or clause, market records and Raffa items follow in
 * their own sections. The action row (`buildEvidenceActions`: Portfolio filtered to these
 * contracts, or the one contract's Contract 360, plus the backend's actions, three at most) stays
 * visible under the row -- it is the reply's next step, not part of the collapsed detail.
 *
 * Keeps the `.citation-card*` hooks the e2e specs read (`web/e2e/v2.spec.ts`): the root is still a
 * `.citation-card`, the always-visible row still carries one `.citation-card-title` /
 * `.citation-card-subtitle` pair, actions still render inside `.reply-actions`. The collapsed
 * detail is `hidden`, not unmounted, so its text (the provenance section titles) stays in the
 * document. R-ASK-08 still holds: ids only ever appear inside `href` attributes, never as text.
 */
export default function EvidenceCard({ citations, actions, onOpenCitation }: EvidenceCardProps) {
  const groups = useMemo(() => groupCitations(citations), [citations]);
  const footerActions = useMemo(() => buildEvidenceActions(groups, actions), [groups, actions]);
  const summary = useMemo(() => describeEvidence(groups), [groups]);
  const [expanded, setExpanded] = useState<ReadonlySet<number>>(() => new Set());
  const [open, setOpen] = useState(false);
  const bodyId = useId();

  if (citations.length === 0) return null;

  // With exactly one contract the footer's primary button already is its Contract 360, so the
  // group heading carries no duplicate link; with several, each group links to its own 360.
  const linkedGroups = groups.contracts.filter((group) => group.href !== null).length;
  const showGroupLinks = linkedGroups > 1;

  const toggle = (n: number) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(n)) next.delete(n);
      else next.add(n);
      return next;
    });

  const renderRow = (citation: ReplyCitation, label: string | null, clickable: boolean) => {
    const isLong = citation.snippet.length > SNIPPET_COLLAPSE_LENGTH;
    const isExpanded = expanded.has(citation.n);
    const viewerHref = citation.href && isDocumentViewerHref(citation.href) ? citation.href : null;
    // ADR-030: a web source opens in a new tab through a real anchor (noopener, noreferrer) --
    // the app never navigates itself to a public URL, and the visible text is the host, not the
    // URL.
    const webHref = citation.corpus === "web" && citation.href ? citation.href : null;
    const webHost = webSourceHost(webHref);

    return (
      <li key={citation.n} className="evidence-row" data-n={citation.n}>
        {clickable ? (
          <button
            type="button"
            className="evidence-row-ref"
            aria-label={`Open source ${citation.n}`}
            onClick={() => onOpenCitation(citation)}
          >
            {citation.n}
          </button>
        ) : (
          <span className="evidence-row-ref evidence-row-ref-static">{citation.n}</span>
        )}
        <div className="evidence-row-body">
          {(label || citation.subtitle) && (
            <div className="evidence-row-meta">
              {label && <span className="evidence-row-label">{label}</span>}
              {label && citation.subtitle && <span aria-hidden="true"> · </span>}
              {citation.subtitle && <span className="evidence-row-subtitle">{citation.subtitle}</span>}
            </div>
          )}
          <p className="evidence-row-snippet" data-collapsed={isLong && !isExpanded ? "true" : "false"}>
            {citation.snippet}
          </p>
          {(isLong || viewerHref || webHref) && (
            <div className="evidence-row-links">
              {viewerHref && (
                <DocumentViewerLink to={viewerHref} className="evidence-link">
                  Open at this span
                </DocumentViewerLink>
              )}
              {webHref && webHost && (
                <a href={webHref} className="evidence-link evidence-row-external" target="_blank" rel="noopener noreferrer">
                  {webHost} ↗
                </a>
              )}
              {isLong && (
                <button type="button" className="evidence-row-toggle" onClick={() => toggle(citation.n)}>
                  {isExpanded ? "Less" : "More"}
                </button>
              )}
            </div>
          )}
        </div>
      </li>
    );
  };

  const renderContractGroup = (group: ContractEvidenceGroup) => (
    <div key={group.key} className="evidence-group" data-contract-group="">
      <div className="evidence-group-head">
        <span className="evidence-group-title">{group.title}</span>
        {showGroupLinks && group.href !== null && (
          <Link to={group.href} className="evidence-link">
            Contract 360 →
          </Link>
        )}
      </div>
      <ul className="evidence-rows">
        {group.rows.map((citation) =>
          renderRow(citation, evidenceRowLabel(citation, group.title), Boolean(citation.href)),
        )}
      </ul>
    </div>
  );

  return (
    <div className="citation-card evidence-card" data-corpus="mixed" data-open={open ? "true" : "false"}>
      <button
        type="button"
        className="citation-card-header evidence-toggle"
        aria-expanded={open}
        aria-controls={bodyId}
        aria-label={`${sourcesLabel(citations.length)}: ${summary.title} · ${summary.subtitle}`}
        onClick={() => setOpen((current) => !current)}
      >
        <span className="evidence-toggle-icon" aria-hidden="true">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" focusable="false">
            <path d="M7 3h7l5 5v13H7z" />
            <path d="M14 3v5h5M10 13h6M10 17h6" />
          </svg>
        </span>
        <span className="citation-card-label">{sourcesLabel(citations.length)}</span>
        <span className="citation-card-source">
          <span className="citation-card-title">{summary.title}</span>
          <span aria-hidden="true"> · </span>
          <span className="citation-card-subtitle">{summary.subtitle}</span>
        </span>
        <span className="evidence-toggle-chevron" aria-hidden="true">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" focusable="false">
            <path d="m6 9 6 6 6-6" />
          </svg>
        </span>
      </button>

      <div id={bodyId} className="evidence-body" hidden={!open}>
        {groups.contracts.length > 0 && (
          <section className="evidence-section" data-section="contracts">
            <h4 className="evidence-section-title">Your contracts</h4>
            {groups.contracts.map(renderContractGroup)}
          </section>
        )}

        {groups.market.length > 0 && (
          <section className="evidence-section" data-section="market">
            <h4 className="evidence-section-title">Market · representative</h4>
            <ul className="evidence-rows">
              {groups.market.map((citation) => renderRow(citation, citation.title, true))}
            </ul>
          </section>
        )}

        {groups.raffa.length > 0 && (
          <section className="evidence-section" data-section="raffa">
            <h4 className="evidence-section-title">Raffa</h4>
            <ul className="evidence-rows">
              {groups.raffa.map((citation) => renderRow(citation, citation.title, Boolean(citation.href)))}
            </ul>
          </section>
        )}

        {groups.web.length > 0 && (
          <section className="evidence-section" data-section="web">
            <h4 className="evidence-section-title">Web · unverified</h4>
            <p className="evidence-section-note micro-meta">Public sources Raffa read with your permission. Not checked against your contracts.</p>
            <ul className="evidence-rows">
              {groups.web.map((citation) => renderRow(citation, citation.title, Boolean(citation.href)))}
            </ul>
          </section>
        )}
      </div>

      {footerActions.length > 0 && (
        <div className="citation-card-footer">
          <ActionRow actions={footerActions} />
        </div>
      )}
    </div>
  );
}
