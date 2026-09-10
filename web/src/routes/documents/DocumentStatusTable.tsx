import { useState } from "react";
import { Link } from "react-router-dom";
import type { DocumentListItemBody } from "../../api/client";
import type { LocalUploadEntry, RejectedFileOutcome } from "./uploadPipeline";
import {
  formatUploadedAt,
  getDocumentTypeLabel,
  getRowAction,
  getRowStatus,
  getRowStatusTag,
  type AttentionFilterValue,
} from "./documentTable";
import ProcessingPipeline from "./ProcessingPipeline";
import UploadResultCard from "./UploadResultCard";

export interface DocumentStatusTableProps {
  /** Already filtered by the caller's current attention/all toggle. */
  documents: readonly DocumentListItemBody[];
  filter: AttentionFilterValue;
  localUploads: readonly LocalUploadEntry[];
  rejected: readonly RejectedFileOutcome[];
  onDismissRejected: (key: string) => void;
  onRetryLocal: (key: string) => void;
  onRetryServer: (documentId: string) => void;
  onDelete: (documentId: string) => void;
  /** R-WEB-07: "Procurement sees ... delete disabled." */
  isAdmin: boolean;
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
 */
export default function DocumentStatusTable({
  documents,
  filter,
  localUploads,
  rejected,
  onDismissRejected,
  onRetryLocal,
  onRetryServer,
  onDelete,
  isAdmin,
}: DocumentStatusTableProps) {
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null);

  const attentionEmpty = filter === "attention" && documents.length === 0 && localUploads.length === 0;
  const rowsVisible = documents.length > 0 || localUploads.length > 0;

  return (
    <div className="documents-list-body">
      {rejected.map((entry) => (
        <UploadResultCard key={entry.key} fileName={entry.fileName} message={entry.message} onDismiss={() => onDismissRejected(entry.key)} />
      ))}

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
            {localUploads.map((entry) => (
              <tr key={entry.key}>
                <td>
                  <div className="document-status-table-filename">{entry.file.name}</div>
                  {entry.phase === "failed" && entry.errorMessage && <div className="hint">{entry.errorMessage}</div>}
                </td>
                <td className="micro-meta">—</td>
                <td>
                  <span className={`tag tag-${getRowStatusTag(entry.phase === "failed" ? "failed" : "processing").variant}`}>
                    {getRowStatusTag(entry.phase === "failed" ? "failed" : "processing").label}
                  </span>
                  {entry.phase === "uploading" && <ProcessingPipeline stage={null} />}
                </td>
                <td className="document-status-table-next-step">
                  {entry.phase === "failed" ? (
                    <button type="button" className="btn btn-secondary" onClick={() => onRetryLocal(entry.key)}>
                      Retry upload
                    </button>
                  ) : (
                    <span className="micro-meta">Uploading…</span>
                  )}
                </td>
                {isAdmin && <td />}
              </tr>
            ))}

            {documents.map((item) => {
              const rowStatus = getRowStatus(item.processingStatus);
              const tag = getRowStatusTag(rowStatus);
              const action = getRowAction(item);
              const isQuote = item.documentType === "Quote";
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
                      <span className="micro-meta">{(item.stage ?? "Uploading") + "…"}</span>
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
    </div>
  );
}
