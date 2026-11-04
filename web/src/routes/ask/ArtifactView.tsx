import { useEffect, useState } from "react";
import { useI18n } from "../../i18n/I18nProvider";
import type { Translate } from "../../i18n/translate";
import ArtifactPanel from "./ArtifactPanel";
import { artifactToCsv, artifactToMarkdown, type Artifact } from "./artifacts";
import { MailIcon } from "./DraftPanel";
import ReplyBody from "./ReplyBody";

function ChartIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <path d="M4 20V10M10 20V4M16 20v-7M22 20H2" />
    </svg>
  );
}
function DocIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <path d="M6 3h8l4 4v14H6zM14 3v4h4M9 12h6M9 16h6" />
    </svg>
  );
}
export function artifactIcon(a: Artifact) {
  return a.kind === "document" ? <DocIcon /> : <ChartIcon />;
}
void MailIcon;

/** "KPIs", or "KPIs · Version 2" for a later version of the same content. */
function artifactKindLabel(a: Artifact, t: Translate): string {
  const kind = a.kind === "dashboard" ? t("Dashboard") : a.kind === "kpi" ? t("KPIs") : t("Document");
  return a.version > 1 ? `${kind} · ${t("Version {n}", { n: a.version })}` : kind;
}

function download(name: string, text: string, type: string) {
  const url = URL.createObjectURL(new Blob([text], { type }));
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  a.click();
  URL.revokeObjectURL(url);
}

/** The in-chat handle of a digital content Raffa built: one line, opens the panel. */
export function ContentCard({ artifact, active, onOpen }: { artifact: Artifact; active: boolean; onOpen: () => void }) {
  const { t } = useI18n();
  const kind = artifactKindLabel(artifact, t);
  return (
    <button type="button" className="artifact-card" data-active={active ? "true" : undefined} aria-pressed={active} onClick={onOpen}>
      <span className="artifact-card-icon" aria-hidden="true">
        {artifactIcon(artifact)}
      </span>
      <span className="artifact-card-text">
        <span className="artifact-card-title">{artifact.title}</span>
        <span className="artifact-card-meta">
          {kind}
          <span aria-hidden="true"> · </span>
          {active ? t("Open⟦panel⟧") : t("Click to open")}
        </span>
      </span>
    </button>
  );
}

/** A dashboard, a set of KPIs or a document, in the right-hand panel, with Copy, Download and Print. */
export default function ArtifactViewPanel({ artifact, onClose }: { artifact: Artifact; onClose: () => void }) {
  const { t } = useI18n();
  const [copied, setCopied] = useState(false);
  useEffect(() => setCopied(false), [artifact.id]);
  useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(timer);
  }, [copied]);
  const csv = artifactToCsv(artifact);
  const kind = artifactKindLabel(artifact, t);
  const copy = () => {
    const clipboard = typeof navigator === "undefined" ? undefined : navigator.clipboard;
    if (clipboard) void clipboard.writeText(artifactToMarkdown(artifact)).then(() => setCopied(true));
  };
  const max = (items: Array<{ value: number }>) => Math.max(1, ...items.map((i) => i.value));

  return (
    <ArtifactPanel
      label={kind}
      kicker={kind}
      title={artifact.title}
      icon={artifactIcon(artifact)}
      size="wide"
      onClose={onClose}
      actions={
        <>
          <button type="button" className="artifact-panel-action" onClick={() => download(`${artifact.kind}.md`, artifactToMarkdown(artifact), "text/markdown")}>
            {t("Download")}
          </button>
          {csv ? (
            <button type="button" className="artifact-panel-action" onClick={() => download(`${artifact.kind}.csv`, csv, "text/csv")}>
              CSV
            </button>
          ) : null}
          <button type="button" className="artifact-panel-action" onClick={() => window.print()}>
            {t("Print")}
          </button>
          <button type="button" className="artifact-panel-action" data-primary="true" onClick={copy} aria-live="polite">
            {copied ? t("Copied") : t("Copy")}
          </button>
        </>
      }
    >
      <article className="content-doc">
        <p className="content-sub">{artifact.subtitle}</p>
        {artifact.tiles.length ? (
          <div className="content-tiles">
            {artifact.tiles.map((x) => (
              <div key={x.label} className={x.tone ? `content-tile is-${x.tone}` : "content-tile"}>
                <div className="content-tile-label">{x.label}</div>
                <div className="content-tile-value">{x.value}</div>
                {x.hint ? <div className="content-tile-hint">{x.hint}</div> : null}
              </div>
            ))}
          </div>
        ) : null}
        {artifact.charts.map((c) => (
          <section key={c.title} className="content-section">
            <h4 className="content-h">{c.title}</h4>
            <ul className="content-bars" aria-label={c.title}>
              {c.items.map((i) => (
                <li key={i.label}>
                  <span className="content-bar-label">{i.label}</span>
                  <span className="content-bar-track">
                    <span className="content-bar-fill" style={{ width: `${Math.round((i.value / max(c.items)) * 100)}%` }} />
                  </span>
                  <span className="content-bar-value">{i.display}</span>
                </li>
              ))}
            </ul>
          </section>
        ))}
        {artifact.tables.map((tb) => (
          <section key={tb.title} className="content-section">
            <h4 className="content-h">{tb.title}</h4>
            <div className="content-table-wrap">
              <table className="content-table">
                <thead>
                  <tr>
                    {tb.columns.map((col) => (
                      <th key={col} scope="col">
                        {col}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {tb.rows.map((r, i) => (
                    <tr key={i}>
                      {r.map((cell, j) => (
                        <td key={j}>{cell}</td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        ))}
        {artifact.markdown ? <ReplyBody text={artifact.markdown} sources={[]} /> : null}
      </article>
    </ArtifactPanel>
  );
}
