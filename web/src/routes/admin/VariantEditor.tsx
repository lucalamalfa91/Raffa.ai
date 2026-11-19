import { useMemo, useState, type FormEvent } from "react";
import type {
  PlaygroundBaselineBody,
  PlaygroundModelOverride,
  PlaygroundOverrides,
  PlaygroundVariantBody,
} from "../../api/client";

const AGENT = "agent";

interface ModelDraft {
  modelId: string;
  reasoningEffort: string;
  maxCompletionTokens: string;
  temperature: string;
}

const norm = (text: string): string => text.replace(/\r\n/g, "\n").trim();

function draftFrom(baseline: PlaygroundBaselineBody, variant: PlaygroundVariantBody | null) {
  const o = variant?.overrides;
  const models: Record<string, ModelDraft> = {};
  for (const m of baseline.models.filter((x) => x.configured)) {
    const change = o?.models?.[m.role];
    models[m.role] = {
      modelId: change?.modelId ?? "",
      reasoningEffort: change?.reasoningEffort ?? "",
      maxCompletionTokens: change?.maxCompletionTokens?.toString() ?? "",
      temperature: change?.temperature?.toString() ?? "",
    };
  }
  return {
    name: variant?.name ?? "",
    description: variant?.description ?? "",
    prompt: o?.prompts?.[AGENT] ?? baseline.prompts.find((p) => p.agent === AGENT)?.text ?? "",
    models,
    disabledTools: new Set(o?.disabledTools ?? []),
    maxToolCalls: o?.maxToolCalls?.toString() ?? "",
  };
}

/** The overrides a draft stands for: only what differs from what production runs. */
export function overridesOf(baseline: PlaygroundBaselineBody, draft: ReturnType<typeof draftFrom>): Partial<PlaygroundOverrides> {
  const basePrompt = baseline.prompts.find((p) => p.agent === AGENT)?.text ?? "";
  const prompts: Record<string, string> = norm(draft.prompt) !== norm(basePrompt) && norm(draft.prompt) !== "" ? { [AGENT]: draft.prompt } : {};
  const models: Record<string, PlaygroundModelOverride> = {};
  for (const [role, m] of Object.entries(draft.models)) {
    const change: PlaygroundModelOverride = {
      modelId: m.modelId || null,
      reasoningEffort: m.reasoningEffort || null,
      maxCompletionTokens: m.maxCompletionTokens ? Number(m.maxCompletionTokens) : null,
      temperature: m.temperature !== "" ? Number(m.temperature) : null,
    };
    if (Object.values(change).some((v) => v !== null)) models[role] = change;
  }
  return {
    prompts,
    models,
    disabledTools: [...draft.disabledTools],
    maxToolCalls: draft.maxToolCalls ? Number(draft.maxToolCalls) : null,
  };
}

/** Create or edit one variant: the agent prompt, the model of each role, the tools and the tool-call limit. */
export default function VariantEditor({
  baseline,
  variant,
  onSave,
  onCancel,
}: {
  baseline: PlaygroundBaselineBody;
  variant: PlaygroundVariantBody | null;
  onSave: (variant: { name: string; description: string | null; overrides: Partial<PlaygroundOverrides> }) => Promise<string | null>;
  onCancel: () => void;
}) {
  const [draft, setDraft] = useState(() => draftFrom(baseline, variant));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const basePrompt = baseline.prompts.find((p) => p.agent === AGENT)?.text ?? "";
  const promptModified = norm(draft.prompt) !== norm(basePrompt);
  const roles = useMemo(() => baseline.models.filter((m) => m.configured), [baseline]);

  const setModel = (role: string, patch: Partial<ModelDraft>) =>
    setDraft((d) => ({ ...d, models: { ...d.models, [role]: { ...d.models[role], ...patch } } }));

  const toggleTool = (name: string) =>
    setDraft((d) => {
      const next = new Set(d.disabledTools);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return { ...d, disabledTools: next };
    });

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    const problem = await onSave({
      name: draft.name.trim(),
      description: draft.description.trim() || null,
      overrides: overridesOf(baseline, draft),
    });
    setBusy(false);
    setError(problem);
  };

  return (
    <form className="pg-editor admin-card" onSubmit={(e) => void submit(e)} aria-label="Variant editor">
      <h2 className="pg-heading">{variant ? "Edit variant" : "New variant"}</h2>
      <p className="hint">
        A variant changes only what you set here, and only for playground runs. Nothing reaches production until the change is
        proposed as a pull request.
      </p>
      <div className="pg-field">
        <label htmlFor="pg-variant-name">Name</label>
        <input id="pg-variant-name" className="input" value={draft.name} maxLength={120} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
      </div>
      <div className="pg-field">
        <label htmlFor="pg-variant-desc">Description</label>
        <input
          id="pg-variant-desc"
          className="input"
          value={draft.description}
          maxLength={500}
          onChange={(e) => setDraft({ ...draft, description: e.target.value })}
        />
      </div>

      <div className="pg-field">
        <label htmlFor="pg-variant-prompt">
          Orchestrator prompt ({baseline.prompts.find((p) => p.agent === AGENT)?.version}){" "}
          {promptModified ? <span className="pg-chip pg-chip-warn">modified</span> : <span className="pg-chip">as in production</span>}
        </label>
        <textarea
          id="pg-variant-prompt"
          className="input pg-prompt"
          rows={14}
          value={draft.prompt}
          onChange={(e) => setDraft({ ...draft, prompt: e.target.value })}
        />
        {promptModified ? (
          <button type="button" className="btn btn-ghost" onClick={() => setDraft({ ...draft, prompt: basePrompt })}>
            Reset to production prompt
          </button>
        ) : null}
      </div>

      <fieldset className="pg-field">
        <legend>Models</legend>
        <p className="hint">Leave a field empty to keep what production uses. Deployments are the ones already configured here.</p>
        <table className="table pg-models">
          <thead>
            <tr>
              <th>Role</th>
              <th>Deployment</th>
              <th>Reasoning</th>
              <th>Max tokens</th>
              <th>Temp (≤ {baseline.maxTemperature})</th>
            </tr>
          </thead>
          <tbody>
            {roles.map((r) => (
              <tr key={r.role}>
                <th scope="row">
                  {r.role}
                  {r.fallbackOf ? <span className="hint"> (uses {r.fallbackOf})</span> : null}
                </th>
                <td>
                  <select
                    aria-label={`${r.role} deployment`}
                    className="input"
                    value={draft.models[r.role]?.modelId ?? ""}
                    onChange={(e) => setModel(r.role, { modelId: e.target.value })}
                  >
                    <option value="">{r.modelId}</option>
                    {baseline.allowedModelIds
                      .filter((id) => id !== r.modelId)
                      .map((id) => (
                        <option key={id} value={id}>
                          {id}
                        </option>
                      ))}
                  </select>
                </td>
                <td>
                  <select
                    aria-label={`${r.role} reasoning effort`}
                    className="input"
                    value={draft.models[r.role]?.reasoningEffort ?? ""}
                    onChange={(e) => setModel(r.role, { reasoningEffort: e.target.value })}
                  >
                    <option value="">{r.reasoningEffort ?? "default"}</option>
                    {baseline.reasoningEfforts
                      .filter((x) => x !== r.reasoningEffort)
                      .map((x) => (
                        <option key={x} value={x}>
                          {x}
                        </option>
                      ))}
                  </select>
                </td>
                <td>
                  <input
                    aria-label={`${r.role} max tokens`}
                    className="input"
                    type="number"
                    min={256}
                    max={65536}
                    placeholder={r.maxCompletionTokens?.toString() ?? "default"}
                    value={draft.models[r.role]?.maxCompletionTokens ?? ""}
                    onChange={(e) => setModel(r.role, { maxCompletionTokens: e.target.value })}
                  />
                </td>
                <td>
                  <input
                    aria-label={`${r.role} temperature`}
                    className="input"
                    type="number"
                    min={0}
                    max={baseline.maxTemperature}
                    step={0.05}
                    placeholder={r.temperature?.toString() ?? "default"}
                    value={draft.models[r.role]?.temperature ?? ""}
                    onChange={(e) => setModel(r.role, { temperature: e.target.value })}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </fieldset>

      <fieldset className="pg-field">
        <legend>Tools the orchestrator may call</legend>
        <div className="pg-tools">
          {baseline.tools.map((t) => (
            <label key={t.name} className="pg-tool" title={t.description}>
              <input type="checkbox" checked={!draft.disabledTools.has(t.name)} onChange={() => toggleTool(t.name)} /> {t.name}
            </label>
          ))}
        </div>
        <div className="pg-field">
          <label htmlFor="pg-variant-maxcalls">Max tool calls per turn</label>
          <input
            id="pg-variant-maxcalls"
            className="input"
            type="number"
            min={1}
            max={16}
            placeholder={baseline.maxToolCalls.toString()}
            value={draft.maxToolCalls}
            onChange={(e) => setDraft({ ...draft, maxToolCalls: e.target.value })}
          />
        </div>
      </fieldset>

      {error ? (
        <p role="alert" className="admin-error">
          {error}
        </p>
      ) : null}
      <div className="pg-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || draft.name.trim().length === 0}>
          Save variant
        </button>
        <button type="button" className="btn" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}
