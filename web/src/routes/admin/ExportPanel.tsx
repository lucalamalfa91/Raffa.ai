import { useEffect, useState } from "react";
import type {
  ApiClient,
  PlaygroundExportCapabilities,
  PlaygroundExportRecord,
  PlaygroundExportResult,
  PlaygroundVariantBody,
} from "../../api/client";

/** The server's error text: its `{"error": ...}` body when there is one, else the raw message. */
function messageOf(error: string | null): string {
  if (!error) return "The export failed.";
  try {
    const body = JSON.parse(error) as { error?: unknown };
    return typeof body.error === "string" ? body.error : error;
  } catch {
    return error;
  }
}

function download(result: PlaygroundExportResult): void {
  if (!result.zipBase64 || typeof URL.createObjectURL !== "function") return;
  const bytes = Uint8Array.from(atob(result.zipBase64), (c) => c.charCodeAt(0));
  const url = URL.createObjectURL(new Blob([bytes], { type: "application/zip" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = result.fileName ?? "playground-proposal.zip";
  link.click();
  URL.revokeObjectURL(url);
}

/** What a variant would change in the repository, in a few words, for the confirmation. */
function changes(variant: PlaygroundVariantBody): string[] {
  const o = variant.overrides;
  const lines: string[] = [];
  const prompts = Object.keys(o.prompts ?? {});
  if (prompts.length > 0) lines.push(`Prompts: ${prompts.join(", ")}${prompts.includes("agent") ? " (the orchestrator becomes a new version in code)" : ""}`);
  const models = Object.keys(o.models ?? {});
  if (models.length > 0) lines.push(`Models (${models.join(", ")}): listed as configuration to set, not applied`);
  if ((o.disabledTools ?? []).length > 0) lines.push(`Tools removed (${o.disabledTools.join(", ")}): listed, needs a code change`);
  if (o.maxToolCalls) lines.push(`Tool-call limit ${o.maxToolCalls}: listed as configuration`);
  return lines;
}

/**
 * Proposes a variant to the repository. A pull request is always a draft on a new branch with the report, the proposed prompts and
 * their diffs; Raffa never merges it, and nothing changes in production until a person reviews and merges it. Where GitHub is not
 * connected the same files come as a zip.
 */
export default function ExportPanel({
  apiClient,
  variant,
  onClose,
}: {
  apiClient: ApiClient;
  variant: PlaygroundVariantBody;
  onClose: () => void;
}) {
  const [capabilities, setCapabilities] = useState<PlaygroundExportCapabilities | null>(null);
  const [past, setPast] = useState<PlaygroundExportRecord[]>([]);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<PlaygroundExportResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadPast = async () => {
    const list = await apiClient.listPlaygroundExports?.(variant.id);
    if (list?.ok && list.body) setPast(list.body.exports);
  };

  useEffect(() => {
    let live = true;
    void (async () => {
      const [caps, list] = await Promise.all([apiClient.getPlaygroundExportCapabilities?.(), apiClient.listPlaygroundExports?.(variant.id)]);
      if (!live) return;
      setCapabilities(caps?.ok && caps.body ? caps.body : { pullRequest: false, repository: null });
      if (list?.ok && list.body) setPast(list.body.exports);
    })();
    return () => {
      live = false;
    };
  }, [apiClient, variant.id]);

  const run = async (target: "pull_request" | "bundle") => {
    if (!apiClient.exportPlaygroundVariant) return;
    setBusy(true);
    setError(null);
    setResult(null);
    const response = await apiClient.exportPlaygroundVariant(variant.id, target);
    setBusy(false);
    if (response.ok && response.body) {
      setResult(response.body);
      if (target === "bundle") download(response.body);
    } else {
      setError(messageOf(response.error));
    }
    await loadPast();
  };

  const summary = changes(variant);
  return (
    <section className="admin-card pg-export" aria-label="Export variant">
      <h2 className="pg-heading">Propose “{variant.name}” to the repository</h2>
      <p className="hint">
        This only proposes. A pull request is a draft on a new branch that a person reviews and merges; production changes only then. Run the golden set before merging.
      </p>
      {summary.length > 0 ? (
        <ul className="pg-rationales">
          {summary.map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      ) : (
        <p className="hint">This variant changes nothing: there is nothing to propose.</p>
      )}

      <div className="pg-actions">
        <button
          type="button"
          className="btn btn-primary"
          disabled={busy || summary.length === 0 || !capabilities?.pullRequest}
          title={capabilities && !capabilities.pullRequest ? "GitHub is not connected in this environment" : undefined}
          onClick={() => void run("pull_request")}
        >
          {busy ? "Working…" : `Open a draft pull request${capabilities?.repository ? ` on ${capabilities.repository}` : ""}`}
        </button>
        <button type="button" className="btn" disabled={busy || summary.length === 0} onClick={() => void run("bundle")}>
          Download bundle (.zip)
        </button>
        <button type="button" className="btn btn-ghost" onClick={onClose}>
          Close
        </button>
      </div>
      {capabilities && !capabilities.pullRequest ? <p className="hint">GitHub is not connected here: download the bundle and apply it from a checkout (APPLY.md inside).</p> : null}

      {error ? (
        <p role="alert" className="admin-error">
          {error}
        </p>
      ) : null}
      {result ? (
        <div role="status" className="admin-result">
          {result.prUrl ? (
            <p>
              Draft pull request opened:{" "}
              <a href={result.prUrl} target="_blank" rel="noreferrer noopener">
                #{result.prNumber}
              </a>{" "}
              on branch <code>{result.branch}</code> ({result.files.length} files).
            </p>
          ) : (
            <p>Bundle downloaded ({result.files.length} files).</p>
          )}
          {(result.notes ?? []).map((n) => (
            <p key={n} className="pg-note">
              {n}
            </p>
          ))}
        </div>
      ) : null}

      {past.length > 0 ? (
        <details>
          <summary>Earlier exports of this variant ({past.length})</summary>
          <ul className="pg-rationales">
            {past.map((e) => (
              <li key={e.id}>
                {new Date(e.createdAt).toLocaleString()} · {e.target === "pull_request" ? "pull request" : "bundle"} · {e.status}
                {e.prUrl ? (
                  <>
                    {" "}
                    ·{" "}
                    <a href={e.prUrl} target="_blank" rel="noreferrer noopener">
                      #{e.prNumber}
                    </a>
                  </>
                ) : null}
                {e.error ? ` · ${e.error}` : ""}
              </li>
            ))}
          </ul>
        </details>
      ) : null}
    </section>
  );
}
