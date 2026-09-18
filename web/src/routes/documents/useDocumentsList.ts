import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ApiClient, DocumentListItemBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { POLL_INTERVAL_MS, POLL_NO_CHANGE_BUDGET_MS, usePollBudget } from "../../components/shell/usePollBudget";
import { runUploadBatch, MAX_FILES_PER_BATCH, type LocalUploadEntry } from "./uploadPipeline";
import {
  filterDocumentsByAttention,
  STUCK_REPROCESS_AFTER_MS,
  STUCK_PROCESSING_REPROCESS_AFTER_MS,
  MAX_STUCK_REPROCESS_ATTEMPTS,
  type AttentionFilterValue,
  type DocumentCountsBody,
} from "./documentTable";

// R-DOC-09's cadence and ADR-012 w15 §17's no-change budget live in the shared hook every "not
// ready yet" surface polls through (`usePollBudget.ts`); re-exported so this file stays the one
// place a reader of the Documents screen looks for them.
export { POLL_INTERVAL_MS, POLL_NO_CHANGE_BUDGET_MS };

/** Same 100-row fetch-once ceiling `getPortfolio`'s own callers already use (`PortfolioPageRequest
 * .MaxPageSize`) -- Documents V2 buckets attention/all client-side (`documentTable.ts`), not via a
 * server round trip per filter click, the same architecture Portfolio already established. The
 * *numbers* are never this page's, though: every count is the server's `counts` (ADR-012 w15 §4). */
const LIST_PAGE_SIZE = 100;

/** Five zeros, present-and-zero -- the server's own shape before the first fetch resolves. */
export const ZERO_COUNTS: DocumentCountsBody = { all: 0, needsAttention: 0, needsReview: 0, processing: 0, rejected: 0 };

export type DocumentsFetchState = "loading" | "error" | "ready";

export interface UseDocumentsListResult {
  /** False only when no workspace is current (should not normally be reachable -- see `index.tsx`). */
  hasWorkspace: boolean;
  fetchState: DocumentsFetchState;
  errorMessage: string | null;
  reload: () => void;
  /** The tenant's first page, unfiltered, newest-first (server order) -- `Rejected` rows included,
   * which is why every reader goes through `filteredDocuments`, never this array's length. */
  documents: readonly DocumentListItemBody[];
  /** ADR-027 §D7: the server's tenant-wide counts -- the chips, the rail badge and the summary line
   * read these, never the fetched page (ADR-012 w15 §4). */
  counts: DocumentCountsBody;
  filter: AttentionFilterValue;
  setFilter: (value: AttentionFilterValue) => void;
  /** The rows for the current chip: a client bucket of `documents` for attention/all, the server's
   * own `status=Rejected` bucket for the third chip (ADR-012 w15 §13.5b). */
  filteredDocuments: readonly DocumentListItemBody[];
  /** Files picked/dropped this session that the server list does not carry yet (R-DOC-01 AC-1: a row
   * from the moment it is picked), that failed before a server row could be created, or that were
   * refused before storage (oversize, 413, 415 -- ADR-020 w15 §6's local "Not added" row). */
  localUploads: readonly LocalUploadEntry[];
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
  /** ADR-012 w15 §17 / ADR-020 w15 §8: the 2 s poll stopped after five minutes without a change.
   * Rows stay exactly as the server last reported them; `resumeUpdates` is "Check again". */
  updatesPaused: boolean;
  resumeUpdates: () => void;
}

/** What "changed" means for the poll budget: a row appearing, leaving, or moving status/stage, or a
 * count moving -- the server's last answer, reduced to a string. */
function fingerprintOf(documents: readonly DocumentListItemBody[], counts: DocumentCountsBody): string {
  const rows = documents.map((item) => `${item.id}:${item.processingStatus}:${item.stage ?? ""}`).join("|");
  return `${rows}#${counts.all}/${counts.needsAttention}/${counts.needsReview}/${counts.processing}/${counts.rejected}`;
}

export function useDocumentsList(apiClient: ApiClient): UseDocumentsListResult {
  const workspace = loadCurrentWorkspace();
  const [fetchState, setFetchState] = useState<DocumentsFetchState>("loading");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [documents, setDocuments] = useState<readonly DocumentListItemBody[]>([]);
  const [counts, setCounts] = useState<DocumentCountsBody>(ZERO_COUNTS);
  const [rejectedDocuments, setRejectedDocuments] = useState<readonly DocumentListItemBody[]>([]);
  const [filter, setFilterState] = useState<AttentionFilterValue>("attention");
  const [localUploads, setLocalUploads] = useState<readonly LocalUploadEntry[]>([]);
  const [retryError, setRetryError] = useState<string | null>(null);

  const mountedRef = useRef(true);
  const filterRef = useRef<AttentionFilterValue>("attention");

  useEffect(
    () => () => {
      mountedRef.current = false;
    },
    [],
  );

  // The third chip's rows are the server's own bucket (`status=Rejected`), fetched only while that
  // chip is selected -- `counts.rejected` is what says whether the chip exists at all.
  const loadRejected = useCallback(() => {
    if (!workspace) return;
    void apiClient.listDocuments(workspace.id, { status: "Rejected", pageSize: LIST_PAGE_SIZE }).then((result) => {
      if (!mountedRef.current) return;
      if (result.ok && result.page) setRejectedDocuments(result.page.items);
    });
  }, [apiClient, workspace?.id]);

  const load = useCallback(() => {
    if (!workspace) return;
    void apiClient.listDocuments(workspace.id, { pageSize: LIST_PAGE_SIZE }).then((result) => {
      if (!mountedRef.current) return;
      if (!result.ok || !result.page) {
        setFetchState("error");
        setErrorMessage(
          result.statusCode === 503 || result.statusCode === null
            ? "Raffa.ai's document service is temporarily unavailable. Try again in a moment."
            : (result.error ?? "The document list could not be loaded."),
        );
        return;
      }
      setDocuments(result.page.items);
      setCounts(result.page.counts);
      setFetchState("ready");
      setErrorMessage(null);
    });
    if (filterRef.current === "rejected") loadRejected();
    // workspace?.id (a primitive), not workspace itself -- loadCurrentWorkspace() returns a fresh
    // object every call, the same convention every other route's own load() callback follows.
  }, [apiClient, workspace?.id, loadRejected]);

  useEffect(() => {
    load();
  }, [load]);

  const setFilter = useCallback(
    (value: AttentionFilterValue) => {
      filterRef.current = value;
      setFilterState(value);
      if (value === "rejected") loadRejected();
    },
    [loadRejected],
  );

  // The chip disappears with its last row (an Admin deleted it): fall back to the default filter
  // rather than showing an empty bucket under a chip that no longer renders.
  useEffect(() => {
    if (filter === "rejected" && fetchState === "ready" && counts.rejected === 0) {
      filterRef.current = "attention";
      setFilterState("attention");
    }
  }, [filter, fetchState, counts.rejected]);

  // R-DOC-09: poll every 2 s while at least one row is non-terminal (Uploaded/Processing). The
  // predicate reads the *server* array (ADR-012 w15 §5: unchanged, and deliberately so); the budget
  // gates the interval, never the row's meaning (§17).
  const hasNonTerminalRow = useMemo(
    () => documents.some((item) => item.processingStatus === "Uploaded" || item.processingStatus === "Processing"),
    [documents],
  );
  const fingerprint = useMemo(() => fingerprintOf(documents, counts), [documents, counts]);
  const { paused: updatesPaused, resume: resumeUpdates } = usePollBudget({
    active: hasNonTerminalRow && workspace !== null,
    fingerprint,
    onTick: load,
  });

  // ADR-012 w15 §5: the optimistic row is handed off on evidence, never on a timer -- a local entry
  // that carries a server id is dropped only once the server list actually carries that id. If a
  // load fails, the row stays.
  useEffect(() => {
    if (documents.length === 0) return;
    const serverIds = new Set(documents.map((item) => item.id));
    setLocalUploads((current) =>
      current.some((entry) => entry.serverId !== undefined && serverIds.has(entry.serverId))
        ? current.filter((entry) => entry.serverId === undefined || !serverIds.has(entry.serverId))
        : current,
    );
  }, [documents]);

  const filteredDocuments = useMemo(
    () => (filter === "rejected" ? rejectedDocuments : filterDocumentsByAttention(documents, filter)),
    [documents, rejectedDocuments, filter],
  );

  const settle = useCallback(
    (outcome: Parameters<Parameters<typeof runUploadBatch>[3]>[0]) => {
      if (!mountedRef.current) return;

      if (outcome.kind === "admitted") {
        // Stored (201): keep the row, remember the server id, and bring the real row in from the
        // server list (which also (re)starts polling if needed) -- the local row leaves only when
        // that row is present (see the effect above).
        setLocalUploads((current) =>
          current.map((entry) => (entry.key === outcome.key ? { ...entry, phase: "uploading" as const, serverId: outcome.document.id } : entry)),
        );
        load();
        return;
      }

      if (outcome.kind === "rejected") {
        // Refused before storage (oversize, 413, 415): a local "Not added" row with the designed
        // sentence, no next step, no dismiss -- a reload clears it (ADR-020 w15 §6).
        setLocalUploads((current) =>
          current.map((entry) => (entry.key === outcome.key ? { ...entry, phase: "rejected" as const, errorMessage: outcome.message } : entry)),
        );
        return;
      }

      // "failed": keep the entry (with its own File) so retryLocalUpload can resubmit it.
      setLocalUploads((current) =>
        current.map((entry) => (entry.key === outcome.key ? { ...entry, phase: "failed" as const, errorMessage: outcome.message } : entry)),
      );
    },
    [load],
  );

  const uploadFiles = useCallback(
    (files: File[]) => {
      if (!workspace || files.length === 0) return;
      const capped = files.slice(0, MAX_FILES_PER_BATCH);
      const entries = capped.map((file) => ({ key: crypto.randomUUID(), file }));

      // R-DOC-01 AC-1: a row exists the moment the file is picked, before the request even starts.
      setLocalUploads((current) => [...entries.map((entry) => ({ ...entry, phase: "uploading" as const })), ...current]);

      void runUploadBatch(entries, apiClient, workspace.id, settle);
    },
    [apiClient, workspace?.id, settle],
  );

  const retryLocalUpload = useCallback(
    (key: string) => {
      if (!workspace) return;
      const entry = localUploads.find((candidate) => candidate.key === key);
      if (!entry) return;
      setLocalUploads((current) =>
        current.map((e) => (e.key === key ? { ...e, phase: "uploading" as const, errorMessage: undefined } : e)),
      );

      void runUploadBatch([{ key: entry.key, file: entry.file }], apiClient, workspace.id, settle);
    },
    [apiClient, workspace?.id, localUploads, settle],
  );

  const retryServerDocument = useCallback(
    (documentId: string) => {
      if (!workspace) return;
      setRetryError(null);
      void apiClient.reprocessDocument(workspace.id, documentId).then((result) => {
        if (!mountedRef.current) return;
        if (!result.ok) {
          setRetryError(result.error ?? "Raffa.ai could not reprocess this document.");
          return;
        }
        load();
      });
    },
    [apiClient, workspace?.id, load],
  );

  // Auto-reprocess stuck Uploaded (never claimed) and hung Processing (claimed, then silent)
  // rows through POST /api/documents/{id}/reprocess. Tenant-scoped via workspace.id. Capped at
  // MAX_STUCK_REPROCESS_ATTEMPTS so a permanently bad file cannot loop. Failures stay silent —
  // a 403 (Procurement) must not paint a banner the user did not ask for; the API list recovery
  // still unsticks the row for every role.
  const autoReprocessCountRef = useRef(new Map<string, number>());
  const autoReprocessTimersRef = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  const processingStageSeenRef = useRef(new Map<string, { stage: string | null; since: number }>());

  useEffect(() => {
    autoReprocessCountRef.current.clear();
    processingStageSeenRef.current.clear();
    for (const timer of autoReprocessTimersRef.current.values()) clearTimeout(timer);
    autoReprocessTimersRef.current.clear();
  }, [workspace?.id]);

  useEffect(() => {
    const tenantId = workspace?.id;
    if (!tenantId) return;

    const now = Date.now();
    for (const item of documents) {
      const count = autoReprocessCountRef.current.get(item.id) ?? 0;
      if (count >= MAX_STUCK_REPROCESS_ATTEMPTS) continue;
      if (autoReprocessTimersRef.current.has(item.id)) continue;

      let delay: number | null = null;
      if (item.processingStatus === "Uploaded") {
        const created = Date.parse(item.createdAt);
        if (!Number.isFinite(created)) continue;
        delay = Math.max(0, STUCK_REPROCESS_AFTER_MS - (now - created));
      } else if (item.processingStatus === "Processing") {
        const stageKey = item.stage ?? "";
        const seen = processingStageSeenRef.current.get(item.id);
        if (!seen || seen.stage !== stageKey) {
          processingStageSeenRef.current.set(item.id, { stage: stageKey, since: now });
          delay = STUCK_PROCESSING_REPROCESS_AFTER_MS;
        } else {
          delay = Math.max(0, STUCK_PROCESSING_REPROCESS_AFTER_MS - (now - seen.since));
        }
      } else {
        processingStageSeenRef.current.delete(item.id);
        continue;
      }

      const documentId = item.id;
      const timer = setTimeout(() => {
        autoReprocessTimersRef.current.delete(documentId);
        if (!mountedRef.current) return;
        const fired = autoReprocessCountRef.current.get(documentId) ?? 0;
        if (fired >= MAX_STUCK_REPROCESS_ATTEMPTS) return;
        autoReprocessCountRef.current.set(documentId, fired + 1);
        void apiClient.reprocessDocument(tenantId, documentId).then((result) => {
          if (!mountedRef.current) return;
          if (result.ok) load();
        });
      }, delay);
      autoReprocessTimersRef.current.set(documentId, timer);
    }

    return () => {
      for (const timer of autoReprocessTimersRef.current.values()) clearTimeout(timer);
      autoReprocessTimersRef.current.clear();
    };
  }, [apiClient, documents, load, workspace?.id]);

  return {
    hasWorkspace: workspace !== null,
    fetchState,
    errorMessage,
    reload: load,
    documents,
    counts,
    filter,
    setFilter,
    filteredDocuments,
    localUploads,
    uploadFiles,
    retryLocalUpload,
    retryServerDocument,
    retryError,
    updatesPaused,
    resumeUpdates,
  };
}
