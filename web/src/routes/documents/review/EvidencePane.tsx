import { useEffect, useState, type FormEvent } from "react";
import type { ContractFieldEvidenceBody } from "../../../api/client";
import { ACCEPTED_AS_EXTRACTED_REASON, CONTRACT_TYPE_OPTIONS, splitPassage, type CorrectableFieldName, type ReviewFieldRow } from "./reviewViewModel";
import { getContractTypeLabel } from "../../contracts/portfolioTableFormatters";
import { DocumentViewerLink } from "../viewer/DocumentViewerOverlay";
import { buildDocumentViewerHref } from "../viewer/documentViewerViewModel";
import { useI18n } from "../../../i18n/I18nProvider";
import type { Translate } from "../../../i18n/translate";

export interface EvidencePaneProps {
  /** `null` before any row has been selected (initial state). */
  row: ReviewFieldRow | null;
  onCorrect: (name: CorrectableFieldName, newValue: string | null, reason: string) => void;
  submitting: boolean;
  error: string | null;
}

const VALUE_INPUT_ID = "review-correction-value";
const REASON_INPUT_ID = "review-correction-reason";

/**
 * Right-hand evidence + correction pane (screens.md #6 AC-3: "Right pane: evidence page with
 * highlighted passage, correction form, model/prompt version"; task E07/F03/US01/T01). Reuses the
 * locked `.detail-pane` component (ADR-019 catalogue: "340-400px, surface, 2px left rule").
 *
 * The evidence card is real now: `GET /api/contracts/{id}/evidence` carries, per field, the source
 * file and page, the span the model quoted, the passage of page text around it and the model id
 * (`ReviewFieldRow.evidence`). The card renders exactly that -- file · page as the header, the
 * passage with the span highlighted, the model and confidence underneath -- and degrades honestly
 * when a piece is missing: a span whose page text could not be located shows the quote alone, a
 * classification verdict (no span, it reads the whole document) says so, and a field with no
 * evidence row at all says no source was recorded. Nothing here is ever fabricated to fill the
 * card (Appendix C rule 10). The correction history below it is the same real trail
 * `GET /api/contracts/{id}/corrections` already provided.
 */
export default function EvidencePane({ row, onCorrect, submitting, error }: EvidencePaneProps) {
  const { t } = useI18n();
  const [draftValue, setDraftValue] = useState("");
  const [reason, setReason] = useState("");

  // Re-seeds the form whenever a different field is selected, or this same field's own current
  // value/decision changes (e.g. a correction just saved successfully) -- never on every re-render,
  // so mid-edit keystrokes are not clobbered by an unrelated parent re-render.
  useEffect(() => {
    setDraftValue(row?.rawValue ?? "");
    setReason("");
  }, [row?.name, row?.rawValue, row?.decision]);

  if (row === null) {
    return (
      <aside className="detail-pane review-evidence-pane">
        <p className="micro-meta">{t("Select a field to see its evidence and correction form.")}</p>
      </aside>
    );
  }

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = row.kind === "bool" || row.kind === "enum" ? draftValue : draftValue.trim();
    // A blank submission on an optional field clears it (sent as JSON null, ContractCorrectionService's
    // own "clear an optional field" contract); a required field is never blank-able, so its own empty
    // string is sent through as-is and the backend's real "cannot be cleared" 400 surfaces honestly.
    const value = trimmed === "" && !row.required ? null : trimmed;
    onCorrect(row.name, value, reason);
  };

  return (
    <aside className="detail-pane review-evidence-pane">
      <h6>{row.label}</h6>
      <p className="micro-meta">
        {row.proposalPending
          ? t("Proposed value: {value} — not applied until you accept or correct it", { value: row.displayValue })
          : t("Extracted value: {value}", { value: row.displayValue })}
      </p>
      <EvidenceCard evidence={row.evidence} />

      <form onSubmit={handleSubmit} className="review-correction-form">
        <div className="field">
          <label htmlFor={VALUE_INPUT_ID}>{t("Correct value")}</label>
          {renderValueInput(row, draftValue, setDraftValue, t)}
        </div>
        <div className="field">
          <label htmlFor={REASON_INPUT_ID}>{t("Reason (optional)")}</label>
          <input
            id={REASON_INPUT_ID}
            className="input"
            type="text"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
        </div>
        {error !== null && (
          <p className="hint" role="alert">
            {error}
          </p>
        )}
        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? t("Saving…") : t("Save correction")}
        </button>
      </form>

      <h6>{t("Correction history")}</h6>
      {row.latestCorrection === null ? (
        <p className="micro-meta">{t("Not yet corrected — this is the original extracted value.")}</p>
      ) : (
        <ul className="review-history-list">
          <li>
            <div>
              {row.latestCorrection.previousValue ?? "—"} → {row.latestCorrection.newValue ?? "—"}
            </div>
            <div className="micro-meta">
              {formatTimestamp(row.latestCorrection.correctedAt)} · {correctionReason(row.latestCorrection.reason, t)}
            </div>
          </li>
        </ul>
      )}
    </aside>
  );
}

/** The reason a correction was written with: a person's own words as typed, the "Accept" reason
 * (stored in English) in the screen's language. */
function correctionReason(reason: string | null, t: Translate): string {
  if (reason === null) return t("No reason given");
  return reason === ACCEPTED_AS_EXTRACTED_REASON ? t(ACCEPTED_AS_EXTRACTED_REASON) : reason;
}

/** The "highlighted passage" card (screens.md #6): file · page header, the page text around the
 * span with the span marked, model + confidence underneath. Every line comes from the evidence row;
 * a missing piece is stated, never filled in. */
function EvidenceCard({ evidence }: { evidence: ContractFieldEvidenceBody | null }) {
  const { t } = useI18n();
  if (evidence === null) {
    return (
      <div className="review-evidence-passage">
        <p className="micro-meta">{t("No source passage was recorded for this field — the extraction did not report it.")}</p>
      </div>
    );
  }

  const header = [
    evidence.sourceFileName?.toUpperCase() ?? t("SOURCE DOCUMENT"),
    evidence.sourcePage !== null ? t("PAGE {n}", { n: evidence.sourcePage }) : evidence.sourceSpan === null ? t("WHOLE DOCUMENT") : null,
  ]
    .filter((part): part is string => part !== null)
    .join(" · ");

  const split = splitPassage(evidence);
  const confidence = evidence.confidence === null ? null : `${Math.round(evidence.confidence * 100)}%`;
  const meta = [
    evidence.modelId !== null ? t("Extracted by {model}", { model: evidence.modelId }) : t("Extracted"),
    confidence !== null ? t("confidence {pct}", { pct: confidence }) : null,
  ]
    .filter((part): part is string => part !== null)
    .join(" · ");

  return (
    <div className="review-evidence-passage">
      <p className="review-evidence-source">{header}</p>
      {split !== null ? (
        <p className="review-evidence-text">
          {split.before}
          {split.highlight !== "" && <mark className="review-evidence-highlight">{split.highlight}</mark>}
          {split.after}
        </p>
      ) : evidence.sourceSpan !== null ? (
        <p className="review-evidence-text">
          <mark className="review-evidence-highlight">{evidence.sourceSpan}</mark>
        </p>
      ) : (
        <p className="review-evidence-text">
          {evidence.value !== null
            ? t("Read from the full document text — proposed “{value}”.", { value: evidence.value })
            : t("Read from the full document text.")}
        </p>
      )}
      <p className="micro-meta review-evidence-meta">{meta}</p>
      {evidence.sourceDocumentId !== null && (
        <p>
          <DocumentViewerLink
            to={buildDocumentViewerHref(evidence.sourceDocumentId, evidence.sourcePage)}
            className="btn btn-ghost"
          >
            {t("Open in document viewer")}
          </DocumentViewerLink>
        </p>
      )}
    </div>
  );
}

/** `yyyy-MM-ddTHH:mm:ss...` -> `yyyy-MM-dd HH:mm`, a plain, locale-independent (and therefore
 * unit-test-stable) rendering -- ContractCorrectionService.CorrectAsync always writes `IClock
 * .UtcNow`, so every value here is already UTC. */
function formatTimestamp(iso: string): string {
  return iso.replace("T", " ").slice(0, 16);
}

function renderValueInput(row: ReviewFieldRow, value: string, setValue: (next: string) => void, t: Translate) {
  switch (row.kind) {
    case "date":
      return (
        <input
          id={VALUE_INPUT_ID}
          className="input"
          type="date"
          value={value}
          onChange={(event) => setValue(event.target.value)}
        />
      );
    case "decimal":
      return (
        <input
          id={VALUE_INPUT_ID}
          className="input"
          type="number"
          step="0.01"
          value={value}
          onChange={(event) => setValue(event.target.value)}
        />
      );
    case "int":
      return (
        <input
          id={VALUE_INPUT_ID}
          className="input"
          type="number"
          step="1"
          value={value}
          onChange={(event) => setValue(event.target.value)}
        />
      );
    case "bool":
      return (
        <select id={VALUE_INPUT_ID} className="input" value={value} onChange={(event) => setValue(event.target.value)}>
          <option value="true">{t("Yes")}</option>
          <option value="false">{t("No")}</option>
        </select>
      );
    case "enum":
      return (
        <select id={VALUE_INPUT_ID} className="input" value={value} onChange={(event) => setValue(event.target.value)}>
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
          id={VALUE_INPUT_ID}
          className="input"
          type="text"
          value={value}
          onChange={(event) => setValue(event.target.value)}
        />
      );
  }
}
