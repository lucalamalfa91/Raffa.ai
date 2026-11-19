import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ApiClient, PlaygroundBaselineBody, PlaygroundRunBody, PlaygroundVariantBody } from "../../api/client";
import PlaygroundPanel, { formatCost, prettyPayload } from "./PlaygroundPanel";

const SANDBOXES = [
  { kind: "empty", tenantId: "t1", description: "Empty workspace.", contracts: 0 },
  { kind: "seeded", tenantId: "t2", description: "Four validated contracts.", contracts: 4 },
  { kind: "needs-review", tenantId: "t3", description: "One contract in review.", contracts: 1 },
] as const;

function run(overrides: Partial<PlaygroundRunBody> = {}): PlaygroundRunBody {
  return {
    runId: "r1",
    sandbox: "seeded",
    status: "ok",
    error: null,
    durationMs: 1500,
    reply: { kind: "answer", answerMarkdown: "Scade il 15 gennaio 2027 [1].", citations: [], actions: [], followUps: [], provenance: {} },
    usage: { promptTokens: 1200, completionTokens: 90, cachedTokens: 0, estimatedCostUsd: 0.0021, unpricedModels: [], modelCalls: 2, toolCalls: 1 },
    trace: [
      { seq: 1, kind: "model_call", name: "converse:ask-orchestrator", at: "", durationMs: 800, ok: true, model: "gpt-5.4-nano-dev", promptTokens: 700, completionTokens: 40, cachedTokens: 0, input: '{"systemPrompt":"SYSTEM"}', output: '{"toolCalls":[]}', note: null },
      { seq: 2, kind: "tool_call", name: "get_contract", at: "", durationMs: 40, ok: true, model: null, promptTokens: null, completionTokens: null, cachedTokens: null, input: '{"supplier":"Salesforce"}', output: '{"endDate":"2027-01-15"}', note: null },
    ],
    ...overrides,
  };
}

const BASELINE: PlaygroundBaselineBody = {
  prompts: [{ agent: "agent", version: "agent-v1.6", text: "You are Ask Raffa." }],
  models: [
    { role: "agent", configured: true, fallbackOf: "answer", modelId: "gpt-5.4-nano-dev", modelVersion: "1", reasoningEffort: "low", maxCompletionTokens: 4096, temperature: null },
    { role: "answer", configured: true, fallbackOf: null, modelId: "gpt-5.4-nano-dev", modelVersion: "1", reasoningEffort: null, maxCompletionTokens: 4096, temperature: null },
  ],
  allowedModelIds: ["gpt-5.4-nano-dev", "gpt-5.4-dev"],
  reasoningEfforts: ["none", "minimal", "low", "medium", "high"],
  maxTemperature: 0.2,
  tools: [
    { name: "get_contract", description: "Reads a contract." },
    { name: "web_research", description: "Searches the web." },
  ],
  maxToolCalls: 8,
  agentic: true,
};

const VARIANT: PlaygroundVariantBody = {
  id: "v1",
  name: "Short prompt",
  description: null,
  overrides: { prompts: { agent: "Be brief." }, models: {}, disabledTools: [], maxToolCalls: null },
  createdBy: "staff",
  createdAt: "2026-11-19T10:00:00Z",
  updatedAt: "2026-11-19T10:00:00Z",
};

function client(runPlayground: ApiClient["runPlayground"], extra: Partial<ApiClient> = {}): ApiClient {
  return {
    listPlaygroundSandboxes: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { sandboxes: SANDBOXES }, error: null }),
    getPlaygroundBaseline: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: BASELINE, error: null }),
    listPlaygroundVariants: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { variants: [VARIANT] }, error: null }),
    runPlayground,
    ...extra,
  } as unknown as ApiClient;
}

describe("PlaygroundPanel", () => {
  it("says so when the caller is not a platform admin", async () => {
    const apiClient = { listPlaygroundSandboxes: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, body: null, error: null }) } as unknown as ApiClient;
    render(<PlaygroundPanel apiClient={apiClient} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("You are not a Raffa platform admin.");
  });

  it("sends a question to the chosen sandbox and shows the answer with its trace", async () => {
    const runPlayground = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: run(), error: null });
    render(<PlaygroundPanel apiClient={client(runPlayground)} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.type(screen.getByLabelText("Question"), "Quando scade Salesforce?");
    await userEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(runPlayground).toHaveBeenCalledWith({ sandbox: "seeded", question: "Quando scade Salesforce?", history: [], comparisonId: expect.any(String) });
    expect(await screen.findByText("Scade il 15 gennaio 2027 [1].")).toBeInTheDocument();

    const summary = screen.getByLabelText("Run summary");
    expect(within(summary).getByText("$0.0021")).toBeInTheDocument();
    expect(within(summary).getByText("1200 / 90")).toBeInTheDocument();

    const trace = screen.getByRole("list", { name: "Trace" });
    expect(within(trace).getByText("converse:ask-orchestrator")).toBeInTheDocument();
    expect(within(trace).getByText("get_contract")).toBeInTheDocument();
    // The raw payloads are in the page (collapsed) so the admin can read exactly what was sent and returned.
    expect(trace).toHaveTextContent('"systemPrompt": "SYSTEM"');
    expect(trace).toHaveTextContent('"endDate": "2027-01-15"');
  });

  it("carries the earlier turns into the next question", async () => {
    const runPlayground = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: run(), error: null });
    render(<PlaygroundPanel apiClient={client(runPlayground)} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.type(screen.getByLabelText("Question"), "Prima");
    await userEvent.click(screen.getByRole("button", { name: "Send" }));
    await screen.findByText("Scade il 15 gennaio 2027 [1].");
    await userEvent.type(screen.getByLabelText("Question"), "Seconda");
    await userEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(runPlayground).toHaveBeenLastCalledWith({
      sandbox: "seeded",
      question: "Seconda",
      history: [
        { role: "you", markdown: "Prima" },
        { role: "raffa", markdown: "Scade il 15 gennaio 2027 [1]." },
      ],
      comparisonId: expect.any(String),
    });
  });

  it("shows the failure of a run instead of an answer", async () => {
    const runPlayground = vi.fn().mockResolvedValue({ ok: false, statusCode: 500, body: null, error: "boom" });
    render(<PlaygroundPanel apiClient={client(runPlayground)} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.type(screen.getByLabelText("Question"), "Ciao");
    await userEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByText("boom")).toBeInTheDocument();
  });

  it("sends the same question to the baseline and a variant side by side, as one comparison", async () => {
    const runPlayground = vi.fn().mockImplementation(async (input: { variantId?: string }) => ({
      ok: true,
      statusCode: 200,
      error: null,
      body: run({
        reply: {
          kind: "answer",
          answerMarkdown: input.variantId ? "Risposta della variante." : "Risposta di produzione.",
          citations: [],
          actions: [],
          followUps: [],
          provenance: {},
        },
      }),
    }));
    render(<PlaygroundPanel apiClient={client(runPlayground)} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.click(screen.getByLabelText("Short prompt"));
    await userEvent.type(screen.getByLabelText("Question"), "Quando scade?");
    await userEvent.click(screen.getByRole("button", { name: "Send to 2 variants" }));

    expect(await screen.findByText("Risposta di produzione.")).toBeInTheDocument();
    expect(await screen.findByText("Risposta della variante.")).toBeInTheDocument();
    expect(runPlayground).toHaveBeenCalledTimes(2);
    const calls = runPlayground.mock.calls.map((c) => c[0] as { variantId?: string; comparisonId: string });
    expect(calls.map((c) => c.variantId)).toEqual(expect.arrayContaining([undefined, "v1"]));
    expect(calls[0].comparisonId).toBe(calls[1].comparisonId);
    expect(screen.getByRole("region", { name: "Lane Baseline (production)" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Lane Short prompt" })).toBeInTheDocument();
  });

  it("keeps at most three variants in a comparison and never fewer than one", async () => {
    const many = [1, 2, 3].map((n) => ({ ...VARIANT, id: `v${n}`, name: `Variant ${n}` }));
    const apiClient = client(vi.fn(), {
      listPlaygroundVariants: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { variants: many }, error: null }),
    });
    render(<PlaygroundPanel apiClient={apiClient} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.click(screen.getByLabelText("Variant 1"));
    await userEvent.click(screen.getByLabelText("Variant 2"));
    await userEvent.click(screen.getByLabelText("Variant 3"));
    expect(screen.getByLabelText("Variant 3")).not.toBeChecked();
    expect(screen.getAllByRole("region", { name: /^Lane / })).toHaveLength(3);

    await userEvent.click(screen.getByLabelText("Baseline (production)"));
    await userEvent.click(screen.getByLabelText("Variant 1"));
    await userEvent.click(screen.getByLabelText("Variant 2"));
    expect(screen.getByLabelText("Variant 2")).toBeChecked();
    expect(screen.getAllByRole("region", { name: /^Lane / })).toHaveLength(1);
  });

  it("saves a variant with only what differs from production", async () => {
    const savePlaygroundVariant = vi.fn().mockResolvedValue({ ok: true, statusCode: 201, body: VARIANT, error: null });
    render(<PlaygroundPanel apiClient={client(vi.fn(), { savePlaygroundVariant })} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.click(screen.getByRole("button", { name: "Variants" }));
    await userEvent.click(screen.getByRole("button", { name: "New variant" }));
    await userEvent.type(screen.getByLabelText("Name"), "Brief, no web");
    // Untouched: the prompt is "as in production" and is not sent as an override.
    expect(screen.getByText("as in production")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("checkbox", { name: "web_research" }));
    await userEvent.selectOptions(screen.getByLabelText("agent deployment"), "gpt-5.4-dev");
    await userEvent.click(screen.getByRole("button", { name: "Save variant" }));

    expect(savePlaygroundVariant).toHaveBeenCalledWith(null, {
      name: "Brief, no web",
      description: null,
      overrides: {
        prompts: {},
        models: { agent: { modelId: "gpt-5.4-dev", reasoningEffort: null, maxCompletionTokens: null, temperature: null } },
        disabledTools: ["web_research"],
        maxToolCalls: null,
      },
    });
  });

  it("marks a changed prompt as modified, sends it, and shows the server's reason when a save is refused", async () => {
    const savePlaygroundVariant = vi.fn().mockResolvedValue({ ok: false, statusCode: 400, body: null, error: "Unknown tool 'x'." });
    render(<PlaygroundPanel apiClient={client(vi.fn(), { savePlaygroundVariant })} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.click(screen.getByRole("button", { name: "Variants" }));
    await userEvent.click(screen.getByRole("button", { name: "New variant" }));
    await userEvent.type(screen.getByLabelText("Name"), "Edited");
    const prompt = screen.getByLabelText(/Orchestrator prompt/);
    await userEvent.clear(prompt);
    await userEvent.type(prompt, "Be brief.");
    expect(screen.getByText("modified")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Save variant" }));

    expect(savePlaygroundVariant).toHaveBeenCalledWith(
      null,
      expect.objectContaining({ overrides: expect.objectContaining({ prompts: { agent: "Be brief." } }) }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent("Unknown tool 'x'.");
  });

  it("opens a stored run from the history with its trace", async () => {
    const summary = {
      id: "r9", workflow: "ask", sandbox: "seeded", createdAt: "2026-11-19T10:00:00Z", variantId: null, variantName: null,
      comparisonId: null, durationMs: 900, status: "ok", promptTokens: 10, completionTokens: 5, estimatedCostUsd: null,
      input: { question: "Domanda salvata" },
    };
    const getPlaygroundRun = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      error: null,
      body: {
        id: "r9", sandbox: "seeded", variantName: null, durationMs: 900, status: "ok", input: { question: "Domanda salvata" },
        output: { reply: run().reply, error: null, usage: run().usage }, trace: run().trace,
      },
    });
    render(
      <PlaygroundPanel
        apiClient={client(vi.fn(), {
          listPlaygroundRuns: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { runs: [summary] }, error: null }),
          getPlaygroundRun,
        })}
      />,
    );
    await screen.findByText("Four validated contracts.");

    await userEvent.click(screen.getByRole("button", { name: "History" }));
    expect(await screen.findByText("Domanda salvata")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Open" }));

    expect(getPlaygroundRun).toHaveBeenCalledWith("r9");
    expect(await screen.findByLabelText("Stored run")).toHaveTextContent("Scade il 15 gennaio 2027 [1].");
    expect(screen.getByRole("list", { name: "Trace" })).toHaveTextContent("get_contract");
  });

  it("shows n/a, never zero, when a model has no price", () => {
    expect(formatCost(null)).toBe("n/a");
    expect(formatCost(0.0021)).toBe("$0.0021");
    expect(formatCost(1.5)).toBe("$1.50");
  });

  it("pretty-prints JSON payloads and leaves other text alone", () => {
    expect(prettyPayload('{"a":1}')).toBe('{\n  "a": 1\n}');
    expect(prettyPayload("not json")).toBe("not json");
  });
});
