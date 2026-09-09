export interface UploadResultCardProps {
  fileName: string;
  /** The full requirements-copy sentence (`uploadPipeline.ts#getRejectionReasonCopy`/
   * `getOversizedCopy`) -- already includes the "Not added:" lead-in, quoted verbatim. */
  message: string;
  onDismiss: () => void;
}

/**
 * The "Not added" card (*req*, R-DOC-04; `contigo-v2/screens-v2.md` #3: "Not added card per
 * rejected file ... session-only, never counted"). V1's `UploadResultCard` rendered the single
 * outcome (needs_review/completed/failed) of the one file mid-upload at a time; V2 shows every real
 * document as its own row in `DocumentStatusTable.tsx` instead (screens-v2.md's own `docRows`), so
 * this component is repurposed to the one outcome that is genuinely *not* a row -- a rejected file
 * was never stored, has no id, and is never counted in `kbSummary`/the attention or all counts
 * (R-DOC-05 AC-1: "rejected files do not exist server-side"). Rendered above the row grid in
 * `DocumentStatusTable.tsx`, one per rejected file this session, dismissible (never persisted, so a
 * reload already clears it -- the dismiss button just lets a user clear the noise sooner).
 */
export default function UploadResultCard({ fileName, message, onDismiss }: UploadResultCardProps) {
  return (
    <div className="upload-result-card" role="status">
      <div className="upload-result-header">
        <span className="tag tag-outline">Not added</span>
        <span className="upload-result-filename">{fileName}</span>
        <button type="button" className="btn btn-ghost upload-result-dismiss" onClick={onDismiss} aria-label={`Dismiss "${fileName}" not added`}>
          Dismiss
        </button>
      </div>
      <p className="upload-result-message">{message}</p>
    </div>
  );
}
