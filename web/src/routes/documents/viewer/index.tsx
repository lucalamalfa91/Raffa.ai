import { useCallback, useEffect, useState } from "react";
import { useParams, useSearchParams } from "react-router-dom";
import type {
  ApiClient,
  Contract360ClauseBody,
  ContractFieldEvidenceBody,
  ReadBackDocument,
} from "../../../api/client";
import { loadCurrentWorkspace } from "../../signin/workspaceStore";
import BoxOverlay, { selectPageBoxes, type PageBoxSpec } from "./BoxOverlay";
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

export interface DocumentViewerProps {
  apiClient: ApiClient;
  documentId: string | undefined;
  pageParam: string | null;
  clauseParam: string | null;
  onPageChange: (nextPage: number, clauseId: string | null) => void;
}

type DocumentLoad =
  | { phase: "loading" }
  | { phase: "missing" }
  | { phase: "error"; statusCode: number | null; message: string }
  | {
      phase: "ready";
      document: ReadBackDocument;
      clauses: readonly Contract360ClauseBody[] | null;
    };

type PreviewLoad =
  | { phase: "idle" }
  | { phase: "loading" }
  | { phase: "empty" }
  | { phase: "reaped" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; objectUrl: string };

/**
 * Document viewer internals (page fetch, pdfium PNG, chrome). The dedicated
 * `/documents/:documentId/viewer` route and the in-app overlay both render this
 * so paging, highlights and empty/error states stay one implementation.
 */
export function DocumentViewer({
  apiClient,
  documentId,
  pageParam,
  clauseParam,
  onPageChange,
}: DocumentViewerProps) {
  const workspace = loadCurrentWorkspace();

  const [documentLoad, setDocumentLoad] = useState<DocumentLoad>({ phase: "loading" });
  const [previewLoad, setPreviewLoad] = useState<PreviewLoad>({ phase: "idle" });
  const [reloadNonce, setReloadNonce] = useState(0);
  // Task E23/F04/US01/T01 (NW-63r): the contract's field evidence, fetched once per document (not
  // per page) so BoxOverlay can select whichever fields' phrases sit on the page currently on
  // screen. A fetch failure degrades to "no boxes this page" -- the same honest w17 fallback
  // (text-level highlight only) a null box already gets, never a viewer-wide error.
  const [evidence, setEvidence] = useState<readonly ContractFieldEvidenceBody[]>([]);
  // The page `<img>`'s own naturalWidth/naturalHeight (its `onLoad`), so BoxOverlay can scale
  // pixel-space boxes to however large the viewport renders the page. Reset to `null` whenever a
  // new page fetch begins (see the preview effect below) so a stale size never positions a new
  // page's boxes for a render or two before the new image reports in.
  const [naturalSize, setNaturalSize] = useState<{ width: number; height: number } | null>(null);

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

      if (clauseParam !== null && document.contractId !== null) {
        const contractResult = await apiClient.getContract360(workspace.id, document.contractId);
        if (contractResult.ok && contractResult.contract !== null) {
          clauses = contractResult.contract.tabs.clauses;
        } else {
          clauses = [];
        }
      }

      setDocumentLoad({ phase: "ready", document, clauses });
    });
  }, [apiClient, workspace?.id, documentId, clauseParam]);

  useEffect(() => {
    loadDocument();
  }, [loadDocument, reloadNonce]);

  const evidenceContractId = documentLoad.phase === "ready" ? documentLoad.document.contractId : null;

  useEffect(() => {
    if (!workspace || evidenceContractId === null) {
      setEvidence([]);
      return;
    }

    let cancelled = false;
    // `Promise.resolve(...)` rather than a bare `.then`: `getContractEvidence` is typed to always
    // return a `Promise`, but this call must degrade to "no boxes" rather than throw even if a
    // caller (a test double, or a future refactor) ever hands back a bare value instead of one.
    void Promise.resolve(apiClient.getContractEvidence(workspace.id, evidenceContractId)).then((result) => {
      if (cancelled) return;
      setEvidence(result?.ok && result.evidence ? result.evidence : []);
    });

    return () => {
      cancelled = true;
    };
  }, [apiClient, workspace?.id, evidenceContractId]);

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
    // A new page fetch invalidates the previous page's natural size (task E23/F04/US01/T01):
    // BoxOverlay must not scale this page's boxes against the last page's image dimensions for the
    // one render before the new <img> reports its own onLoad.
    setNaturalSize(null);

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
          <button type="button" className="btn btn-secondary" onClick={() => onPageChange(FIRST_PAGE, clauseParam)}>
            Go to page 1
          </button>
        </div>
      </div>
    );
  }

  const citation: CitationResolution = surface.citation;
  // Task E23/F04/US01/T01 (NW-63r): every field whose evidence sits on the page on screen, not
  // only the field a `?clause=` citation happened to name -- a page can carry more than one cited
  // phrase (screens-v2.md:95-112). `documentId` is narrowed to `string` by the guard clause above.
  const pageBoxes: readonly PageBoxSpec[] = selectPageBoxes(evidence, documentId, surface.page);
  const canPrev = surface.page > FIRST_PAGE;
  const canNext = documentLoad.document.pageCount !== null && surface.page < documentLoad.document.pageCount;
  const onPrev = () => onPageChange(surface.page - 1, resolvedClauseId);
  const onNext = () => onPageChange(surface.page + 1, resolvedClauseId);

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

      {previewLoad.phase === "reaped" ? (
        <div className="empty-state" role="status">
          <h3>{REAPED_PAGE_HEADING}</h3>
          <p className="micro-meta">{REAPED_PAGE_COPY}</p>
          <button type="button" className="btn btn-secondary" onClick={() => setReloadNonce((n) => n + 1)}>
            Reload the document
          </button>
        </div>
      ) : (
        <PageCanvas
          preview={previewLoad}
          onRetry={() => setReloadNonce((n) => n + 1)}
          boxes={pageBoxes}
          naturalSize={naturalSize}
          onImageLoad={setNaturalSize}
        />
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
  boxes,
  naturalSize,
  onImageLoad,
}: {
  preview: PreviewLoad;
  onRetry: () => void;
  /** Task E23/F04/US01/T01 (NW-63r): boxes to draw over this page's image, already narrowed to
   * this document + this page (see `index.tsx`'s own `pageBoxes`). */
  boxes: readonly PageBoxSpec[];
  naturalSize: { width: number; height: number } | null;
  onImageLoad: (size: { width: number; height: number }) => void;
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
        <img
          src={preview.objectUrl}
          alt="Document page"
          onLoad={(event) => {
            const image = event.currentTarget;
            onImageLoad({ width: image.naturalWidth, height: image.naturalHeight });
          }}
        />
        <BoxOverlay boxes={boxes} naturalWidth={naturalSize?.width ?? 0} naturalHeight={naturalSize?.height ?? 0} />
      </div>
    );
  }

  return (
    <div className="document-viewer-canvas" role="status" aria-live="polite">
      <div className="skeleton" />
    </div>
  );
}

/**
 * Route `/documents/:documentId/viewer?page=&clause=` (ADR-018 w17 clause 13;
 * ADR-029 clauses 4–7; task E22/F03/US01/T01, NW-63 w17 half). Citation-reached
 * deep link: the current page lives in the URL. In-app CTAs prefer
 * `DocumentViewerProvider`'s overlay on the current screen instead.
 */
export default function DocumentViewerRoute({ apiClient }: DocumentViewerRouteProps) {
  const { documentId } = useParams<{ documentId: string }>();
  const [searchParams, setSearchParams] = useSearchParams();

  const writePage = (nextPage: number, clauseId: string | null) => {
    const next = new URLSearchParams();
    next.set("page", String(nextPage));
    if (clauseId !== null) next.set("clause", clauseId);
    setSearchParams(next);
  };

  return (
    <DocumentViewer
      apiClient={apiClient}
      documentId={documentId}
      pageParam={searchParams.get("page")}
      clauseParam={searchParams.get("clause")}
      onPageChange={writePage}
    />
  );
}
