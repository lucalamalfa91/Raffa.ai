import { useEffect, useState } from "react";
import type { ApiClient } from "../../../api/client";
import { useI18n } from "../../../i18n/I18nProvider";
import { computeBoxRect } from "../../documents/viewer/BoxOverlay";
import { DocumentViewerLink } from "../../documents/viewer/DocumentViewerOverlay";
import { buildDocumentViewerHref } from "../../documents/viewer/documentViewerViewModel";

/** One passage to draw on the page: the field under review, or a passage its value rests on. */
export interface PageHighlight {
  key: string;
  page: number;
  box: { x: number; y: number; width: number; height: number };
  tone: "field" | "source";
}

export interface ReviewPagePaneProps {
  apiClient: ApiClient;
  tenantId: string;
  documentId: string;
  /** The page on screen (1-based). */
  page: number;
  onPage: (page: number) => void;
  highlights: readonly PageHighlight[];
}

type Preview =
  | { phase: "loading" }
  | { phase: "ready"; objectUrl: string }
  | { phase: "missing" };

/**
 * Revisione: the contract page the field was read on, with the quoted words boxed (the field in the
 * accent colour, the passages its value rests on outlined), so the user checks the value against the
 * document itself, not against a bare value. Same page PNG and pixel-space boxes as the document
 * viewer (`GET /api/documents/{id}/preview?page=n`, `BoxOverlay`'s `computeBoxRect`); the full viewer
 * stays one click away. A page without a preview says so: the quoted passage is still beside it.
 */
export default function ReviewPagePane({ apiClient, tenantId, documentId, page, onPage, highlights }: ReviewPagePaneProps) {
  const { t } = useI18n();
  const [preview, setPreview] = useState<Preview>({ phase: "loading" });
  const [natural, setNatural] = useState<{ width: number; height: number } | null>(null);
  const [pageCount, setPageCount] = useState<number | null>(null);

  useEffect(() => {
    let live = true;
    void apiClient.getDocument(tenantId, documentId).then((result) => {
      if (live && result.ok && result.document) setPageCount(result.document.pageCount ?? null);
    });
    return () => {
      live = false;
    };
  }, [apiClient, tenantId, documentId]);

  useEffect(() => {
    let live = true;
    let objectUrl: string | null = null;
    setPreview({ phase: "loading" });
    setNatural(null);
    void apiClient.getDocumentPreviewUrl(tenantId, documentId, page).then((result) => {
      if (result.ok && result.objectUrl) {
        objectUrl = result.objectUrl;
        if (live) setPreview({ phase: "ready", objectUrl });
        else URL.revokeObjectURL(objectUrl);
      } else if (live) {
        setPreview({ phase: "missing" });
      }
    });
    return () => {
      live = false;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [apiClient, tenantId, documentId, page]);

  const onPageBoxes = highlights.filter((h) => h.page === page);
  const otherPages = [...new Set(highlights.filter((h) => h.page !== page).map((h) => h.page))].sort((a, b) => a - b);

  return (
    <section className="c4-pane" aria-label={t("Contract page")}>
      <div className="c4-pane-bar">
        <button type="button" className="btn btn-ghost" disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label={t("Previous page")}>
          ←
        </button>
        <span className="c4-pane-page">
          {pageCount ? t("Page {page} of {total}", { page, total: pageCount }) : t("Page {page}", { page })}
        </span>
        <button
          type="button"
          className="btn btn-ghost"
          disabled={pageCount !== null && page >= pageCount}
          onClick={() => onPage(page + 1)}
          aria-label={t("Next page")}
        >
          →
        </button>
        <DocumentViewerLink to={buildDocumentViewerHref(documentId, page)} className="c4-pane-open">
          {t("Open the whole document")}
        </DocumentViewerLink>
      </div>
      {otherPages.length > 0 ? (
        <div className="c4-pane-jump">
          {t("Also highlighted on")}{" "}
          {otherPages.map((p) => (
            <button key={p} type="button" className="btn btn-ghost c4-pane-jump-btn" onClick={() => onPage(p)}>
              {t("page {page}", { page: p })}
            </button>
          ))}
        </div>
      ) : null}
      <div className="c4-pane-canvas">
        {preview.phase === "ready" ? (
          <>
            <img
              src={preview.objectUrl}
              alt={t("Page {page} of the contract", { page })}
              onLoad={(e) => setNatural({ width: e.currentTarget.naturalWidth, height: e.currentTarget.naturalHeight })}
            />
            {natural ? (
              <div className="c4-pane-boxes" aria-hidden="true">
                {onPageBoxes.map((h) => {
                  const rect = computeBoxRect(h.box, natural.width, natural.height);
                  return rect ? (
                    <div key={h.key} className={`c4-pane-box is-${h.tone}`} data-testid={`review-box-${h.tone}`} style={rect} />
                  ) : null;
                })}
              </div>
            ) : null}
          </>
        ) : preview.phase === "loading" ? (
          <div className="c4-pane-empty" role="status">
            <span className="c4-spinner" aria-hidden="true" /> {t("Loading the page…")}
          </div>
        ) : (
          <div className="c4-pane-empty">{t("The page image is not available: check the quoted passage beside it.")}</div>
        )}
      </div>
    </section>
  );
}
