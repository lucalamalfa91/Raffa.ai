import { useEffect, useState, type FormEvent } from "react";
import { CONTRACT_TYPE_OPTIONS, type CorrectableFieldName, type ReviewFieldRow } from "./reviewViewModel";
import { getContractTypeLabel } from "../portfolioTableFormatters";

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
 * Two honest departures from the cited prototype's own mock evidence pane, both already named in
 * `./reviewViewModel.ts`'s own header comment (never re-explained here, only applied): there is no
 * highlighted source passage (no endpoint exposes a per-field source span/page for this field set
 * yet -- shown as a plain "not yet available" line, not a fabricated citation), and no
 * model/prompt version (`ExtractionEvidence.ExtractionJobId` is the real trail for that, also
 * unexposed). What this pane *can* show for real is the field's own correction history -- the
 * genuine "version" trail `GET /api/contracts/{id}/corrections` already provides -- which doubles
 * as this AC's "version" element with real data instead of an invented one.
 */
export default function EvidencePane({ row, onCorrect, submitting, error }: EvidencePaneProps) {
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
        <p className="micro-meta">Select a field to see its evidence and correction form.</p>
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
      <p className="micro-meta">Extracted value: {row.displayValue}</p>
      <p className="micro-meta">Source: not yet available for this field (open backend gap -- see reviewViewModel.ts).</p>

      <form onSubmit={handleSubmit} className="review-correction-form">
        <div className="field">
          <label htmlFor={VALUE_INPUT_ID}>Correct value</label>
          {renderValueInput(row, draftValue, setDraftValue)}
        </div>
        <div className="field">
          <label htmlFor={REASON_INPUT_ID}>Reason (optional)</label>
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
          {submitting ? "Saving…" : "Save correction"}
        </button>
      </form>

      <h6>Correction history</h6>
      {row.latestCorrection === null ? (
        <p className="micro-meta">Not yet corrected — this is the original extracted value.</p>
      ) : (
        <ul className="review-history-list">
          <li>
            <div>
              {row.latestCorrection.previousValue ?? "—"} → {row.latestCorrection.newValue ?? "—"}
            </div>
            <div className="micro-meta">
              {formatTimestamp(row.latestCorrection.correctedAt)} · {row.latestCorrection.reason ?? "No reason given"}
            </div>
          </li>
        </ul>
      )}
    </aside>
  );
}

/** `yyyy-MM-ddTHH:mm:ss...` -> `yyyy-MM-dd HH:mm`, a plain, locale-independent (and therefore
 * unit-test-stable) rendering -- ContractCorrectionService.CorrectAsync always writes `IClock
 * .UtcNow`, so every value here is already UTC. */
function formatTimestamp(iso: string): string {
  return iso.replace("T", " ").slice(0, 16);
}

function renderValueInput(row: ReviewFieldRow, value: string, setValue: (next: string) => void) {
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
          <option value="true">Yes</option>
          <option value="false">No</option>
        </select>
      );
    case "enum":
      return (
        <select id={VALUE_INPUT_ID} className="input" value={value} onChange={(event) => setValue(event.target.value)}>
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
          id={VALUE_INPUT_ID}
          className="input"
          type="text"
          value={value}
          onChange={(event) => setValue(event.target.value)}
        />
      );
  }
}
