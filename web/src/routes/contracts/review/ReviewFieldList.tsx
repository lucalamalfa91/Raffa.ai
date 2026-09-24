import { useEffect, useState, type KeyboardEvent } from "react";
import { getContractTypeLabel } from "../portfolioTableFormatters";
import { CopyTip } from "../../../components/InfoTip";
import { buildReviewConfidenceTip, TIPS } from "../../../components/infoTipCopy";
import { CONTRACT_TYPE_OPTIONS, fieldTag, isFieldBlocking, legendThresholdPct, type CorrectableFieldName, type ReviewFieldRow } from "./reviewViewModel";

export interface ReviewFieldListProps {
  rows: readonly ReviewFieldRow[];
  selectedField: CorrectableFieldName | null;
  onSelect: (name: CorrectableFieldName) => void;
  onAccept: (name: CorrectableFieldName) => void;
  /** Accepts every pending field as extracted in one write; the button is hidden when omitted. */
  onAcceptAll?: () => void;
  /** Same correction write the evidence pane uses (`PATCH /api/contracts/{id}` then in-place merge). */
  onCorrect: (name: CorrectableFieldName, value: string | null, reason: string) => void;
  submitting?: boolean;
  /** Shown in the unrecovered section when the last fill failed and no recovered row is selected. */
  error?: string | null;
  /** The response's auto-accept bar (0..1), named in the Confidence header's tip; `null` until known. */
  autoAcceptThreshold?: number | null;
}

/**
 * The 4-column review list (screens.md #6 AC-1: "Field (critical marker) · Extracted value + source
 * · Confidence tag · Decision (Accept / Correct or result)"; task E07/F03/US01/T01). Column 2 folds
 * "value" and "source" into one cell (the AC itself names them as one column); the source line is
 * the field's real evidence -- page and quoted span from `GET /api/contracts/{id}/evidence` -- or an
 * honest "no source recorded" when the extraction reported none, never a fabricated citation.
 *
 * Every recovered row is reachable by keyboard through its own field-name button (`.btn.btn-ghost`,
 * the same reusable primitive `../contract360/DetailSections.tsx`'s "Review all →" link already
 * uses) -- row `onClick` would not be, so this is the real interactive surface, not a mouse-only
 * convenience on top of it.
 *
 * Task E11/F07/US01/T01 (gap G-REV): "Correct" is `.btn.btn-ghost` (was `.btn-primary`) and the
 * value cell carries `.review-field-value` (ellipsis truncation) -- both copied from the export's
 * own inline styling on this exact row; column widths live in `./review.css`.
 *
 * Unrecovered canonical fields (NW-64) sit in a fourth row state after the table, as label + empty
 * `.input` from the locked catalogue -- never a confidence tag, never inside the evidence pane.
 */
export default function ReviewFieldList({
  rows,
  selectedField,
  onSelect,
  onAccept,
  onAcceptAll,
  onCorrect,
  submitting = false,
  error = null,
  autoAcceptThreshold = null,
}: ReviewFieldListProps) {
  const recovered = rows.filter((row) => !row.missing);
  const missing = rows.filter((row) => row.missing);
  const pendingCount = recovered.filter((row) => row.decision === "pending" && row.rawValue !== "").length;

  return (
    <div className="review-field-column">
      <table className="table review-field-table">
        <thead>
          <tr>
            <th>Field</th>
            <th>Extracted value</th>
            <th>
              Confidence
              <CopyTip tip={buildReviewConfidenceTip(autoAcceptThreshold === null ? null : legendThresholdPct(autoAcceptThreshold))} />
            </th>
            <th>
              Decision
              <CopyTip tip={TIPS.reviewDecision} align="end" />
              {onAcceptAll && pendingCount > 0 && (
                <button
                  type="button"
                  className="btn btn-secondary review-accept-all"
                  disabled={submitting}
                  onClick={onAcceptAll}
                >
                  Accept all ({pendingCount})
                </button>
              )}
            </th>
          </tr>
        </thead>
        <tbody>
          {recovered.map((row) => {
            const tag = fieldTag(row);
            const critical = isFieldBlocking(row);
            const classNames = [critical ? "row-critical" : null, row.name === selectedField ? "review-row-selected" : null]
              .filter((value): value is string => value !== null)
              .join(" ");

            return (
              <tr key={row.name} className={classNames === "" ? undefined : classNames}>
                <td>
                  <button type="button" className="btn btn-ghost review-field-name" onClick={() => onSelect(row.name)}>
                    {critical && <span className="review-critical-marker" aria-hidden="true" />}
                    {row.label}
                  </button>
                </td>
                <td>
                  <div className="review-field-value">{row.displayValue}</div>
                  <div className="micro-meta review-field-source">{describeSource(row)}</div>
                </td>
                <td>
                  <span className={`tag tag-${tag.variant}`}>{tag.label}</span>
                </td>
                <td>
                  {row.decision === "pending" ? (
                    <div className="review-decision-actions">
                      <button
                        type="button"
                        className="btn btn-secondary"
                        disabled={submitting}
                        onClick={() => onAccept(row.name)}
                      >
                        Accept
                      </button>
                      <button type="button" className="btn btn-ghost" disabled={submitting} onClick={() => onSelect(row.name)}>
                        Correct
                      </button>
                    </div>
                  ) : row.decision === "accepted" ? (
                    <div className="review-decision-actions">
                      {row.evidenceDecision === "auto_accepted" ? (
                        <span className="micro-meta review-decision-result">Accepted automatically</span>
                      ) : (
                        <span className="micro-meta review-decision-result">Accepted by you</span>
                      )}
                      <button type="button" className="btn btn-ghost" onClick={() => onSelect(row.name)}>
                        Correct
                      </button>
                    </div>
                  ) : (
                    <span className="micro-meta">Corrected → {row.latestCorrection?.newValue ?? "—"}</span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {missing.length > 0 ? (
        <section className="review-missing-fields" aria-labelledby="review-missing-heading">
          <h3 id="review-missing-heading">Not found in the document</h3>
          <p className="micro-meta">
            Raffa could not find these in the file. Type the value if you have it — it is saved as your correction.
          </p>
          {error !== null && selectedField === null ? (
            <p className="hint" role="alert">
              {error}
            </p>
          ) : null}
          {missing.map((row) => (
            <MissingFieldRow key={row.name} row={row} onCorrect={onCorrect} submitting={submitting} />
          ))}
        </section>
      ) : null}
    </div>
  );
}

const MAX_SPAN_PREVIEW = 60;

/** One line of provenance under the value: where the extraction read it, in the row's own words. */
function describeSource(row: ReviewFieldRow): string {
  const prefix = row.proposalPending ? "Proposed, not yet applied · " : "";
  const { evidence } = row;
  if (evidence === null) return `${prefix}No source recorded`;

  const span = evidence.sourceSpan === null ? null : truncate(evidence.sourceSpan.replace(/\s+/g, " ").trim(), MAX_SPAN_PREVIEW);
  if (evidence.sourcePage !== null && span !== null) return `${prefix}p. ${evidence.sourcePage} · “${span}”`;
  if (span !== null) return `${prefix}“${span}”`;
  if (evidence.sourcePage !== null) return `${prefix}p. ${evidence.sourcePage}`;
  return `${prefix}From the whole document`;
}

function truncate(text: string, max: number): string {
  return text.length <= max ? text : `${text.slice(0, max - 1)}…`;
}

function MissingFieldRow({
  row,
  onCorrect,
  submitting,
}: {
  row: ReviewFieldRow;
  onCorrect: ReviewFieldListProps["onCorrect"];
  submitting: boolean;
}) {
  const [draft, setDraft] = useState(row.rawValue);
  const inputId = `review-missing-${row.name}`;

  useEffect(() => {
    setDraft(row.rawValue);
  }, [row.rawValue]);

  const commit = () => {
    const trimmed = row.kind === "bool" || row.kind === "enum" ? draft : draft.trim();
    if (trimmed === "" || trimmed === row.rawValue) return;
    onCorrect(row.name, trimmed, "");
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement | HTMLSelectElement>) => {
    if (event.key === "Enter") {
      event.preventDefault();
      commit();
    }
  };

  return (
    <div className="field">
      <label htmlFor={inputId}>{row.label}</label>
      {renderMissingInput(row, inputId, draft, setDraft, submitting, commit, onKeyDown)}
    </div>
  );
}

function renderMissingInput(
  row: ReviewFieldRow,
  inputId: string,
  value: string,
  setValue: (next: string) => void,
  submitting: boolean,
  commit: () => void,
  onKeyDown: (event: KeyboardEvent<HTMLInputElement | HTMLSelectElement>) => void,
) {
  switch (row.kind) {
    case "date":
      return (
        <input
          id={inputId}
          className="input"
          type="date"
          value={value}
          disabled={submitting}
          onChange={(event) => setValue(event.target.value)}
          onBlur={commit}
          onKeyDown={onKeyDown}
        />
      );
    case "decimal":
      return (
        <input
          id={inputId}
          className="input"
          type="number"
          step="0.01"
          value={value}
          disabled={submitting}
          onChange={(event) => setValue(event.target.value)}
          onBlur={commit}
          onKeyDown={onKeyDown}
        />
      );
    case "int":
      return (
        <input
          id={inputId}
          className="input"
          type="number"
          step="1"
          value={value}
          disabled={submitting}
          onChange={(event) => setValue(event.target.value)}
          onBlur={commit}
          onKeyDown={onKeyDown}
        />
      );
    case "bool":
      return (
        <select
          id={inputId}
          className="input"
          value={value}
          disabled={submitting}
          onChange={(event) => setValue(event.target.value)}
          onBlur={commit}
          onKeyDown={onKeyDown}
        >
          <option value="">Select…</option>
          <option value="true">Yes</option>
          <option value="false">No</option>
        </select>
      );
    case "enum":
      return (
        <select
          id={inputId}
          className="input"
          value={value}
          disabled={submitting}
          onChange={(event) => setValue(event.target.value)}
          onBlur={commit}
          onKeyDown={onKeyDown}
        >
          <option value="">Select…</option>
          {CONTRACT_TYPE_OPTIONS.map((option) => (
            <option key={option} value={option}>
              {getContractTypeLabel(option)}
            </option>
          ))}
        </select>
      );
    default:
      return (
        <input
          id={inputId}
          className="input"
          type="text"
          value={value}
          disabled={submitting}
          onChange={(event) => setValue(event.target.value)}
          onBlur={commit}
          onKeyDown={onKeyDown}
        />
      );
  }
}
