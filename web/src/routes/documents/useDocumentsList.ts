import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ApiClient, DocumentListItemBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import {
  runUploadBatch,
  MAX_FILES_PER_BATCH,
  type LocalUploadEntry,
  type RejectedFileOutcome,
} from "./uploadPipeline";
import { filterDocumentsByAttention, type AttentionFilterValue } from "./documentTable";

/** R-DOC-09: "polled every 2 s until terminal." */
const POLL_INTERVAL_MS = 2000;

/** Same 100-row fetch-once ceiling `getPortfolio`'s own callers already use (`PortfolioPageRequest
 * .MaxPageSize`) -- Documents V2 buckets attention/all client-side (`documentTable.ts`), not via a
 * server round trip per filter click, the same architecture Portfolio already established. */
const LIST_PAGE_SIZE = 100;

export type DocumentsFetchState = "loading" | "error" | "ready";

export interface UseDocumentsListResult {
  /** False only when no workspace is current (should not normally be reachable -- see `index.tsx`). */
  hasWorkspace: boolean;
  fetchState: DocumentsFetchState;
  errorMessage: string | null;
  reload: () => void;
  /** The tenant's whole list, unfiltered, newest-first (server order). */
  documents: readonly DocumentListItemBody[];
  filter: AttentionFilterValue;
  setFilter: (value: AttentionFilterValue) => void;
  filteredDocuments: readonly DocumentListItemBody[];
  attentionCount: number;
  allCount: number;
  /** Files picked/dropped this session, not yet a real server document (R-DOC-01 AC-1: a row from
   * the moment it is picked) or that failed before one could be created. */
  localUploads: readonly LocalUploadEntry[];
  /** R-DOC-04: rejected files, this session only, never counted above. */
  rejected: readonly RejectedFileOutcome[];
  dismissRejected: (key: string) => void;
  /** Starts uploading every file in `files` (capped at `MAX_FILES_PER_BATCH`, <= 3 in flight). */
  uploadFiles: (files: File[]) => void;
  /** Re-submits a `localUploads` entry's own file (phase `"failed"` only). */
  retryLocalUpload: (key: string) => void;
  /** Retries a document the server already knows about and reports `Failed` (no local `File` to
   * resubmit -- calls `POST /api/documents/{id}/reprocess`, Admin only; a non-Admin sees the
   * server's own 403 message, the same "let the server be authoritative" posture every write call
   * in this app already follows). */
  retryServerDocument: (documentId: string) => void;
  retryError: string | null;
}

function dedupeNewestFirst(entries: readonly LocalUploadEntry[]): LocalUploadEntry[] {
  return [...entries];
}

export function useDocumentsList(apiClient: ApiClient): UseDocumentsListResult {
  const workspace = loadCurrentWorkspace();
  const [fetchState, setFetchState] = useState<DocumentsFetchState>("loading");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [documents, setDocuments] = useState<readonly DocumentListItemBody[]>([]);
  const [filter, setFilter] = useState<AttentionFilterValue>("attention");
  const [localUploads, setLocalUploads] = useState<readonly LocalUploadEntry[]>([]);
  const [rejected, setRejected] = useState<readonly RejectedFileOutcome[]>([]);
  const [retryError, setRetryError] = useState<string | null>(null);

  const mountedRef = useRef(true);
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(
    () => () => {
      mountedRef.current = false;
    },
    [],
  );

  const load = useCallback(() => {
    if (!workspace) return;
    void apiClient.listDocuments(workspace.id, { pageSize: LIST_PAGE_SIZE }).then((result) => {
      if (!mountedRef.current) return;
      if (!result.ok || !result.page) {
        setFetchState("error");
        setErrorMessage(
          result.statusCode === 503 || result.statusCode === null
            ? "Raffa's document service is temporarily unavailable. Try again in a moment."
            : (result.error ?? "The document list could not be loaded."),
        );
        return;
      }
      setDocuments(result.page.items);
      setFetchState("ready");
      setErrorMessage(null);
    });
    // workspace?.id (a primitive), not workspace itself -- loadCurrentWorkspace() returns a fresh
    // object every call, the same convention every other route's own load() callback follows.
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    load();
  }, [load]);

  // R-DOC-09: poll every 2s while at least one row is non-terminal (Uploaded/Processing). `hasWork`
  // is a plain boolean (not the `documents` array itself) so this effect only restarts the interval
  // when that boolean actually flips, never on every unrelated render.
  const hasNonTerminalRow = useMemo(
    () => documents.some((item) => item.processingStatus === "Uploaded" || item.processingStatus === "Processing"),
    [documents],
  );

  useEffect(() => {
    if (!hasNonTerminalRow || !workspace) return;
    pollRef.current = setInterval(load, POLL_INTERVAL_MS);
    return () => {
      if (pollRef.current !== null) {
        clearInterval(pollRef.current);
        pollRef.current = null;
      }
    };
  }, [hasNonTerminalRow, workspace?.id, load]);

  const filteredDocuments = useMemo(() => filterDocumentsByAttention(documents, filter), [documents, filter]);
  const attentionCount = useMemo(() => filterDocumentsByAttention(documents, "attention").length, [documents]);
  const allCount = documents.length;

  const uploadFiles = useCallback(
    (files: File[]) => {
      if (!workspace || files.length === 0) return;
      const capped = files.slice(0, MAX_FILES_PER_BATCH);
      const entries = capped.map((file) => ({ key: crypto.randomUUID(), file }));

      // R-DOC-01 AC-1: a row exists the moment the file is picked, before the request even starts.
      setLocalUploads((current) =>
        dedupeNewestFirst([...entries.map((entry) => ({ ...entry, phase: "uploading" as const })), ...current]),
      );

      void runUploadBatch(entries, apiClient, workspace.id, (outcome) => {
        if (!mountedRef.current) return;

        if (outcome.kind === "admitted") {
          setLocalUploads((current) => current.filter((entry) => entry.key !== outcome.key));
          load(); // brings the real row in from the server list (also (re)starts polling if needed)
          return;
        }

        if (outcome.kind === "rejected") {
          setLocalUploads((current) => current.filter((entry) => entry.key !== outcome.key));
          setRejected((current) => [{ key: outcome.key, fileName: outcome.fileName, message: outcome.message }, ...current]);
          return;
        }

        // "failed": keep the entry (with its own File) so retryLocalUpload can resubmit it.
        setLocalUploads((current) =>
          current.map((entry) =>
            entry.key === outcome.key ? { ...entry, phase: "failed" as const, errorMessage: outcome.message } : entry,
          ),
        );
      });
    },
    [apiClient, workspace?.id, load],
  );

  const retryLocalUpload = useCallback(
    (key: string) => {
      if (!workspace) return;
      const entry = localUploads.find((candidate) => candidate.key === key);
      if (!entry) return;
      setLocalUploads((current) => current.map((e) => (e.key === key ? { ...e, phase: "uploading" as const } : e)));

      void runUploadBatch([{ key: entry.key, file: entry.file }], apiClient, workspace.id, (outcome) => {
        if (!mountedRef.current) return;
        if (outcome.kind === "admitted") {
          setLocalUploads((current) => current.filter((e) => e.key !== outcome.key));
          load();
          return;
        }
        if (outcome.kind === "rejected") {
          setLocalUploads((current) => current.filter((e) => e.key !== outcome.key));
          setRejected((current) => [{ key: outcome.key, fileName: outcome.fileName, message: outcome.message }, ...current]);
          return;
        }
        setLocalUploads((current) =>
          current.map((e) => (e.key === outcome.key ? { ...e, phase: "failed" as const, errorMessage: outcome.message } : e)),
        );
      });
    },
    [apiClient, workspace?.id, localUploads, load],
  );

  const retryServerDocument = useCallback(
    (documentId: string) => {
      if (!workspace) return;
      setRetryError(null);
      void apiClient.reprocessDocument(workspace.id, documentId).then((result) => {
        if (!mountedRef.current) return;
        if (!result.ok) {
          setRetryError(result.error ?? "Raffa could not reprocess this document.");
          return;
        }
        load();
      });
    },
    [apiClient, workspace?.id, load],
  );

  const dismissRejected = useCallback((key: string) => {
    setRejected((current) => current.filter((entry) => entry.key !== key));
  }, []);

  return {
    hasWorkspace: workspace !== null,
    fetchState,
    errorMessage,
    reload: load,
    documents,
    filter,
    setFilter,
    filteredDocuments,
    attentionCount,
    allCount,
    localUploads,
    rejected,
    dismissRejected,
    uploadFiles,
    retryLocalUpload,
    retryServerDocument,
    retryError,
  };
}
