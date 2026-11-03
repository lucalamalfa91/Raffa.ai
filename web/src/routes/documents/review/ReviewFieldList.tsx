import { useEffect, useState, type KeyboardEvent } from "react";
import { getContractTypeLabel } from "../../contracts/portfolioTableFormatters";
import { CopyTip } from "../../../components/InfoTip";
import { buildReviewConfidenceTip, TIPS } from "../../../components/infoTipCopy";
import { CONTRACT_TYPE_OPTIONS, fieldTag, isFieldBlocking, legendThresholdPct, type CorrectableFieldName, type ReviewFieldRow } from "./reviewViewModel";
import { useI18n } from "../../../i18n/I18nProvider";
import type { Translate } from "../../../i18n/translate";

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
  const { t } = useI18n();
  const recovered = rows.filter((row) => !row.missing);
  const missing = rows.filter((row) => row.missing);
  const pendingCount = recovered.filter((row) => row.decision === "pending" && row.rawValue !== "").length;
  const thresholdPct = autoAcceptThreshold === null ? null : legendThresholdPct(autoAcceptThreshold);
  const confidenceTip = buildReviewConfidenceTip(thresholdPct);
  // The tip's threshold line carries a number, so it is translated here with its placeholder;
  // `CopyTip` then shows it as is (its own `t()` finds no id for an already-translated line).
  const thresholdLine = thresholdPct === null ? null : `At ${thresholdPct}% or more a value is accepted automatically; below, you decide.`;
  const localizedConfidenceTip = {
    ...confidenceTip,
    lines: confidenceTip.lines.map((line) =>
      line === thresholdLine ? t("At {pct}% or more a value is accepted automatically; below, you decide.", { pct: thresholdPct }) : line,
    ),
  };

  return (
    <div className="review-field-column">
      {onAcceptAll && pendingCount > 0 && (
        <div className="review-accept-all-bar">
          <button type="button" className="btn btn-secondary" disabled={submitting} onClick={onAcceptAll}>
            {t("Accept all ({n})", { n: pendingCount })}
          </button>
        </div>
      )}
      <table className="table review-field-table">
        <thead>
          <tr>
            <th>{t("Field")}</th>
            <th>{t("Extracted value")}</th>
            <th>
              {t("Confidence")}
              <CopyTip tip={localizedConfidenceTip} />
            </th>
            <th>
              {t("Decision")}
              <CopyTip tip={TIPS.reviewDecision} align="end" />
            </th>
          </tr>
        </thead>
        <tbody>
          {recovered.map((row) => {
            const tag = fieldTag(row, t);
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
                  <div className="micro-meta review-field-source">{describeSource(row, t)}</div>
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
                        {t("Accept")}
                      </button>
                      <button type="button" className="btn btn-ghost" disabled={submitting} onClick={() => onSelect(row.name)}>
                        {t("Correct")}
                      </button>
                    </div>
                  ) : row.decision === "accepted" ? (
                    <div className="review-decision-actions">
                      {row.evidenceDecision === "auto_accepted" ? (
                        <span className="micro-meta review-decision-result">{t("Accepted automatically")}</span>
                      ) : (
                        <span className="micro-meta review-decision-result">{t("Accepted by you")}</span>
                      )}
                      <button type="button" className="btn btn-ghost" onClick={() => onSelect(row.name)}>
                        {t("Correct")}
                      </button>
                    </div>
                  ) : (
                    <span className="micro-meta">{t("Corrected → {value}", { value: row.latestCorrection?.newValue ?? "—" })}</span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {missing.length > 0 ? (
        <section className="review-missing-fields" aria-labelledby="review-missing-heading">
          <h3 id="review-missing-heading">{t("Not found in the document")}</h3>
          <p className="micro-meta">
            {t("Raffa could not find these in the file. Type the value if you have it — it is saved as your correction.")}
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
function describeSource(row: ReviewFieldRow, t: Translate): string {
  const source = describeEvidenceSource(row, t);
  return row.proposalPending ? t("Proposed, not yet applied · {source}", { source }) : source;
}

function describeEvidenceSource(row: ReviewFieldRow, t: Translate): string {
  const { evidence } = row;
  if (evidence === null) return t("No source recorded");

  const span = evidence.sourceSpan === null ? null : truncate(evidence.sourceSpan.replace(/\s+/g, " ").trim(), MAX_SPAN_PREVIEW);
  if (evidence.sourcePage !== null && span !== null) return t("p. {page} · “{span}”", { page: evidence.sourcePage, span });
  if (span !== null) return `“${span}”`;
  if (evidence.sourcePage !== null) return t("p. {page}", { page: evidence.sourcePage });
  return t("From the whole document");
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
  const { t } = useI18n();
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
      {renderMissingInput(row, inputId, draft, setDraft, submitting, commit, onKeyDown, t)}
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
  t: Translate,
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
          <option value="">{t("Select…")}</option>
          <option value="true">{t("Yes")}</option>
          <option value="false">{t("No")}</option>
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
          <option value="">{t("Select…")}</option>
          {CONTRACT_TYPE_OPTIONS.map((option) => (
            <option key={option} value={option}>
              {t(getContractTypeLabel(option))}
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
