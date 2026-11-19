import type { PlaygroundWorkflow } from "../../api/client";

type Rec = Record<string, unknown>;
const isRec = (v: unknown): v is Rec => typeof v === "object" && v !== null && !Array.isArray(v);
const list = (v: unknown): unknown[] => (Array.isArray(v) ? v : []);
const str = (v: unknown): string => (typeof v === "string" ? v : v === null || v === undefined ? "" : String(v));

/** The label a run gets in a lane and in the history. */
export function workflowLabel(workflow: PlaygroundWorkflow): string {
  return { ask: "Ask Raffa", council: "Negotiation council", drafting: "Email drafting", research: "Web research", extraction: "Document extraction" }[workflow];
}

function Pretty({ value }: { value: unknown }) {
  return <pre className="pg-pre">{JSON.stringify(value, null, 2)}</pre>;
}

function Council({ r }: { r: Rec }) {
  const plays = list(r.plays).filter(isRec);
  const failures = list(r.failures).map(str);
  return (
    <div className="pg-result" aria-label="Council result">
      <p className="hint">
        {str(r.supplier)} · agents run: {list(r.agentsRun).map(str).join(", ") || "none"}
        {Number(r.playsDropped) > 0 ? ` · ${str(r.playsDropped)} play(s) dropped by the guards` : ""}
      </p>
      {plays.length === 0 ? <p className="hint">The council produced no plays.</p> : null}
      <ol className="pg-plays">
        {plays.map((p) => (
          <li key={str(p.citationKey)} className="pg-play">
            <strong>{str(p.title)}</strong>
            <p className="pg-reply-text">{str(p.snippet)}</p>
          </li>
        ))}
      </ol>
      {failures.length > 0 ? (
        <p role="alert" className="admin-error">
          {failures.join(" · ")}
        </p>
      ) : null}
      <details>
        <summary>Evidence given to the council ({list(r.evidence).length} items)</summary>
        <Pretty value={r.evidence} />
      </details>
    </div>
  );
}

function Drafting({ r }: { r: Rec }) {
  const tone = list(r.toneFindings).map(str);
  return (
    <div className="pg-result" aria-label="Drafting result">
      <p className="hint">
        {str(r.supplier)} · {str(r.type)} · written by: {str(r.source)} · {str(r.attempts)} writer call(s)
      </p>
      <p className="pg-you">{str(r.subject)}</p>
      <p className="pg-reply-text">{str(r.body)}</p>
      {tone.length > 0 ? <p className="pg-note">Tone check: {tone.join("; ")}</p> : null}
      {list(r.failures).length > 0 ? <p className="pg-note">Agent failures: {list(r.failures).map(str).join("; ")}</p> : null}
    </div>
  );
}

function Research({ r }: { r: Rec }) {
  const citations = list(r.citations).filter(isRec);
  return (
    <div className="pg-result" aria-label="Research result">
      <p className="pg-reply-meta">
        <span className="pg-chip">{str(r.kind)}</span>
        <span className="pg-chip">{str(r.sourceCount)} sources</span>
        {r.guardIntervened ? <span className="pg-chip pg-chip-warn">guard intervened</span> : null}
        <span className="pg-chip">
          figures {str(r.figuresVerified)}/{str(r.figuresReported)} verified
        </span>
        {Number(r.sentencesRemoved) > 0 ? <span className="pg-chip">{str(r.sentencesRemoved)} sentence(s) removed</span> : null}
      </p>
      {str(r.sentQuery) ? <p className="hint">Query sent to the web: “{str(r.sentQuery)}”</p> : null}
      <p className="pg-reply-text">{str(r.markdown)}</p>
      {citations.length > 0 ? (
        <ol className="pg-plays">
          {citations.map((c) => (
            <li key={str(c.n)}>
              {str(c.href) ? (
                <a href={str(c.href)} target="_blank" rel="noreferrer noopener">
                  {str(c.title)}
                </a>
              ) : (
                str(c.title)
              )}
            </li>
          ))}
        </ol>
      ) : null}
      {str(r.guardViolation) ? <p className="pg-note">Guard: {str(r.guardViolation)}</p> : null}
      {str(r.error) ? (
        <p role="alert" className="admin-error">
          {str(r.error)}
        </p>
      ) : null}
    </div>
  );
}

function Extraction({ r }: { r: Rec }) {
  const stages = list(r.stages).filter(isRec);
  return (
    <div className="pg-result" aria-label="Extraction result">
      <p className="hint">
        {str(r.pages)} page(s) · {str(r.characters)} characters · {str(r.documentFirst) === "true" ? "document-first layout" : "stage-first layout"}
      </p>
      <ol className="pg-trace">
        {stages.map((s) => (
          <li key={str(s.stage)} className={s.ok ? "pg-step" : "pg-step is-failed"}>
            <details>
              <summary>
                <strong>{str(s.stage)}</strong>
                <span className="pg-step-meta">
                  {str(s.model) ? ` · ${str(s.model)}` : ""}
                  {s.ok ? "" : " · failed"}
                </span>
              </summary>
              {str(s.error) ? <p className="pg-note">{str(s.error)}</p> : null}
              <Pretty value={s.payload} />
            </details>
          </li>
        ))}
      </ol>
    </div>
  );
}

/** What a non-chat workflow returned, in the shape of that workflow. */
export default function WorkflowResult({ workflow, result }: { workflow: PlaygroundWorkflow; result: unknown }) {
  if (!isRec(result)) return <p className="hint">No result.</p>;
  switch (workflow) {
    case "council":
      return <Council r={result} />;
    case "drafting":
      return <Drafting r={result} />;
    case "research":
      return <Research r={result} />;
    case "extraction":
      return <Extraction r={result} />;
    default:
      return <Pretty value={result} />;
  }
}
