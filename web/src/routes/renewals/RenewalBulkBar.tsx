import { RenewalActionLink } from "./RenewalActionLauncher";
import type { RenewalBulkActionView } from "./renewalActions";

export interface RenewalBulkBarProps {
  selectedCount: number;
  actions: readonly RenewalBulkActionView[];
  /** A `write` bulk action (today: "Assign to me") -- the route runs it row by row. */
  onWrite: (action: RenewalBulkActionView) => void;
  onClear: () => void;
  /** "Assigning 2 of 3…" while a write runs; `null` otherwise. */
  progress: string | null;
  error: string | null;
}

/**
 * Shown while rows are ticked in the list: what can be done to all of them at once, from the bulk
 * half of the action registry (`renewalActions.ts#RENEWAL_BULK_ACTIONS`). Links and Ask launches
 * navigate; a write runs through the route, one real `POST /api/renewals/{id}/action` per row.
 */
export default function RenewalBulkBar({ selectedCount, actions, onWrite, onClear, progress, error }: RenewalBulkBarProps) {
  return (
    <div className="renewal-bulk" role="region" aria-label="Selected renewals">
      <strong className="renewal-bulk-count">{selectedCount} selected</strong>
      <div className="renewal-bulk-actions">
        {actions.map((action) =>
          action.target.kind === "write" ? (
            <button key={action.id} type="button" className="btn btn-secondary renewal-bulk-button" disabled={progress !== null} onClick={() => onWrite(action)}>
              {action.label}
            </button>
          ) : (
            <RenewalActionLink key={action.id} target={action.target} className="btn btn-ghost renewal-bulk-button">
              {action.label} →
            </RenewalActionLink>
          ),
        )}
        <button type="button" className="btn btn-ghost renewal-bulk-button" onClick={onClear} disabled={progress !== null}>
          Clear selection
        </button>
      </div>
      {progress !== null && (
        <span className="micro-meta" role="status">
          {progress}
        </span>
      )}
      {error !== null && (
        <span className="hint" role="alert">
          {error}
        </span>
      )}
    </div>
  );
}
