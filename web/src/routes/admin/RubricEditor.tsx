import { useState, type FormEvent } from "react";
import type { PlaygroundRubricBody, PlaygroundWorkflow } from "../../api/client";
import { workflowLabel } from "./WorkflowResult";

const WORKFLOWS: PlaygroundWorkflow[] = ["ask", "council", "drafting", "research", "extraction"];
const MAX_CRITERIA = 8;

interface Row {
  key: number;
  label: string;
  description: string;
  weight: number;
}

/** Create or edit a custom rubric: the criteria the judge scores and how much each weighs. Built-in rubrics start a new one. */
export default function RubricEditor({
  rubric,
  onSave,
  onCancel,
}: {
  /** An existing custom rubric to edit, or a built-in one to copy; null starts from nothing. */
  rubric: PlaygroundRubricBody | null;
  onSave: (rubric: { name: string; workflow: PlaygroundWorkflow; criteria: Array<{ label: string; description: string; weight: number }> }) => Promise<string | null>;
  onCancel: () => void;
}) {
  const [name, setName] = useState(rubric ? (rubric.isDefault ? `${rubric.name} (custom)` : rubric.name) : "");
  const [workflow, setWorkflow] = useState<PlaygroundWorkflow>(rubric?.workflow ?? "ask");
  const [rows, setRows] = useState<Row[]>(
    rubric ? rubric.criteria.map((c, i) => ({ key: i, label: c.label, description: c.description, weight: c.weight })) : [{ key: 0, label: "", description: "", weight: 3 }],
  );
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const update = (key: number, patch: Partial<Row>) => setRows((current) => current.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  const valid = name.trim().length > 0 && rows.length > 0 && rows.every((r) => r.label.trim().length > 0);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    const problem = await onSave({
      name: name.trim(),
      workflow,
      criteria: rows.map((r) => ({ label: r.label.trim(), description: r.description.trim(), weight: r.weight })),
    });
    setBusy(false);
    setError(problem);
  };

  return (
    <form className="pg-editor admin-card" onSubmit={(e) => void submit(e)} aria-label="Rubric editor">
      <h2 className="pg-heading">{rubric && !rubric.isDefault ? "Edit rubric" : "New rubric"}</h2>
      <p className="hint">The judge scores every answer on each criterion from 1 to 5. A criterion with a higher weight counts more in the total.</p>
      <div className="pg-field">
        <label htmlFor="pg-rubric-name">Name</label>
        <input id="pg-rubric-name" className="input" value={name} maxLength={120} onChange={(e) => setName(e.target.value)} />
      </div>
      <div className="pg-field">
        <label htmlFor="pg-rubric-workflow">Workflow</label>
        <select id="pg-rubric-workflow" className="input" value={workflow} onChange={(e) => setWorkflow(e.target.value as PlaygroundWorkflow)}>
          {WORKFLOWS.map((w) => (
            <option key={w} value={w}>
              {workflowLabel(w)}
            </option>
          ))}
        </select>
      </div>

      <fieldset className="pg-field">
        <legend>Criteria</legend>
        {rows.map((r, i) => (
          <div key={r.key} className="pg-criterion">
            <input aria-label={`Criterion ${i + 1} label`} className="input" placeholder="Label" maxLength={80} value={r.label} onChange={(e) => update(r.key, { label: e.target.value })} />
            <input
              aria-label={`Criterion ${i + 1} description`}
              className="input"
              placeholder="What a 5 looks like"
              maxLength={400}
              value={r.description}
              onChange={(e) => update(r.key, { description: e.target.value })}
            />
            <select aria-label={`Criterion ${i + 1} weight`} className="input" value={r.weight} onChange={(e) => update(r.key, { weight: Number(e.target.value) })}>
              {[1, 2, 3, 4, 5].map((w) => (
                <option key={w} value={w}>
                  weight {w}
                </option>
              ))}
            </select>
            <button type="button" className="btn btn-ghost" disabled={rows.length === 1} onClick={() => setRows((current) => current.filter((x) => x.key !== r.key))}>
              Remove
            </button>
          </div>
        ))}
        <button
          type="button"
          className="btn"
          disabled={rows.length >= MAX_CRITERIA}
          onClick={() => setRows((current) => [...current, { key: Math.max(0, ...current.map((x) => x.key)) + 1, label: "", description: "", weight: 3 }])}
        >
          Add criterion
        </button>
      </fieldset>

      {error ? (
        <p role="alert" className="admin-error">
          {error}
        </p>
      ) : null}
      <div className="pg-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || !valid}>
          Save rubric
        </button>
        <button type="button" className="btn" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}
