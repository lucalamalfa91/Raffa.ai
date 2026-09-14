import { useEffect, useRef } from "react";
import { Link } from "react-router-dom";
import type { ApiClient, DocumentListItemBody } from "../../api/client";
import { CHECK_AGAIN_LABEL, UPDATES_PAUSED_NOTICE } from "../../components/shell/usePollBudget";
import { getDocumentTypeLabel, getRowStatus, getRowStatusTag } from "./documentTable";
import { getProgressView } from "./documentProgress";

export interface DocumentProgressPanelProps {
  apiClient: ApiClient;
  tenantId: string;
  /** Looked up by `index.tsx` from the already-loaded, already-polling documents list -- this
   * component never fetches on its own (the same division of labour `ReviewState.tsx` follows for
   * `?review=`). Re-renders with a fresher value on every 2 s poll tick. */
  item: DocumentListItemBody;
  onBack: () => void;
  /** ADR-020 w15 §8: the list-level "stopped checking" notice, threaded through so this document's
   * own wait shows the one control every surface that waits already shares. */
  updatesPaused: boolean;
  onResumeUpdates: () => void;
}

/**
 * The progress panel (`/documents?progress=<id>`, a state of Documents the same way
 * `ReviewState.tsx` is `?review=<id>` -- task E16/F03/US02/T02, wave w15, ADR-020 w15 footer 11).
 * Opened from an `"uploaded"`/`"processing"` row's filename link (`documentTable.ts#getOpenTarget`):
 * the perceived-instant batch means a user can open a document Raffa.ai has not actually looked at
 * yet, and this is where that honesty lives -- the six real stages
 * (`documentProgress.ts#getProgressStages`), never a guess dressed up as one.
 *
 * **Calls `POST /api/documents/{id}/prioritise` exactly once per document id** (ADR-027 w15 footer
 * C12): opening this panel *is* the signal Raffa.ai was waiting for, so there is nothing to ask the
 * user first. The call is fire-and-forget -- `ApiClient`'s own never-throws shape, and the outcome
 * is deliberately never surfaced -- because priority is an optimisation the Worker may already have
 * made moot (the job may already be claimed, or already prioritised by an earlier open of the same
 * document), never a promise this screen has to keep.
 *
 * **Never auto-navigates.** `item` comes from the same poll the list already runs, so a document
 * that finishes while this panel stays open re-renders it with a terminal `processingStatus` --
 * `getProgressView` changes the headline and offers a link, but the screen stays put; the user
 * clicks through on their own terms, the same posture `ReviewState.tsx` takes for "Mark as
 * validated" never firing itself.
 */
export default function DocumentProgressPanel({ apiClient, tenantId, item, onBack, updatesPaused, onResumeUpdates }: DocumentProgressPanelProps) {
  // Guards against re-sending the same document's prioritise call on every poll-driven re-render
  // (this effect's own dependency array already only reruns on a real id change; the ref is the
  // second, explicit line of defence against React re-mounting this component under StrictMode).
  const prioritisedIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (prioritisedIdRef.current === item.id) return;
    prioritisedIdRef.current = item.id;
    void apiClient.prioritiseDocument(tenantId, item.id);
  }, [apiClient, tenantId, item.id]);

  const view = getProgressView(item);
  const tag = getRowStatusTag(getRowStatus(item.processingStatus));

  return (
    <div className="documents-progress-panel">
      <button type="button" className="btn btn-ghost documents-review-back" onClick={onBack}>
        ← Documents
      </button>

      <header className="documents-progress-header">
        <p className="micro-meta">
          {getDocumentTypeLabel(item.documentType)}
          {item.supplierName !== null ? ` · ${item.supplierName}` : ""}
        </p>
        <h2 className="screen-title">{item.fileName}</h2>
        <div className="documents-progress-status">
          <span className={`tag tag-${tag.variant}`}>{tag.label}</span>
          <span className="micro-meta">{view.headline}</span>
        </div>
      </header>

      {view.isWaiting && (
        <>
          <p className="hint">Raffa.ai is giving this document priority over the rest of the queue.</p>

          <ol className="documents-progress-stages" aria-label="Processing stages">
            {view.stages.map((stage) => (
              <li key={stage.name} className={`documents-progress-stage documents-progress-stage--${stage.state}`}>
                <span className="documents-progress-stage-dot" aria-hidden="true" />
                {stage.name}
                {stage.state === "current" && <span className="micro-meta"> · in progress</span>}
              </li>
            ))}
          </ol>

          {updatesPaused && (
            <div className="documents-updates-paused" role="status">
              <p className="hint">{UPDATES_PAUSED_NOTICE}</p>
              <button type="button" className="btn btn-secondary" onClick={onResumeUpdates}>
                {CHECK_AGAIN_LABEL}
              </button>
            </div>
          )}
        </>
      )}

      {view.link !== null && (
        <Link to={view.link.href} className="btn btn-primary documents-progress-link">
          {view.link.label}
        </Link>
      )}
    </div>
  );
}
