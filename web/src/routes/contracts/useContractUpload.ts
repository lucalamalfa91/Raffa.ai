import { useCallback, useState, useSyncExternalStore } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import { useFixtureSession } from "../../fixtures/FixtureSessionContext";
import { CONTRACTS_CHANGED_EVENT, useTenantId } from "./contracts4";

/** The route that starts an upload from a plain link (Guida Raffa's CTA, Oggi's "Punto zero"). */
export const NEW_UPLOAD_PATH = "/contracts/review/new";

export const reviewPath = (documentId: string): string => `/contracts/review/${encodeURIComponent(documentId)}`;

/** Files sent to the API at the same time during a bulk upload. */
export const UPLOAD_CONCURRENCY = 4;


/**
 * Fixture mode: uploads the sample contract (`POST /api/documents/sample`) and returns the
 * Revisione path of its document; null when it failed.
 */
export async function startSampleUpload(apiClient: ApiClient, tenantId: string): Promise<string | null> {
  const result = await apiClient.uploadSampleDocument(tenantId);
  return result.ok && result.data ? reviewPath(result.data.documentId) : null;
}

/** The demo contract bundled with the web app (`public/samples`), uploaded like any real file. */
export const DEMO_CONTRACT_URL = "/samples/Demo_SaaS_MSA_2024.pdf";
export const DEMO_CONTRACT_NAME = "Demo_SaaS_MSA_2024.pdf";

/** A bulk upload in flight (or just finished): what the Contratti banner reports. */
export interface BulkUploadProgress {
  total: number;
  /** Files the API answered (uploaded, already present or failed). */
  done: number;
  uploaded: number;
  duplicates: number;
  failed: string[];
  finished: boolean;
}

/** One bulk upload at a time, shared by every screen (the Contratti banner reads it wherever it started). */
let bulkState: BulkUploadProgress | null = null;
const bulkListeners = new Set<() => void>();
function setBulkState(update: (b: BulkUploadProgress | null) => BulkUploadProgress | null): void {
  bulkState = update(bulkState);
  bulkListeners.forEach((l) => l());
}
const subscribeBulk = (listener: () => void) => {
  bulkListeners.add(listener);
  return () => bulkListeners.delete(listener);
};

export interface ContractUpload {
  /** Starts an upload: the sample in fixture mode, else the file picker (one or many files). */
  start: () => void;
  /** Always the file picker, fixture mode or not: the user's own contracts. */
  pickFiles: () => void;
  /** The demo contract: the scripted sample in fixture mode, else the bundled demo PDF uploaded for real. */
  startDemo: () => void;
  busy: boolean;
  error: string | null;
  bulk: BulkUploadProgress | null;
  dismissBulk: () => void;
}

/** Runs `worker` over `items` with at most `limit` calls in flight. */
export async function runPool<T>(items: readonly T[], limit: number, worker: (item: T) => Promise<void>): Promise<void> {
  let next = 0;
  const lane = async () => {
    while (next < items.length) {
      const item = items[next++];
      await worker(item);
    }
  };
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, lane));
}

/**
 * "Carica il primo contratto" / "Carica un contratto": one entry point for every screen. In fixture
 * mode it uploads the scripted sample (the prototype's `upload()`). Otherwise the picker accepts
 * many files: one file opens its reading (Revisione); several stay on Contratti, upload
 * {@link UPLOAD_CONCURRENCY} at a time and report progress, then the banner leads to the documents
 * to verify, one at a time. A file already uploaded (same hash) is counted, never uploaded twice.
 */
export function useContractUpload(apiClient: ApiClient): ContractUpload {
  const fixture = useFixtureSession();
  const tenantId = useTenantId();
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const bulk = useSyncExternalStore(subscribeBulk, () => bulkState, () => bulkState);
  const setBulk = setBulkState;

  const uploadMany = useCallback(
    async (files: File[]) => {
      if (!tenantId) return;
      setBusy(true);
      setBulk(() => ({ total: files.length, done: 0, uploaded: 0, duplicates: 0, failed: [], finished: false }));
      navigate("/contracts");
      await runPool(files, UPLOAD_CONCURRENCY, async (file) => {
        const result = await apiClient.uploadDocument(tenantId, file);
        setBulk((b) =>
          b === null
            ? b
            : {
                ...b,
                done: b.done + 1,
                uploaded: b.uploaded + (result.ok && result.document && !result.alreadyUploaded ? 1 : 0),
                duplicates: b.duplicates + (result.alreadyUploaded ? 1 : 0),
                failed: result.ok || result.alreadyUploaded ? b.failed : [...b.failed, file.name],
              },
        );
        window.dispatchEvent(new Event(CONTRACTS_CHANGED_EVENT));
      });
      setBulk((b) => (b === null ? b : { ...b, finished: true }));
      setBusy(false);
      window.dispatchEvent(new Event(CONTRACTS_CHANGED_EVENT));
    },
    [apiClient, tenantId, navigate],
  );

  const uploadOne = useCallback(
    async (file: File) => {
      if (!tenantId) return;
      setBusy(true);
      const result = await apiClient.uploadDocument(tenantId, file);
      setBusy(false);
      if (result.ok && result.document) navigate(reviewPath(result.document.id));
      else if (result.alreadyUploaded) navigate("/contracts");
      else setError(result.error ?? "The upload did not go through.");
    },
    [apiClient, tenantId, navigate],
  );

  const pickFiles = useCallback(() => {
    if (!tenantId || busy) return;
    setError(null);
    const input = document.createElement("input");
    input.type = "file";
    input.multiple = true;
    input.accept = ".pdf,.docx,.doc,.png,.jpg,.jpeg,.tif,.tiff";
    input.onchange = () => {
      const files = Array.from(input.files ?? []);
      if (files.length === 0) return;
      if (files.length > 1) void uploadMany(files);
      else void uploadOne(files[0]);
    };
    input.click();
  }, [tenantId, busy, uploadMany, uploadOne]);

  const startDemo = useCallback(() => {
    if (!tenantId || busy) return;
    setError(null);
    setBusy(true);
    if (fixture) {
      void startSampleUpload(apiClient, tenantId).then((path) => {
        setBusy(false);
        if (path) navigate(path);
        else setError("The sample contract could not be uploaded.");
      });
      return;
    }
    void fetch(DEMO_CONTRACT_URL)
      .then((r) => (r.ok ? r.blob() : Promise.reject(new Error(String(r.status)))))
      .then((blob) => uploadOne(new File([blob], DEMO_CONTRACT_NAME, { type: "application/pdf" })))
      .catch(() => {
        setBusy(false);
        setError("The sample contract could not be uploaded.");
      });
  }, [apiClient, tenantId, fixture, busy, navigate, uploadOne]);

  /** Fixture mode keeps the scripted sample behind the generic "Upload" entry points. */
  const start = useCallback(() => (fixture ? startDemo() : pickFiles()), [fixture, startDemo, pickFiles]);

  return { start, pickFiles, startDemo, busy: busy || (bulk !== null && !bulk.finished), error, bulk, dismissBulk: () => setBulk(() => null) };
}
