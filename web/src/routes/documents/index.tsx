import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { resolveWorkspaceRole } from "../../components/shell/workspaceRole";
import { useDocumentsList } from "./useDocumentsList";
import OnboardingEmptyState from "./OnboardingEmptyState";
import AttentionFilter from "./AttentionFilter";
import UploadDropzone from "./UploadDropzone";
import DocumentStatusTable from "./DocumentStatusTable";
import ReviewState from "./ReviewState";
import { createSampleDocumentFile } from "./sampleDocument";
import { buildKbSummary, getDocumentTypeLabel } from "./documentTable";
import "./documents.css";

export interface DocumentsRouteProps {
  apiClient: ApiClient;
}

interface JustValidated {
  contractId: string;
  displayName: string;
}

/**
 * Route `/documents` (ADR-018/ADR-024; `contigo-v2/screens-v2.md` #3 and #4; task E13/F09/US01/T03).
 * V2 rebuild of `contigo-v2/app.jsx`'s own state machine (`docView: 'list' | 'review'`,
 * `docFilter`, `justValidated`) -- three states, not V1's single upload-pipeline screen:
 *
 *   1. **Onboarding empty** (`OnboardingEmptyState.tsx`) -- this tenant has no tracked document at
 *      all (not even an in-flight/rejected one this session).
 *   2. **List** (`AttentionFilter.tsx` + `DocumentStatusTable.tsx`) -- the default once anything
 *      exists; server-backed (`useDocumentsList.ts`, `GET /api/documents`, R-DOC-06), not
 *      `sessionStorage` (see `documentStore.ts`'s own updated header comment for the one remaining
 *      reader of that module, `components/shell/RailNav.tsx`, out of this task's file scope).
 *   3. **Review, a state of Documents** (`?review=<documentId>`, `ReviewState.tsx`) -- rendered in
 *      place of the list, never a separate route.
 *
 * `?filter=all` (read once, on mount) honours `components/shell/WorkspaceShellApp.tsx`'s own
 * `/review -> /documents?filter=attention` redirect target and any future explicit link to the
 * unfiltered view; the default (`"attention"`, R-DOC-06) already matches that redirect's own value,
 * so this is a no-op for that specific link and only matters for a hypothetical `?filter=all` one.
 */
export default function DocumentsRoute({ apiClient }: DocumentsRouteProps) {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const workspace = loadCurrentWorkspace();
  const role = resolveWorkspaceRole();
  const list = useDocumentsList(apiClient);
  const [justValidated, setJustValidated] = useState<JustValidated | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  useEffect(() => {
    if (searchParams.get("filter") === "all") {
      list.setFilter("all");
    }
    // Mount-only: seeds the initial toggle from the URL once, the same "read once" convention this
    // app already uses for role/workspace query-string reads (`workspaceRole.ts`).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const reviewDocumentId = searchParams.get("review");

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
        onBack={() => navigate("/documents")}
        onValidated={(contractId) => {
          setJustValidated({ contractId, displayName });
          navigate("/documents");
        }}
      />
    );
  }

  const handleFilesSelected = (files: File[]) => {
    // A fresh batch supersedes the previous validated hook -- `contigo-v2/app.jsx`'s own
    // `justValidated` is a single slot, replaced (not accumulated) by the next relevant event.
    setJustValidated(null);
    list.uploadFiles(files);
  };

  const handleUseSampleFile = () => {
    handleFilesSelected([createSampleDocumentFile()]);
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

  const isEmpty =
    list.fetchState === "ready" &&
    list.documents.length === 0 &&
    list.localUploads.length === 0 &&
    list.rejected.length === 0;

  if (list.fetchState === "loading" && list.documents.length === 0) {
    return (
      <div className="documents-screen" role="status" aria-live="polite">
        <p className="micro-meta">Loading documents…</p>
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="skeleton documents-list-skeleton-row" />
        ))}
      </div>
    );
  }

  if (list.fetchState === "error" && list.documents.length === 0) {
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
      <div className="documents-list-header">
        <h2 className="screen-title">Documents</h2>
        <p className="micro-meta">{buildKbSummary(list.documents)}</p>
      </div>

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

      <AttentionFilter value={list.filter} onChange={list.setFilter} attentionCount={list.attentionCount} allCount={list.allCount} />

      <DocumentStatusTable
        documents={list.filteredDocuments}
        filter={list.filter}
        localUploads={list.localUploads}
        rejected={list.rejected}
        onDismissRejected={list.dismissRejected}
        onRetryLocal={list.retryLocalUpload}
        onRetryServer={list.retryServerDocument}
        onDelete={handleDelete}
        isAdmin={role === "admin"}
      />

      <p className="micro-meta documents-legend">
        uploaded → processing → needs review → completed. Only <strong>completed</strong> documents feed Ask Contigo,
        Portfolio and Renewals.
      </p>
    </div>
  );
}
