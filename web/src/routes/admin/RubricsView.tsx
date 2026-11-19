import { useCallback, useEffect, useState } from "react";
import type { ApiClient, PlaygroundRubricBody } from "../../api/client";
import RubricEditor from "./RubricEditor";
import { workflowLabel } from "./WorkflowResult";

/** The judging rubrics: built-in ones (read-only, copy to change) and the custom ones. */
export default function RubricsView({ apiClient }: { apiClient: ApiClient }) {
  const [rubrics, setRubrics] = useState<PlaygroundRubricBody[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<{ rubric: PlaygroundRubricBody | null } | null>(null);

  const load = useCallback(async () => {
    const result = await apiClient.listPlaygroundRubrics?.();
    if (result?.ok && result.body) setRubrics(result.body.rubrics);
    else setError(result?.error ?? "Could not load the rubrics.");
  }, [apiClient]);

  useEffect(() => {
    void load();
  }, [load]);

  const save = async (rubric: { name: string; workflow: PlaygroundRubricBody["workflow"]; criteria: Array<{ label: string; description: string; weight: number }> }) => {
    const id = editing?.rubric && !editing.rubric.isDefault ? editing.rubric.id : null;
    const result = await apiClient.savePlaygroundRubric?.(id, rubric);
    if (!result?.ok) return result?.error ?? "The rubric was not saved.";
    await load();
    setEditing(null);
    return null;
  };

  const archive = async (rubric: PlaygroundRubricBody) => {
    if (!window.confirm(`Archive "${rubric.name}"? Past judgements keep their own copy.`)) return;
    await apiClient.archivePlaygroundRubric?.(rubric.id);
    await load();
  };

  if (error) {
    return (
      <p role="alert" className="admin-error">
        {error}
      </p>
    );
  }
  if (!rubrics) return <p className="hint">Loading…</p>;
  if (editing) return <RubricEditor key={editing.rubric?.id ?? "new"} rubric={editing.rubric} onSave={save} onCancel={() => setEditing(null)} />;

  return (
    <section className="admin-card" aria-label="Rubrics">
      <div className="pg-actions">
        <button type="button" className="btn btn-primary" onClick={() => setEditing({ rubric: null })}>
          New rubric
        </button>
      </div>
      <table className="table admin-table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Workflow</th>
            <th>Criteria</th>
            <th>
              <span className="visually-hidden">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {rubrics.map((r) => (
            <tr key={r.id}>
              <td>
                {r.name} {r.isDefault ? <span className="pg-chip">built-in</span> : null}
              </td>
              <td>{workflowLabel(r.workflow)}</td>
              <td>{r.criteria.map((c) => `${c.label} ×${c.weight}`).join(", ")}</td>
              <td>
                {r.isDefault ? (
                  <button type="button" className="btn" onClick={() => setEditing({ rubric: r })}>
                    Copy and edit
                  </button>
                ) : (
                  <>
                    <button type="button" className="btn" onClick={() => setEditing({ rubric: r })}>
                      Edit
                    </button>
                    <button type="button" className="btn" onClick={() => void archive(r)}>
                      Archive
                    </button>
                  </>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
