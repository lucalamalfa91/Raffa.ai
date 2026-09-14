import { useState } from "react";
import { Link } from "react-router-dom";
import type { DocumentListItemBody } from "../../api/client";
import { CHECK_AGAIN_LABEL, UPDATES_PAUSED_NOTICE } from "../../components/shell/usePollBudget";
import { getRejectionReasonCopy, type LocalUploadEntry } from "./uploadPipeline";
import {
  formatUploadedAt,
  getDocumentTypeLabel,
  getRowAction,
  getRowStatus,
  getRowStatusTag,
  type AttentionFilterValue,
  type RowStatus,
} from "./documentTable";
import ProcessingPipeline from "./ProcessingPipeline";

export interface DocumentStatusTableProps {
  /** Already filtered by the caller's current chip. */
  documents: readonly DocumentListItemBody[];
  filter: AttentionFilterValue;
  localUploads: readonly LocalUploadEntry[];
  onRetryLocal: (key: string) => void;
  onRetryServer: (documentId: string) => void;
  onDelete: (documentId: string) => void;
  /** R-WEB-07: "Procurement sees ... delete disabled." */
  isAdmin: boolean;
  /** ADR-020 w15 §8: the list-level "stopped checking" notice with its one resume control. */
  updatesPaused?: boolean;
  onResumeUpdates?: () => void;
}

/** ADR-019 w15 clause 4 / ADR-020 w15 §6: a local entry's tag is keyed on the reading, not on where
 * the fact came from -- a refused file reads "Not added" whether the server or this browser
 * refused it. The third branch is the one the compiler never asks for (`DocumentStatusTable.tsx`
 * used to map this with a two-way ternary), so it is a switch here, not a ternary. */
function localRowStatus(phase: LocalUploadEntry["phase"]): RowStatus {
  switch (phase) {
    case "failed":
      return "failed";
    case "rejected":
      return "rejected";
    case "queued":
    case "uploading":
      return "processing";
  }
}

/**
 * The Documents V2 row grid (`raffa-v2/markup.html`'s `docRows`; screens-v2.md #3). Implemented as
 * a real `<table>` (ADR-019's locked `.table` catalogue entry), not a literal port of the prototype's
 * own CSS-grid divs -- the same translation every other list screen in this app already makes
 * (Portfolio, Renewals, the field-review list). A row's primary interactive surface is the filename
 * `<Link>` (or a plain, honestly-reasoned span when there is nowhere to go yet), never a bare
 * `<tr onClick>` -- `../contracts/review/ReviewFieldList.tsx`'s own header comment states why
 * (keyboard/screen-reader reachability); the action cell's own control is a second, independent
 * interactive surface with its own destination (screens-v2.md #3's row-click and action-button
 * targets genuinely differ for a `completed` row: the filename opens Contract 360, "Ask about it"
 * opens a new Ask chat).
 *
 * Task E16/F03/US01/T01 (wave w15): a refused file is a *row*, never a card (ADR-020 w15 §1 and
 * §6) -- the server's `Rejected` row with its reason hint, or a local row for a refusal that never
 * reached the server -- with an empty action cell and no dismiss; a server row at `Uploaded` reads
 * "Queued…" (§1.6), a local pre-201 row still reads "Uploading…" (ADR-012 w15 §13.7); and the
 * stopped-poll notice renders below the grid as `.hint` + `.btn-secondary` (§8), a list state,
 * never a row state.
 */
export default function DocumentStatusTable({
  documents,
  filter,
  localUploads,
  onRetryLocal,
  onRetryServer,
  onDelete,
  isAdmin,
  updatesPaused = false,
  onResumeUpdates,
}: DocumentStatusTableProps) {
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null);

  // A local refusal (or an in-flight upload) appears only in the default list, above the server
  // rows, exactly where the card was -- the third chip reads the server bucket alone (ADR-020 w15
  // §6.4).
  const visibleLocalUploads = filter === "rejected" ? [] : localUploads;
  const attentionEmpty = filter === "attention" && documents.length === 0 && visibleLocalUploads.length === 0;
  const rowsVisible = documents.length > 0 || visibleLocalUploads.length > 0;

  return (
    <div className="documents-list-body">
      {attentionEmpty && (
        <div className="documents-attention-empty">
          <h3>Nothing needs you right now.</h3>
          <p className="text-muted micro-meta">
            Every uploaded document is validated and askable. Completed documents are tucked away — switch to{" "}
            <strong>All documents</strong> to browse them.
          </p>
        </div>
      )}

      {rowsVisible && (
        <table className="table document-status-table">
          <thead>
            <tr>
              <th scope="col">Document</th>
              <th scope="col">Supplier · type</th>
              <th scope="col">Status</th>
              <th scope="col">Next step</th>
              {isAdmin && <th scope="col">Delete</th>}
            </tr>
          </thead>
          <tbody>
            {visibleLocalUploads.map((entry) => {
              const tag = getRowStatusTag(localRowStatus(entry.phase));
              return (
                <tr key={entry.key}>
                  <td>
                    <div className="document-status-table-filename">{entry.file.name}</div>
                    {entry.errorMessage && <div className="hint">{entry.errorMessage}</div>}
                  </td>
                  <td className="micro-meta">—</td>
                  <td>
                    <span className={`tag tag-${tag.variant}`}>{tag.label}</span>
                    {entry.phase === "uploading" && <ProcessingPipeline stage={null} />}
                  </td>
                  <td className="document-status-table-next-step">
                    {entry.phase === "failed" ? (
                      <button type="button" className="btn btn-secondary" onClick={() => onRetryLocal(entry.key)}>
                        Retry upload
                      </button>
                    ) : entry.phase === "rejected" ? null : (
                      <span className="micro-meta">Uploading…</span>
                    )}
                  </td>
                  {isAdmin && <td />}
                </tr>
              );
            })}

            {documents.map((item) => {
              const rowStatus = getRowStatus(item.processingStatus);
              const tag = getRowStatusTag(rowStatus);
              const action = getRowAction(item);
              const isQuote = item.documentType === "Quote";
              const rejectionHint = rowStatus === "rejected" ? getRejectionReasonCopy(item.rejectionReason) : null;
              const openTarget =
                rowStatus === "needs_review"
                  ? `/documents?review=${item.id}`
                  : rowStatus === "completed"
                    ? isQuote
                      ? "/quotes"
                      : item.contractId !== null
                        ? `/contracts/${item.contractId}`
                        : null
                    : null;

              return (
                <tr key={item.id}>
                  <td>
                    {openTarget !== null ? (
                      <Link to={openTarget} className="document-status-table-link">
                        {item.fileName}
                      </Link>
                    ) : (
                      <>
                        <div className="document-status-table-filename">{item.fileName}</div>
                        {rowStatus === "failed" && <div className="hint">Not yet linked to a contract</div>}
                        {rejectionHint !== null && <div className="hint">{rejectionHint}</div>}
                      </>
                    )}
                    <div className="micro-meta">
                      {item.pageCount !== null ? `${item.pageCount} page${item.pageCount === 1 ? "" : "s"} · ` : ""}
                      {formatUploadedAt(item.createdAt)}
                    </div>
                  </td>
                  <td className="document-status-table-supplier">
                    <span>{item.supplierName ?? "—"}</span> <span className="micro-meta">· {getDocumentTypeLabel(item.documentType)}</span>
                  </td>
                  <td>
                    <span className={`tag tag-${tag.variant}`}>{tag.label}</span>
                    {rowStatus === "processing" && <ProcessingPipeline stage={item.stage} />}
                  </td>
                  <td className="document-status-table-next-step">
                    {rowStatus === "processing" ? (
                      // ADR-020 w15 §1.6: a stored row waiting for a Worker is "Queued…", never
                      // "Uploading…" -- the bytes are already durable; a `Processing` row reads its
                      // real stage string, verbatim.
                      <span className="micro-meta">{(item.stage ?? "Queued") + "…"}</span>
                    ) : action !== null ? (
                      action.kind === "retry" ? (
                        <button type="button" className="btn btn-secondary" onClick={() => onRetryServer(item.id)}>
                          {action.label}
                        </button>
                      ) : action.kind === "ask" ? (
                        // `raffa-v2/app.jsx`'s own "Ask about it" handler both opens a new chat
                        // *and* pre-asks "When does {supplier} expire?" -- `state.query` is the same
                        // seed mechanism `components/ask-bar/GlobalAskBar.tsx` already establishes
                        // (`AskRoute` already reads `state.query` today, per web/README.md's own
                        // "Ask Raffa" section), not a new, unread convention invented here.
                        <Link
                          to={`/ask?scope=${item.contractId ?? ""}`}
                          state={{ query: `When does ${item.supplierName ?? "it"} expire?`, newChat: true }}
                          className="btn btn-secondary"
                        >
                          {action.label}
                        </Link>
                      ) : (
                        <Link
                          to={action.kind === "review" ? `/documents?review=${item.id}` : "/quotes"}
                          className={`btn ${action.kind === "review" ? "btn-primary" : "btn-secondary"}`}
                        >
                          {action.label}
                        </Link>
                      )
                    ) : null}
                  </td>
                  {isAdmin && (
                    <td>
                      {confirmingDeleteId === item.id ? (
                        <div className="document-status-table-delete-confirm">
                          <button
                            type="button"
                            className="btn btn-secondary"
                            onClick={() => {
                              setConfirmingDeleteId(null);
                              onDelete(item.id);
                            }}
                          >
                            Confirm delete
                          </button>
                          <button type="button" className="btn btn-ghost" onClick={() => setConfirmingDeleteId(null)}>
                            Cancel
                          </button>
                        </div>
                      ) : (
                        <button type="button" className="btn btn-ghost" onClick={() => setConfirmingDeleteId(item.id)}>
                          Delete
                        </button>
                      )}
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      {updatesPaused && (
        <div className="documents-updates-paused" role="status">
          <p className="hint">{UPDATES_PAUSED_NOTICE}</p>
          {onResumeUpdates !== undefined && (
            <button type="button" className="btn btn-secondary" onClick={onResumeUpdates}>
              {CHECK_AGAIN_LABEL}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
