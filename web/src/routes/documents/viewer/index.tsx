import { useCallback, useEffect, useState } from "react";
import { useParams, useSearchParams } from "react-router-dom";
import type { ApiClient, Contract360ClauseBody, Contract360DocumentBody, ReadBackDocument } from "../../../api/client";
import { loadCurrentWorkspace } from "../../signin/workspaceStore";
import ClauseHighlight from "../../contracts/contract360/ClauseHighlight";
import {
  BEYOND_COUNT_HEADING,
  CITATION_UNRESOLVABLE_COPY,
  DOCUMENT_MISSING_COPY,
  EMPTY_PAGE_COPY,
  FIRST_PAGE,
  PREVIEW_SERVICE_NAME,
  REAPED_PAGE_COPY,
  REAPED_PAGE_HEADING,
  formatBeyondCountCopy,
  formatPageNav,
  previewNotFoundCause,
  resolveViewerSurface,
  type CitationResolution,
} from "./documentViewerViewModel";
import "./documentViewer.css";

export interface DocumentViewerRouteProps {
  apiClient: ApiClient;
}

type DocumentLoad =
  | { phase: "loading" }
  | { phase: "missing" }
  | { phase: "error"; statusCode: number | null; message: string }
  | {
      phase: "ready";
      document: ReadBackDocument;
      clauses: readonly Contract360ClauseBody[] | null;
      documents: readonly Contract360DocumentBody[];
    };

type PreviewLoad =
  | { phase: "idle" }
  | { phase: "loading" }
  | { phase: "empty" }
  | { phase: "reaped" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; objectUrl: string };

/**
 * Route `/documents/:documentId/viewer?page=&clause=` (ADR-018 w17 clause 13;
 * ADR-029 clauses 4–7; task E22/F03/US01/T01, NW-63 w17 half). Citation-reached,
 * not a rail destination. PNG pages via `getDocumentPreviewUrl`; the caller
 * owns `URL.revokeObjectURL` in this effect's cleanup so at most the current
 * page is alive. No PDF library.
 */
export default function DocumentViewerRoute({ apiClient }: DocumentViewerRouteProps) {
  const { documentId } = useParams<{ documentId: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const workspace = loadCurrentWorkspace();

  const pageParam = searchParams.get("page");
  const clauseParam = searchParams.get("clause");

  const [documentLoad, setDocumentLoad] = useState<DocumentLoad>({ phase: "loading" });
  const [previewLoad, setPreviewLoad] = useState<PreviewLoad>({ phase: "idle" });
  const [reloadNonce, setReloadNonce] = useState(0);

  const loadDocument = useCallback(() => {
    if (!workspace || !documentId) return;
    setDocumentLoad({ phase: "loading" });

    void apiClient.getDocument(workspace.id, documentId).then(async (result) => {
      if (!result.ok || result.document === null) {
        if (result.statusCode === 404) {
          setDocumentLoad({ phase: "missing" });
          return;
        }
        setDocumentLoad({
          phase: "error",
          statusCode: result.statusCode,
          message:
            result.statusCode === 503 || result.statusCode === null
              ? `${PREVIEW_SERVICE_NAME} is temporarily unavailable. Try again in a moment.`
              : (result.error ?? "The document could not be loaded."),
        });
        return;
      }

      const document = result.document;
      let clauses: readonly Contract360ClauseBody[] | null = null;
      let documents: readonly Contract360DocumentBody[] = [];

      if (clauseParam !== null && document.contractId !== null) {
        const contractResult = await apiClient.getContract360(workspace.id, document.contractId);
        if (contractResult.ok && contractResult.contract !== null) {
          clauses = contractResult.contract.tabs.clauses;
          documents = contractResult.contract.tabs.documents;
        } else {
          clauses = [];
        }
      }

      setDocumentLoad({ phase: "ready", document, clauses, documents });
    });
  }, [apiClient, workspace?.id, documentId, clauseParam]);

  useEffect(() => {
    loadDocument();
  }, [loadDocument, reloadNonce]);

  const surface =
    documentLoad.phase === "ready" && documentId !== undefined
      ? resolveViewerSurface({
          documentId,
          pageParam,
          clauseParam,
          pageCount: documentLoad.document.pageCount,
          clauses: documentLoad.clauses,
        })
      : null;

  const fetchPage = surface?.kind === "page" ? surface.fetchPage : null;
  const pageCount = documentLoad.phase === "ready" ? documentLoad.document.pageCount : null;

  useEffect(() => {
    if (!workspace || !documentId || fetchPage === null) {
      setPreviewLoad({ phase: "idle" });
      return;
    }

    let cancelled = false;
    let objectUrl: string | null = null;
    setPreviewLoad({ phase: "loading" });

    void apiClient.getDocumentPreviewUrl(workspace.id, documentId, fetchPage).then((result) => {
      if (cancelled) {
        if (result.objectUrl !== null) URL.revokeObjectURL(result.objectUrl);
        return;
      }

      if (result.ok && result.objectUrl !== null) {
        objectUrl = result.objectUrl;
        setPreviewLoad({ phase: "ready", objectUrl: result.objectUrl });
        return;
      }

      if (result.statusCode === 404) {
        setPreviewLoad({ phase: previewNotFoundCause(pageCount) === "reaped" ? "reaped" : "empty" });
        return;
      }

      setPreviewLoad({
        phase: "error",
        statusCode: result.statusCode,
        message:
          result.statusCode === 503 || result.statusCode === null
            ? `${PREVIEW_SERVICE_NAME} is temporarily unavailable. Try again in a moment.`
            : (result.error ?? `${PREVIEW_SERVICE_NAME} could not load this page.`),
      });
    });

    return () => {
      cancelled = true;
      if (objectUrl !== null) URL.revokeObjectURL(objectUrl);
    };
  }, [apiClient, workspace?.id, documentId, fetchPage, pageCount, reloadNonce]);

  const writePage = (nextPage: number, clauseId: string | null) => {
    const next = new URLSearchParams();
    next.set("page", String(nextPage));
    if (clauseId !== null) next.set("clause", clauseId);
    setSearchParams(next);
  };

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before opening a document.</p>
      </div>
    );
  }

  if (!documentId) {
    return (
      <div className="empty-state" role="status">
        <h3>{DOCUMENT_MISSING_COPY}</h3>
      </div>
    );
  }

  if (documentLoad.phase === "loading") {
    return (
      <div className="document-viewer" aria-busy="true">
        <ViewerChrome
          fileName={null}
          navLabel={formatPageNav(FIRST_PAGE, null)}
          canPrev={false}
          canNext={false}
          onPrev={() => undefined}
          onNext={() => undefined}
        />
        <div className="document-viewer-canvas" role="status" aria-live="polite">
          <div className="skeleton" />
        </div>
      </div>
    );
  }

  if (documentLoad.phase === "missing") {
    return (
      <div className="empty-state" role="status">
        <h3>{DOCUMENT_MISSING_COPY}</h3>
      </div>
    );
  }

  if (documentLoad.phase === "error") {
    return (
      <div className="error-state" role="alert">
        <h4>Preview unavailable</h4>
        <p className="micro-meta">
          {documentLoad.message}
          {documentLoad.statusCode !== null && ` (HTTP ${documentLoad.statusCode})`}
        </p>
        <button type="button" className="btn btn-secondary" onClick={() => setReloadNonce((n) => n + 1)}>
          Retry
        </button>
      </div>
    );
  }

  if (surface === null) return null;

  const resolvedClauseId = surface.kind === "page" && surface.citation.kind === "resolved" ? surface.citation.clause.clauseId : null;

  if (surface.kind === "beyond-count") {
    return (
      <div className="document-viewer">
        <ViewerChrome
          fileName={documentLoad.document.fileName}
          navLabel={formatPageNav(surface.askedPage, surface.pageCount)}
          canPrev={false}
          canNext={false}
          onPrev={() => undefined}
          onNext={() => undefined}
        />
        <div className="empty-state" role="status">
          <h3>{BEYOND_COUNT_HEADING}</h3>
          <p className="micro-meta">{formatBeyondCountCopy(surface.pageCount)}</p>
          <button type="button" className="btn btn-secondary" onClick={() => writePage(FIRST_PAGE, clauseParam)}>
            Go to page 1
          </button>
        </div>
      </div>
    );
  }

  const citation: CitationResolution = surface.citation;
  const canPrev = surface.page > FIRST_PAGE;
  const canNext = documentLoad.document.pageCount !== null && surface.page < documentLoad.document.pageCount;
  const onPrev = () => writePage(surface.page - 1, resolvedClauseId);
  const onNext = () => writePage(surface.page + 1, resolvedClauseId);

  return (
    <div className="document-viewer">
      <ViewerChrome
        fileName={documentLoad.document.fileName}
        navLabel={formatPageNav(surface.page, documentLoad.document.pageCount)}
        canPrev={canPrev}
        canNext={canNext}
        onPrev={onPrev}
        onNext={onNext}
      />

      {citation.kind === "unresolvable" && (
        <div className="empty-state document-viewer-citation-notice" role="status" data-testid="document-viewer-citation-notice">
          <h3>{CITATION_UNRESOLVABLE_COPY}</h3>
          <p className="micro-meta">The document is still open. You can page through it.</p>
        </div>
      )}

      {citation.kind === "resolved" && (
        <p className="micro-meta">{`Page ${surface.page} — the wording is highlighted below`}</p>
      )}

      {previewLoad.phase === "reaped" ? (
        <div className="empty-state" role="status">
          <h3>{REAPED_PAGE_HEADING}</h3>
          <p className="micro-meta">{REAPED_PAGE_COPY}</p>
          <button type="button" className="btn btn-secondary" onClick={() => setReloadNonce((n) => n + 1)}>
            Reload the document
          </button>
        </div>
      ) : (
        <PageCanvas preview={previewLoad} onRetry={() => setReloadNonce((n) => n + 1)} />
      )}

      {citation.kind === "resolved" && (
        <ClauseHighlight clause={citation.clause} documents={documentLoad.documents} />
      )}
    </div>
  );
}

function ViewerChrome({
  fileName,
  navLabel,
  canPrev,
  canNext,
  onPrev,
  onNext,
}: {
  fileName: string | null;
  navLabel: string;
  canPrev: boolean;
  canNext: boolean;
  onPrev: () => void;
  onNext: () => void;
}) {
  return (
    <>
      <header className="document-viewer-header">
        <p className="screen-kicker">Documents</p>
        <h2 className="screen-title">{fileName ?? "Document"}</h2>
      </header>
      <div className="document-viewer-nav" role="navigation" aria-label="Page">
        <button type="button" className="btn btn-ghost" disabled={!canPrev} onClick={onPrev}>
          Previous
        </button>
        <span className="document-viewer-nav-label">{navLabel}</span>
        <button type="button" className="btn btn-ghost" disabled={!canNext} onClick={onNext}>
          Next
        </button>
      </div>
    </>
  );
}

function PageCanvas({
  preview,
  onRetry,
}: {
  preview: PreviewLoad;
  onRetry: () => void;
}) {
  if (preview.phase === "error") {
    return (
      <div className="error-state" role="alert">
        <h4>Preview unavailable</h4>
        <p className="micro-meta">{preview.message}</p>
        <button type="button" className="btn btn-secondary" onClick={onRetry}>
          Retry
        </button>
      </div>
    );
  }

  if (preview.phase === "empty") {
    return (
      <div className="document-viewer-canvas">
        <div className="empty-state" role="status">
          <h3>{EMPTY_PAGE_COPY}</h3>
        </div>
      </div>
    );
  }

  if (preview.phase === "ready") {
    return (
      <div className="document-viewer-canvas">
        <img src={preview.objectUrl} alt="Document page" />
      </div>
    );
  }

  return (
    <div className="document-viewer-canvas" role="status" aria-live="polite">
      <div className="skeleton" />
    </div>
  );
}
