import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import type { WorkspaceRole } from "../../components/shell/navItems";
import { useDocumentsList } from "./useDocumentsList";
import OnboardingEmptyState from "./OnboardingEmptyState";
import AttentionFilter from "./AttentionFilter";
import UploadDropzone from "./UploadDropzone";
import DocumentStatusTable from "./DocumentStatusTable";
import ReviewState from "./ReviewState";
import DocumentProgressPanel from "./DocumentProgressPanel";
import { createSampleDocumentFile, type SampleDocumentKey } from "./sampleDocument";
import { buildKbSummary, getDocumentTypeLabel } from "./documentTable";
import "./documents.css";

export interface DocumentsRouteProps {
  apiClient: ApiClient;
  /**
   * Task E14/F03/US02/T01 (wave w14 "workspace is real"): threaded through from
   * `WorkspaceShellApp.tsx`'s `ShellRoutes` instead of this route calling the now-deleted
   * `resolveWorkspaceRole()` itself -- the role is a server fact (`GET /api/workspaces`'s row) from
   * this wave on, and `ShellRoutes` already has it in scope for every other route that needs it.
   */
  role: WorkspaceRole;
}

interface JustValidated {
  contractId: string;
  displayName: string;
}

/**
 * Route `/documents` (ADR-018/ADR-024; `raffa-v2/screens-v2.md` #3 and #4; task E13/F09/US01/T03).
 * V2 rebuild of `raffa-v2/app.jsx`'s own state machine (`docView: 'list' | 'review'`,
 * `docFilter`, `justValidated`) -- four states, not V1's single upload-pipeline screen:
 *
 *   1. **Onboarding empty** (`OnboardingEmptyState.tsx`) -- this tenant has no tracked document at
 *      all (not even an in-flight/rejected one this session).
 *   2. **List** (`AttentionFilter.tsx` + `DocumentStatusTable.tsx`) -- the default once anything
 *      exists; server-backed (`useDocumentsList.ts`, `GET /api/documents`, R-DOC-06). Every number
 *      on it is the server's own `counts` (ADR-027 §D7; task E16/F03/US01/T01, wave w15) -- the
 *      `sessionStorage` tracker the rail used to read is deleted (ADR-012 w15 §6).
 *   3. **Review, a state of Documents** (`?review=<documentId>`, `ReviewState.tsx`) -- rendered in
 *      place of the list, never a separate route.
 *   4. **Progress, a state of Documents** (`?progress=<documentId>`, `DocumentProgressPanel.tsx`,
 *      task E16/F03/US02/T02, wave w15, ADR-020 w15 footer 11) -- the same "in place of the list,
 *      never a separate route" idiom as review, opened from an `"uploaded"`/`"processing"` row.
 *
 * `?filter=all` (read once, on mount) honours `components/shell/WorkspaceShellApp.tsx`'s own
 * `/review -> /documents?filter=attention` redirect target and any future explicit link to the
 * unfiltered view; the default (`"attention"`, R-DOC-06) already matches that redirect's own value,
 * so this is a no-op for that specific link and only matters for a hypothetical `?filter=all` one.
 */
export default function DocumentsRoute({ apiClient, role }: DocumentsRouteProps) {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const workspace = loadCurrentWorkspace();
  const list = useDocumentsList(apiClient);
  const [justValidated, setJustValidated] = useState<JustValidated | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [confirmingDeleteAll, setConfirmingDeleteAll] = useState(false);

  useEffect(() => {
    if (searchParams.get("filter") === "all") {
      list.setFilter("all");
    }
    // Mount-only: seeds the initial toggle from the URL once, the same "read once" convention this
    // app already uses for role/workspace query-string reads (`workspaceRole.ts`).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const reviewDocumentId = searchParams.get("review");
  const progressDocumentId = searchParams.get("progress");

  if (!workspace) {
    // Should not normally be reachable -- App.tsx only mounts the shell (and therefore this route)
    // once a workspace is current -- but this route reads the store directly rather than trusting
    // that earlier check, the same posture V1 already took.
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before uploading documents.</p>
      </div>
    );
  }

  if (reviewDocumentId !== null) {
    const target = list.documents.find((item) => item.id === reviewDocumentId);

    if (list.fetchState === "loading" && target === undefined) {
      return (
        <div className="review-skeleton" role="status" aria-live="polite">
          <p className="micro-meta">Loading review…</p>
        </div>
      );
    }

    if (target === undefined || target.contractId === null) {
      return (
        <div className="empty-state" role="status">
          <h3>Document not ready for review</h3>
          <p className="micro-meta">This document is not yet linked to a contract, or no longer exists.</p>
          <button type="button" className="btn btn-secondary" onClick={() => navigate("/documents")}>
            ← Documents
          </button>
        </div>
      );
    }

    const displayName = target.supplierName ?? getDocumentTypeLabel(target.documentType);
    return (
      <ReviewState
        apiClient={apiClient}
        contractId={target.contractId}
        documentId={target.id}
        onBack={() => navigate("/documents")}
        onValidated={(contractId) => {
          setJustValidated({ contractId, displayName });
          navigate("/documents");
        }}
      />
    );
  }

  if (progressDocumentId !== null) {
    const target = list.documents.find((item) => item.id === progressDocumentId);

    if (list.fetchState === "loading" && target === undefined) {
      return (
        <div className="review-skeleton" role="status" aria-live="polite">
          <p className="micro-meta">Loading…</p>
        </div>
      );
    }

    if (target === undefined) {
      return (
        <div className="empty-state" role="status">
          <h3>This document no longer exists</h3>
          <p className="micro-meta">It may have been deleted, or this link is stale.</p>
          <button type="button" className="btn btn-secondary" onClick={() => navigate("/documents")}>
            ← Documents
          </button>
        </div>
      );
    }

    return (
      <DocumentProgressPanel
        apiClient={apiClient}
        tenantId={workspace.id}
        item={target}
        onBack={() => navigate("/documents")}
        updatesPaused={list.updatesPaused}
        onResumeUpdates={list.resumeUpdates}
      />
    );
  }

  const handleFilesSelected = (files: File[]) => {
    // A fresh batch supersedes the previous validated hook -- `raffa-v2/app.jsx`'s own
    // `justValidated` is a single slot, replaced (not accumulated) by the next relevant event.
    setJustValidated(null);
    list.uploadFiles(files);
  };

  // Two sample MSAs, two different suppliers (`sampleDocument.ts`): one written plainly enough to
  // complete without review, one whose text is genuinely ambiguous and lands in needs_review -- the
  // two ends of the real product path, never a scripted verdict.
  const handleUseSampleFile = (key: SampleDocumentKey) => {
    handleFilesSelected([createSampleDocumentFile(key)]);
  };

  const handleDelete = (documentId: string) => {
    setDeleteError(null);
    void apiClient.deleteDocument(workspace.id, documentId).then((result) => {
      if (!result.ok) {
        setDeleteError(result.error ?? "This document could not be deleted.");
        return;
      }
      list.reload();
    });
  };

  const handleDeleteAll = () => {
    setDeleteError(null);
    void apiClient.deleteAllDocuments(workspace.id).then((result) => {
      if (!result.ok) {
        setDeleteError(result.error ?? "Documents could not be deleted.");
        return;
      }
      setConfirmingDeleteAll(false);
      list.reload();
    });
  };

  // Onboarding empty is a server fact plus this session's own in-flight rows: nothing Raffa.ai
  // keeps (`counts.all`), nothing it refused (`counts.rejected` -- a refusal is a row now, ADR-020
  // w15 §1), and nothing picked in this browser yet. Never the fetched page's length.
  const isEmpty =
    list.fetchState === "ready" && list.counts.all === 0 && list.counts.rejected === 0 && list.localUploads.length === 0;

  if (list.fetchState === "loading") {
    return (
      <div className="documents-screen" role="status" aria-live="polite">
        <p className="micro-meta">Loading documents…</p>
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="skeleton documents-list-skeleton-row" />
        ))}
      </div>
    );
  }

  // A load that fails after a good load -- or after this session's own drop -- keeps the rows on
  // screen (they are what the server last said, and the optimistic row must not vanish, ADR-012
  // w15 §5); only a tenant with nothing at all to show gets the error state in place of the list.
  if (list.fetchState === "error" && list.counts.all === 0 && list.counts.rejected === 0 && list.localUploads.length === 0) {
    return (
      <div className="error-state" role="alert">
        <h4>Documents unavailable</h4>
        <p className="micro-meta">{list.errorMessage}</p>
        <button type="button" className="btn btn-secondary" onClick={list.reload}>
          Retry
        </button>
      </div>
    );
  }

  if (isEmpty) {
    return <OnboardingEmptyState onFilesSelected={handleFilesSelected} onUseSampleFile={handleUseSampleFile} />;
  }

  return (
    <div className="documents-screen documents-screen--list">
      <header className="screen-header">
        <div>
          <h2 className="screen-title">Documents</h2>
          <p className="screen-header-summary">{buildKbSummary(list.counts)}</p>
        </div>
        {role === "admin" && (
          <div className="screen-header-actions">
            {confirmingDeleteAll ? (
              <div className="document-status-table-delete-confirm">
                <button type="button" className="btn btn-primary" onClick={handleDeleteAll}>
                  Confirm delete all
                </button>
                <button
                  type="button"
                  className="btn document-status-table-delete-cancel"
                  onClick={() => setConfirmingDeleteAll(false)}
                >
                  Cancel
                </button>
              </div>
            ) : (
              <button type="button" className="btn btn-ghost" onClick={() => setConfirmingDeleteAll(true)}>
                Delete all documents
              </button>
            )}
          </div>
        )}
      </header>

      <UploadDropzone variant="list" onFilesSelected={handleFilesSelected} onUseSampleFile={handleUseSampleFile} />

      {justValidated !== null && (
        <div className="documents-validated-hook">
          <span className="documents-validated-hook-text">
            <strong>{justValidated.displayName}</strong> is now askable.
          </span>
          <Link
            to={`/ask?scope=${justValidated.contractId}`}
            state={{ query: "When does it expire?", newChat: true }}
            className="documents-validated-hook-ask"
          >
            Ask: when does it expire?
          </Link>
        </div>
      )}

      <div className="documents-list-main">
      {list.fetchState === "error" && list.errorMessage !== null && (
        <p className="hint" role="alert">
          {list.errorMessage}
        </p>
      )}
      {deleteError !== null && (
        <p className="hint" role="alert">
          {deleteError}
        </p>
      )}
      {list.retryError !== null && (
        <p className="hint" role="alert">
          {list.retryError}
        </p>
      )}

      <AttentionFilter value={list.filter} onChange={list.setFilter} counts={list.counts} />

      <DocumentStatusTable
        documents={list.filteredDocuments}
        filter={list.filter}
        localUploads={list.localUploads}
        onRetryLocal={list.retryLocalUpload}
        onRetryServer={list.retryServerDocument}
        onDelete={handleDelete}
        isAdmin={role === "admin"}
        updatesPaused={list.updatesPaused}
        onResumeUpdates={list.resumeUpdates}
      />

      <p className="micro-meta documents-legend">
        uploaded → processing → needs review → completed. Only <strong>completed</strong> documents feed Ask Raffa,
        Portfolio and Renewals.
      </p>
      </div>
    </div>
  );
}
