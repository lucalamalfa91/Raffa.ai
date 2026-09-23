import { useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import type { Contract360Body, Contract360ClauseBody, Contract360DocumentBody, ContractFieldEvidenceBody, ContractStrategyBody, RenewalPriorityBody } from "../../../api/client";
import { DocumentViewerLink } from "../../documents/viewer/DocumentViewerOverlay";
import ClauseHighlight from "./ClauseHighlight";
import { formatCompactAmount } from "../portfolioViewModel";
import {
  AUTO_ACCEPT_THRESHOLD,
  LEVERS_NOT_YET_AVAILABLE,
  SECTION_COPY,
  STANDARD_TERMS_HINT,
  buildClauseGroups,
  buildDocumentRows,
  buildKeyTerms,
  buildLeverCards,
  buildProductNote,
  buildProductSavingTotal,
  buildObligationColumns,
  buildProductLines,
  buildRiskItems,
  buildScoreParts,
  computeNeedsAttention,
  formatReviewCountLine,
  standardClausesLabel,
  type ClauseItem,
  type ObligationItem,
  type SectionCopy,
} from "./contract360ViewModel";

/**
 * The six numbered sections under the answers band (`Raffa.ai V2.dc.html` "CONTRACT 360 — three
 * answers, then proof, then details"), each the same frame:
 *
 *   <div data-screen-label="Contract 360 · …" style="display:flex;flex-wrap:wrap;gap:16px 40px;padding:30px 32px 34px;border-top:2px solid var(--color-divider)">
 *     <div style="flex:0 1 200px;min-width:150px">
 *       <div style="font-family:heading;font-weight:800;font-size:12px;color:accent;letter-spacing:.06em">01</div>
 *       <h5 style="margin:2px 0 6px">Leverage</h5>
 *       <p style="font-size:12px;color:neutral-600;margin:0;text-wrap:pretty;max-width:26ch">…</p>
 *     <div style="flex:1 1 520px;min-width:0">…content…</div>
 *
 * Every figure inside the content slots is quoted in `contract360.css`'s own section comments.
 * Sections never collapse (the V2 "Details ▾" drawer is gone); a section with nothing on the wire
 * says so in one line rather than disappearing, so the page keeps the same six-part shape for
 * every contract.
 */

function SectionFrame({ copy, label, children }: { copy: SectionCopy; label: string; children: ReactNode }) {
  return (
    <section className="contract360-section" aria-label={label}>
      <div className="contract360-section-intro">
        <div className="contract360-section-number">{copy.number}</div>
        <h5 className="contract360-section-title">{copy.title}</h5>
        <p className="contract360-section-description">{copy.description}</p>
      </div>
      <div className="contract360-section-body">{children}</div>
    </section>
  );
}

// ---- 01 Leverage ------------------------------------------------------------------------------

export function LeverageSection({ strategy, lineDescriptions }: { strategy: ContractStrategyBody | null; lineDescriptions: readonly string[] }) {
  const cards = buildLeverCards(strategy, lineDescriptions);
  return (
    <SectionFrame copy={SECTION_COPY.leverage} label="Leverage">
      {cards.length === 0 ? (
        <p className="contract360-section-empty">{LEVERS_NOT_YET_AVAILABLE}</p>
      ) : (
        <div className="contract360-levers">
          {cards.map((lever) => (
            <div key={lever.key} className={`contract360-lever${lever.strong ? " is-strong" : ""}`}>
              <div className="contract360-lever-kicker">{lever.kicker}</div>
              <div className="contract360-lever-headline">{lever.headline}</div>
              <div className="contract360-lever-entries">
                {lever.entries.map((entry) => (
                  <div key={entry.body}>
                    {entry.lines.length > 0 && <div className="contract360-lever-entry-line">{entry.lines.join(" · ")}</div>}
                    <div className="contract360-lever-body">{entry.body}</div>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
    </SectionFrame>
  );
}

// ---- 02 Products & pricing ----------------------------------------------------------------------

export function ProductsSection({ contract, autoAcceptThreshold }: { contract: Contract360Body; autoAcceptThreshold: number }) {
  const currency = contract.tabs.commercials.currency;
  const lines = buildProductLines(contract.tabs.products, currency, autoAcceptThreshold);
  const total = contract.header.annualSpend === null ? "—" : formatCompactAmount(contract.header.annualSpend, currency);
  const saving = buildProductSavingTotal(contract.tabs.products, currency, autoAcceptThreshold);
  return (
    <SectionFrame copy={SECTION_COPY.products} label="Products & pricing">
      {lines.length === 0 ? (
        <p className="contract360-section-empty">No line items recorded for this contract.</p>
      ) : (
        <div className="contract360-products-scroll">
          <div className="contract360-products">
            <div className="contract360-products-head" role="row">
              <span>Product</span>
              <span className="is-right">Qty</span>
              <span className="is-right">You pay / unit</span>
              <span className="is-right">Market median / unit</span>
              <span>vs market</span>
              <span className="is-right">Could save / yr</span>
              <span className="is-right">Annual</span>
            </div>
            {lines.map((line) => (
              <div key={line.key} className="contract360-product" role="row">
                <div>
                  <div className="contract360-product-name">{line.name}</div>
                  {line.meta !== "" && <div className="contract360-product-meta">{line.meta}</div>}
                  {line.marketBasis !== null && <div className="contract360-product-basis">{line.marketBasis}</div>}
                </div>
                <div className="is-right">{line.qty}</div>
                <div className="is-right contract360-product-price">{line.price}</div>
                <div className="is-right contract360-product-market" title={line.marketTitle ?? undefined}>
                  <div>{line.market}</div>
                  {line.marketMeta !== "" && <div className="contract360-product-market-meta">{line.marketMeta}</div>}
                </div>
                <div className="contract360-product-delta">
                  <div className="contract360-product-bar" aria-hidden="true">
                    <div className="contract360-product-bar-market" style={{ width: line.marketWidth }} />
                    <div className={`contract360-product-bar-pay${line.deltaAccent ? " is-accent" : ""}`} style={{ width: line.payWidth }} />
                  </div>
                  <span className={`contract360-product-delta-value${line.deltaAccent ? " is-accent" : ""}`}>{line.delta}</span>
                </div>
                <div className={`is-right contract360-product-saving${line.savingAccent ? " is-accent" : ""}`}>{line.saving}</div>
                <div className="is-right contract360-product-annual">{line.annual}</div>
              </div>
            ))}
            <div className="contract360-products-foot">
              <span className="contract360-products-note">{buildProductNote(contract.tabs.products)}</span>
              {saving !== null && (
                <span className="contract360-products-total contract360-products-total-saving">
                  <span className="contract360-products-total-label">Could save / yr</span>
                  {saving}
                </span>
              )}
              <span className="contract360-products-total">
                <span className="contract360-products-total-label">Spend / yr</span>
                {total}
              </span>
            </div>
          </div>
        </div>
      )}
    </SectionFrame>
  );
}

// ---- 03 Clauses that matter ---------------------------------------------------------------------

function ClauseRow({
  item,
  selected,
  onSelect,
}: {
  item: ClauseItem;
  selected: boolean;
  onSelect: (clauseId: string) => void;
}) {
  return (
    <div role="listitem" className={`contract360-clause${selected ? " is-selected" : ""}`}>
      <button type="button" className="contract360-clause-select" aria-pressed={selected} onClick={() => onSelect(item.clauseId)}>
        <span className="contract360-clause-type">{item.type}</span>
        <span className="contract360-clause-normalized">{item.normalized}</span>
      </button>
      <span className={`contract360-clause-ask${item.ask === null ? " is-standard" : ""}`}>{item.ask ?? "—"}</span>
      <span className="contract360-clause-src" title={item.source ?? undefined}>
        {item.viewerHref !== null && item.source !== null ? (
          <DocumentViewerLink to={item.viewerHref} className="contract360-clause-viewer">
            {item.source}
          </DocumentViewerLink>
        ) : (
          (item.source ?? "")
        )}
      </span>
    </div>
  );
}

export function ClausesSection({
  contractId,
  clauses,
  documents,
  autoAcceptThreshold,
  selectedClauseId,
  onSelect,
}: {
  contractId: string;
  clauses: readonly Contract360ClauseBody[];
  documents: readonly Contract360DocumentBody[];
  autoAcceptThreshold: number;
  selectedClauseId: string | null;
  onSelect: (clauseId: string) => void;
}) {
  const [standardOpen, setStandardOpen] = useState(false);
  const threshold = autoAcceptThreshold > 0 ? autoAcceptThreshold : AUTO_ACCEPT_THRESHOLD;
  const { groups, standard } = buildClauseGroups(clauses, documents, threshold);
  const selected = clauses.find((c) => c.clauseId === selectedClauseId) ?? null;

  return (
    <SectionFrame copy={SECTION_COPY.clauses} label="Clauses that matter">
      {clauses.length === 0 ? (
        <p className="contract360-section-empty">
          No clauses extracted for this contract yet.{" "}
          <Link to={`/contracts/${contractId}/review`}>Review the extraction</Link> to see the wording behind these answers.
        </p>
      ) : (
        <div className="contract360-clause-groups">
          {groups.map((group) => (
            <div key={group.key} className="contract360-clause-group" role="list" aria-label={group.label}>
              <div className="contract360-clause-group-head">
                <span className={`tag tag-${group.tag} contract360-clause-tag`}>{group.label}</span>
                <span className="contract360-clause-group-hint">{group.hint}</span>
              </div>
              {group.items.map((item) => (
                <ClauseRow key={item.clauseId} item={item} selected={item.clauseId === selectedClauseId} onSelect={onSelect} />
              ))}
            </div>
          ))}
          {standard.length > 0 && (
            <div>
              <button type="button" className="btn btn-ghost contract360-standard-toggle" aria-expanded={standardOpen} onClick={() => setStandardOpen((open) => !open)}>
                {standardClausesLabel(standard.length, standardOpen)}
              </button>
              {standardOpen && (
                <div className="contract360-clause-group contract360-clause-group-standard" role="list" aria-label="Standard terms">
                  <div className="contract360-clause-group-head">
                    <span className="tag tag-outline contract360-clause-tag">Standard terms</span>
                    <span className="contract360-clause-group-hint">{STANDARD_TERMS_HINT}</span>
                  </div>
                  {standard.map((item) => (
                    <ClauseRow key={item.clauseId} item={item} selected={item.clauseId === selectedClauseId} onSelect={onSelect} />
                  ))}
                </div>
              )}
            </div>
          )}
          {selected !== null && <ClauseHighlight clause={selected} documents={documents} />}
        </div>
      )}
    </SectionFrame>
  );
}

// ---- 04 Obligations -----------------------------------------------------------------------------

function ObligationList({ heading, items, emptyText }: { heading: string; items: readonly ObligationItem[]; emptyText: string }) {
  return (
    <div className="contract360-obligations-column">
      <div className="contract360-obligations-head">{heading}</div>
      {items.length === 0 ? (
        <p className="contract360-section-empty">{emptyText}</p>
      ) : (
        items.map((item) => (
          <div key={item.key} className="contract360-obligation">
            <span className={`contract360-obligation-dot is-${item.dot}`} aria-hidden="true" />
            <div>
              <div className={`contract360-obligation-text${item.strong ? " is-strong" : ""}`}>{item.text}</div>
              <div className="contract360-obligation-when">{item.when}</div>
            </div>
            <span className="contract360-obligation-recurrence">{item.recurrence}</span>
          </div>
        ))
      )}
    </div>
  );
}

export function ObligationsSection({
  contract,
  supplierLabel,
  autoAcceptThreshold,
}: {
  contract: Contract360Body;
  supplierLabel: string;
  autoAcceptThreshold: number;
}) {
  const columns = buildObligationColumns(contract.tabs.obligations, autoAcceptThreshold);
  return (
    <SectionFrame copy={SECTION_COPY.obligations} label="Obligations">
      <div className="contract360-obligations">
        <ObligationList heading="You must" items={columns.you} emptyText="No obligation on you extracted for this contract." />
        <ObligationList heading={`${supplierLabel} must`} items={columns.supplier} emptyText="No supplier obligation extracted for this contract." />
      </div>
    </SectionFrame>
  );
}

// ---- 05 Risk factors ----------------------------------------------------------------------------

export function RiskSection({
  contract,
  priority,
  autoAcceptThreshold,
}: {
  contract: Contract360Body;
  priority: RenewalPriorityBody | null;
  autoAcceptThreshold: number;
}) {
  const parts = buildScoreParts(priority);
  const risks = buildRiskItems(contract.tabs.risks, autoAcceptThreshold);
  return (
    <SectionFrame copy={SECTION_COPY.risks} label="Risk factors">
      <div className="contract360-risk-grid">
        <div className="contract360-priority">
          <div>
            <div className="contract360-priority-label">Priority</div>
            <div className="contract360-priority-score">{priority === null ? "—" : Math.round(priority.totalScore)}</div>
            <div className="contract360-priority-of">of 100</div>
          </div>
          <div className="contract360-score-parts">
            {parts.length === 0 ? (
              <p className="contract360-section-empty">The priority score has not been computed for this contract yet.</p>
            ) : (
              parts.map((part) => (
                <div key={part.key} className="contract360-score-part">
                  <div className="contract360-score-part-row">
                    <span>{part.label}</span>
                    <span className="contract360-score-part-value">{part.value}</span>
                  </div>
                  <div className="contract360-score-bar" aria-hidden="true">
                    <div className={`contract360-score-bar-fill${part.accent ? " is-accent" : ""}`} style={{ width: part.width }} />
                  </div>
                </div>
              ))
            )}
          </div>
        </div>
        <div className="contract360-risks">
          {risks.length === 0 ? (
            <p className="contract360-section-empty">No risks recorded for this contract.</p>
          ) : (
            risks.map((risk) => (
              <div key={risk.key} className="contract360-risk">
                <span className={`tag tag-${risk.tag} contract360-risk-tag`}>{risk.level}</span>
                <div>
                  <div className="contract360-risk-title">{risk.title}</div>
                  <div className="contract360-risk-text">{risk.text}</div>
                </div>
              </div>
            ))
          )}
        </div>
      </div>
    </SectionFrame>
  );
}

// ---- 06 Key terms -------------------------------------------------------------------------------

export function KeyTermsSection({ contract, evidence }: { contract: Contract360Body; evidence: readonly ContractFieldEvidenceBody[] }) {
  const terms = buildKeyTerms(contract, evidence);
  const documents = buildDocumentRows(contract.tabs.documents);
  const reviewCount = computeNeedsAttention(evidence);
  return (
    <SectionFrame copy={SECTION_COPY.terms} label="Key terms">
      <div className="contract360-terms">
        {terms.map((row) => (
          <div key={row.key} className="contract360-term">
            <div className="contract360-term-label">{row.term}</div>
            <div className="contract360-term-value">{row.value}</div>
          </div>
        ))}
      </div>
      <div className="contract360-family">
        <span className="contract360-family-label">Documents</span>
        {documents.length === 0 ? (
          <span>No documents linked to this contract.</span>
        ) : (
          documents.map((row) => (
            <span key={row.documentId} className="contract360-family-doc">
              {row.type} · {row.fileName} <span className={`tag tag-${row.status.variant} contract360-family-tag`}>{row.status.label}</span>
            </span>
          ))
        )}
        {reviewCount > 0 && (
          <Link to={`/contracts/${contract.contractId}/review`} className="btn btn-ghost contract360-review-link">
            {formatReviewCountLine(reviewCount)}
          </Link>
        )}
      </div>
    </SectionFrame>
  );
}
