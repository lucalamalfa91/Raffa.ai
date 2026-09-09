import { fieldTag, isFieldBlocking, type CorrectableFieldName, type ReviewFieldRow } from "./reviewViewModel";

export interface ReviewFieldListProps {
  rows: readonly ReviewFieldRow[];
  selectedField: CorrectableFieldName | null;
  onSelect: (name: CorrectableFieldName) => void;
  onAccept: (name: CorrectableFieldName) => void;
}

/**
 * The 4-column review list (screens.md #6 AC-1: "Field (critical marker) · Extracted value + source
 * · Confidence tag · Decision (Accept / Correct or result)"; task E07/F03/US01/T01). Column 2 folds
 * "value" and "source" into one cell (the AC itself names them as one column); the source line is
 * the field's real evidence -- page and quoted span from `GET /api/contracts/{id}/evidence` -- or an
 * honest "no source recorded" when the extraction reported none, never a fabricated citation.
 *
 * Every row is reachable by keyboard through its own field-name button (`.btn.btn-ghost`, the same
 * reusable primitive `../contract360/DetailsSection.tsx`'s "Review all →" link already uses) -- row
 * `onClick` would not be, so this is the real interactive surface, not a mouse-only convenience on
 * top of it.
 *
 * Task E11/F07/US01/T01 (gap G-REV): "Correct" is `.btn.btn-ghost` (was `.btn-primary`) and the
 * value cell carries `.review-field-value` (ellipsis truncation) -- both copied from the export's
 * own inline styling on this exact row; column widths live in `./review.css`.
 */
export default function ReviewFieldList({ rows, selectedField, onSelect, onAccept }: ReviewFieldListProps) {
  return (
    <table className="table review-field-table">
      <thead>
        <tr>
          <th>Field</th>
          <th>Extracted value</th>
          <th>Confidence</th>
          <th>Decision</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => {
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
                    <button type="button" className="btn btn-secondary" onClick={() => onAccept(row.name)}>
                      Accept
                    </button>
                    <button type="button" className="btn btn-ghost" onClick={() => onSelect(row.name)}>
                      Correct
                    </button>
                  </div>
                ) : row.decision === "accepted" ? (
                  <span className="micro-meta">Accepted by you</span>
                ) : (
                  <span className="micro-meta">Corrected → {row.latestCorrection?.newValue ?? "—"}</span>
                )}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
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
