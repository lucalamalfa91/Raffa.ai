import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import UploadDropzone from "./UploadDropzone";
import ProcessingPipeline from "./ProcessingPipeline";
import UploadResultCard from "./UploadResultCard";
import { createSampleDocumentFile } from "./sampleDocument";
import {
  PIPELINE_STAGE_LABELS,
  PIPELINE_STEP_INTERVAL_MS,
  getResultCardContent,
  getUploadOutcome,
  isTerminalProcessingStatus,
  type UploadOutcome,
} from "./uploadPipeline";
import "./documents.css";

export interface DocumentsRouteProps {
  apiClient: ApiClient;
}

type ScreenState =
  | { phase: "idle" }
  | { phase: "uploading"; file: File; stepIndex: number }
  // Defensive fallback for a `processingStatus` of "Uploaded"/"Processing" --
  // not expected on the V1 synchronous pipeline (see uploadPipeline.ts's own
  // comment), but the contract keeps those values, so this does not hang or
  // crash if the backend ever returns one.
  | { phase: "pending"; file: File }
  | { phase: "done"; file: File; outcome: UploadOutcome; message: string; contractId: string | null };

/**
 * Route `/documents` (ADR-018), screen 3's upload half (ADR-020: "screen 3
 * may be two: upload UI + document-status read-back" -- the document table
 * / read-back list is the other half, out of this task's AC). Wired into
 * ../../components/shell/WorkspaceShellApp.tsx's `documents` route in place
 * of that shell task's ScaffoldScreen placeholder.
 *
 * `apiClient` is threaded in as a prop (App.tsx -> WorkspaceShellApp ->
 * here) the same way SignInRoute already receives it; tenantId is *not*
 * threaded as a prop -- it reads `loadCurrentWorkspace()` directly, exactly
 * what that module's own doc comment names as the reason it keeps the
 * current workspace id available ("future screens ... read this to know
 * which tenant to send as the X-Tenant-Id header").
 */
export default function DocumentsRoute({ apiClient }: DocumentsRouteProps) {
  const navigate = useNavigate();
  const workspace = loadCurrentWorkspace();
  const [state, setState] = useState<ScreenState>({ phase: "idle" });
  const [queue, setQueue] = useState<File[]>([]);
  const tickerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const stopTicker = useCallback(() => {
    if (tickerRef.current !== null) {
      clearInterval(tickerRef.current);
      tickerRef.current = null;
    }
  }, []);

  // Never leave an interval running past unmount (e.g. the user navigates
  // away from /documents mid-upload).
  useEffect(() => stopTicker, [stopTicker]);

  const startUpload = useCallback(
    (file: File) => {
      if (!workspace) return;

      setState({ phase: "uploading", file, stepIndex: 0 });
      stopTicker();
      // Pure client-side pacing while the one real request is in flight --
      // see PIPELINE_STEP_INTERVAL_MS's own comment. Holds on the last stage
      // rather than looping if the request outlives the animation.
      tickerRef.current = setInterval(() => {
        setState((current) =>
          current.phase === "uploading"
            ? { ...current, stepIndex: Math.min(current.stepIndex + 1, PIPELINE_STAGE_LABELS.length - 1) }
            : current,
        );
      }, PIPELINE_STEP_INTERVAL_MS);

      void apiClient.uploadDocument(workspace.id, file).then((result) => {
        stopTicker();

        if (!result.ok || !result.document) {
          setState({
            phase: "done",
            file,
            outcome: "failed",
            message: result.error ?? `Contigo could not process ${file.name}. Try again.`,
            contractId: null,
          });
          return;
        }

        const { processingStatus, contractId } = result.document;
        if (!isTerminalProcessingStatus(processingStatus)) {
          setState({ phase: "pending", file });
          return;
        }

        const outcome = getUploadOutcome(processingStatus);
        setState({
          phase: "done",
          file,
          outcome,
          message: getResultCardContent(outcome, file.name).message,
          contractId,
        });
      });
    },
    [apiClient, stopTicker, workspace],
  );

  const handleFilesSelected = (files: File[]) => {
    if (files.length === 0) return;

    // One upload in flight at a time (screens.md #3 shows a single pipeline
    // / single result card, never several at once) -- extra files queue and
    // start automatically once the current one reaches its result card and
    // the user clicks "Upload another" (see handleUploadAnother).
    if (state.phase === "uploading") {
      setQueue((current) => [...current, ...files]);
      return;
    }

    const [first, ...rest] = files;
    if (rest.length > 0) {
      setQueue((current) => [...current, ...rest]);
    }
    startUpload(first);
  };

  const handleUseSampleFile = () => {
    handleFilesSelected([createSampleDocumentFile()]);
  };

  const handleUploadAnother = () => {
    setQueue((current) => {
      if (current.length === 0) {
        setState({ phase: "idle" });
        return current;
      }
      const [next, ...rest] = current;
      startUpload(next);
      return rest;
    });
  };

  const handlePrimaryAction = () => {
    if (state.phase !== "done") return;
    if (state.outcome === "completed") {
      navigate(state.contractId ? `/contracts/${state.contractId}` : "/contracts");
      return;
    }
    if (state.outcome === "needs_review") {
      navigate(state.contractId ? `/contracts/${state.contractId}/review` : "/review");
      return;
    }
    // "Retry upload": re-run the same file, matching the compiled
    // prototype's own failed.go = () => this.startUpload().
    startUpload(state.file);
  };

  if (!workspace) {
    // Should not normally be reachable -- App.tsx only mounts the shell (and
    // therefore this route) once a workspace is current -- but this route
    // reads the store directly rather than trusting that earlier check, so
    // it stays honest rather than sending `X-Tenant-Id: undefined`.
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p>Choose a workspace before uploading documents.</p>
      </div>
    );
  }

  return (
    <div className="documents-screen">
      <p className="screen-kicker">R0</p>
      <h2 className="screen-title">Documents</h2>
      <p className="micro-meta">
        Upload a contract and Contigo classifies, extracts and structures it automatically.
      </p>

      <div className="documents-columns">
        <UploadDropzone
          disabled={state.phase === "uploading"}
          onFilesSelected={handleFilesSelected}
          onUseSampleFile={handleUseSampleFile}
        />
        <div className="documents-status-column">
          {state.phase === "uploading" && <ProcessingPipeline currentStepIndex={state.stepIndex} />}
          {state.phase === "pending" && (
            <div className="empty-state" role="status">
              <h4>Still processing</h4>
              <p className="micro-meta">
                {state.file.name} is taking longer than usual. This screen does not auto-refresh yet — check back
                shortly.
              </p>
            </div>
          )}
          {state.phase === "done" && (
            <UploadResultCard
              fileName={state.file.name}
              outcome={state.outcome}
              message={state.message}
              onPrimaryAction={handlePrimaryAction}
              onUploadAnother={handleUploadAnother}
            />
          )}
        </div>
      </div>

      {queue.length > 0 && (
        <p className="micro-meta">
          {queue.length} more file{queue.length > 1 ? "s" : ""} queued — starts after "Upload another".
        </p>
      )}

      <p className="micro-meta documents-legend">
        uploaded → processing → needs_review / completed · failed = retry or replace file
      </p>
    </div>
  );
}
