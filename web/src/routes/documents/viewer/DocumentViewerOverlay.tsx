import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type MouseEvent as ReactMouseEvent,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";
import { useNavigate } from "react-router-dom";
import type { ApiClient } from "../../../api/client";
import { DocumentViewer } from "./index";
import {
  parseDocumentViewerHref,
  type DocumentViewerTarget,
} from "./documentViewerViewModel";

const FOCUSABLE_SELECTOR =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export interface DocumentViewerOverlayValue {
  open: (target: DocumentViewerTarget) => void;
  close: () => void;
  openHref: (href: string) => boolean;
  target: DocumentViewerTarget | null;
}

const DocumentViewerOverlayContext = createContext<DocumentViewerOverlayValue | null>(null);

export function useDocumentViewerOverlay(): DocumentViewerOverlayValue | null {
  return useContext(DocumentViewerOverlayContext);
}

export interface DocumentViewerProviderProps {
  apiClient: ApiClient;
  children: ReactNode;
}

/**
 * Shell-level overlay for the document viewer. In-app CTAs open this dialog on the current
 * route (Ask conversation, Documents list, Contract 360, Review) so closing restores the
 * exact screen with no lost state. The `/documents/:documentId/viewer` route stays registered
 * for deep links and open-in-new-tab.
 */
export function DocumentViewerProvider({ apiClient, children }: DocumentViewerProviderProps) {
  const [target, setTarget] = useState<DocumentViewerTarget | null>(null);
  const open = useCallback((next: DocumentViewerTarget) => setTarget(next), []);
  const close = useCallback(() => setTarget(null), []);
  const openHref = useCallback((href: string) => {
    const parsed = parseDocumentViewerHref(href);
    if (parsed === null) return false;
    setTarget(parsed);
    return true;
  }, []);
  const value = useMemo(() => ({ open, close, openHref, target }), [open, close, openHref, target]);

  return (
    <DocumentViewerOverlayContext.Provider value={value}>
      {children}
      {target !== null && (
        <DocumentViewerDialog
          apiClient={apiClient}
          documentId={target.documentId}
          pageParam={target.page}
          clauseParam={target.clause}
          onPageChange={(page, clauseId) =>
            setTarget({ documentId: target.documentId, page: String(page), clause: clauseId })
          }
          onClose={close}
        />
      )}
    </DocumentViewerOverlayContext.Provider>
  );
}

interface DocumentViewerDialogProps {
  apiClient: ApiClient;
  documentId: string;
  pageParam: string | null;
  clauseParam: string | null;
  onPageChange: (nextPage: number, clauseId: string | null) => void;
  onClose: () => void;
}

function DocumentViewerDialog({
  apiClient,
  documentId,
  pageParam,
  clauseParam,
  onPageChange,
  onClose,
}: DocumentViewerDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const previouslyFocused = useRef<HTMLElement | null>(null);

  useEffect(() => {
    previouslyFocused.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previouslyOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    const dialog = dialogRef.current;
    const initial = dialog?.querySelector<HTMLElement>("[data-document-viewer-close]") ?? dialog;
    initial?.focus();

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
        return;
      }
      if (event.key !== "Tab" || dialog === null) return;
      const focusable = Array.from(dialog.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR));
      if (focusable.length === 0) {
        event.preventDefault();
        dialog.focus();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = previouslyOverflow;
      previouslyFocused.current?.focus();
    };
  }, [onClose]);

  const handleBackdropMouseDown = (event: ReactMouseEvent<HTMLDivElement>) => {
    if (event.target === event.currentTarget) onClose();
  };

  return createPortal(
    <div className="document-viewer-dialog-backdrop" onMouseDown={handleBackdropMouseDown} data-testid="document-viewer-dialog-backdrop">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-label="Document viewer"
        className="document-viewer-dialog"
        tabIndex={-1}
        data-testid="document-viewer-dialog"
      >
        <div className="document-viewer-dialog-bar">
          <button
            type="button"
            className="btn btn-ghost"
            onClick={onClose}
            aria-label="Close"
            data-document-viewer-close=""
          >
            Close
          </button>
        </div>
        <DocumentViewer
          apiClient={apiClient}
          documentId={documentId}
          pageParam={pageParam}
          clauseParam={clauseParam}
          onPageChange={onPageChange}
        />
      </div>
    </div>,
    document.body,
  );
}

export interface DocumentViewerLinkProps {
  to: string;
  className?: string;
  children: ReactNode;
  onClick?: (event: ReactMouseEvent<HTMLAnchorElement>) => void;
}

/**
 * Native `<a href>` so middle-click / ctrl-click still open the deep-link route in a new tab.
 * An unmodified click opens the overlay when a provider is mounted, otherwise navigates.
 */
export function DocumentViewerLink({ to, className, children, onClick }: DocumentViewerLinkProps) {
  const overlay = useDocumentViewerOverlay();
  const navigate = useNavigate();

  const handleClick = (event: ReactMouseEvent<HTMLAnchorElement>) => {
    onClick?.(event);
    if (event.defaultPrevented) return;
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    const target = parseDocumentViewerHref(to);
    if (target === null) return;
    event.preventDefault();
    if (overlay !== null) overlay.open(target);
    else navigate(to);
  };

  return (
    <a href={to} className={className} onClick={handleClick}>
      {children}
    </a>
  );
}
