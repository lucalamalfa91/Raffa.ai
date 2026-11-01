import { useCallback, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import { useFixtureSession } from "../../fixtures/FixtureSessionContext";
import { useTenantId } from "./contracts4";

/** The route that starts an upload from a plain link (Guida Raffa's CTA, Oggi's "Punto zero"). */
export const NEW_UPLOAD_PATH = "/contracts/review/new";

export const reviewPath = (documentId: string): string => `/contracts/review/${encodeURIComponent(documentId)}`;

/**
 * Fixture mode: uploads the sample contract (`POST /api/documents/sample`) and returns the
 * Revisione path of its document; null when it failed.
 */
export async function startSampleUpload(apiClient: ApiClient, tenantId: string): Promise<string | null> {
  const result = await apiClient.uploadSampleDocument(tenantId);
  return result.ok && result.data ? reviewPath(result.data.documentId) : null;
}

export interface ContractUpload {
  /** Starts an upload: the sample in fixture mode, else the file picker. Then opens Revisione. */
  start: () => void;
  busy: boolean;
  error: string | null;
}

/**
 * "Carica il primo contratto" / "Carica un contratto": one entry point for every screen. In fixture
 * mode it uploads the scripted sample (the prototype's `upload()`); otherwise it asks for a file,
 * sends it to `POST /api/documents` and opens its reading. A file already uploaded (same hash) opens
 * Contratti, where its banner leads to it.
 */
export function useContractUpload(apiClient: ApiClient): ContractUpload {
  const fixture = useFixtureSession();
  const tenantId = useTenantId();
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const start = useCallback(() => {
    if (!tenantId || busy) return;
    setError(null);
    if (fixture) {
      setBusy(true);
      void startSampleUpload(apiClient, tenantId).then((path) => {
        setBusy(false);
        if (path) navigate(path);
        else setError("The upload did not go through.");
      });
      return;
    }

    const input = document.createElement("input");
    input.type = "file";
    input.accept = ".pdf,.docx,.doc,.png,.jpg,.jpeg,.tif,.tiff";
    input.onchange = () => {
      const file = input.files?.[0];
      if (!file) return;
      setBusy(true);
      void apiClient.uploadDocument(tenantId, file).then((result) => {
        setBusy(false);
        if (result.ok && result.document) navigate(reviewPath(result.document.id));
        else if (result.alreadyUploaded) navigate("/contracts");
        else setError(result.error ?? "The upload did not go through.");
      });
    };
    input.click();
  }, [apiClient, tenantId, fixture, busy, navigate]);

  return { start, busy, error };
}
