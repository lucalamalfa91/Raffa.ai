import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ApiClient, PlaygroundBaselineBody, PlaygroundJudgementBody, PlaygroundRubricBody, PlaygroundRunBody, PlaygroundVariantBody } from "../../api/client";
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
  prompts: [
    { agent: "agent", flow: "ask", version: "agent-v1.6", text: "You are Ask Raffa." },
    { agent: "contract-analyst", flow: "negotiation", version: "council-v1", text: "You analyse contracts." },
  ],
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
    const prompt = screen.getByLabelText(/Prompt text/);
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
    const listPlaygroundJudgements = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { judgements: [judgement()] }, error: null });
    render(
      <PlaygroundPanel
        apiClient={client(vi.fn(), {
          listPlaygroundRuns: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { runs: [summary] }, error: null }),
          getPlaygroundRun,
          listPlaygroundJudgements,
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
    expect(screen.getByLabelText("Judgements of this run")).toHaveTextContent("Judgements of this run (1)");
    expect(listPlaygroundJudgements).toHaveBeenCalledWith({ runId: "r9" });
  });

  it("runs the council on a sandbox supplier and shows its plays, with no chat history", async () => {
    const runPlayground = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      error: null,
      body: run({
        workflow: "council",
        reply: null,
        result: {
          supplier: "Salesforce",
          agentsRun: ["contract-analyst", "lever-strategist"],
          failures: [],
          playsDropped: 0,
          plays: [{ citationKey: "calc:council:play[1]", title: "Cap the renewal uplift", snippet: "Ask for a 3% cap." }],
          evidence: [{ citationKey: "k1" }],
        },
      }),
    });
    render(<PlaygroundPanel apiClient={client(runPlayground)} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.selectOptions(screen.getByLabelText("Workflow"), "council");
    expect(screen.queryByLabelText("Question")).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText(/Supplier/), "Salesforce");
    await userEvent.type(screen.getByLabelText(/Savings goal/), "reduce 20k");
    await userEvent.click(screen.getByRole("button", { name: "Run" }));

    expect(runPlayground).toHaveBeenCalledWith({
      sandbox: "seeded",
      question: "Salesforce · reduce 20k",
      history: [],
      comparisonId: expect.any(String),
      workflow: "council",
      input: { supplier: "Salesforce", goal: "reduce 20k", language: "en" },
    });
    expect(await screen.findByText("Cap the renewal uplift")).toBeInTheDocument();
    expect(screen.getByLabelText("Council result")).toHaveTextContent("contract-analyst, lever-strategist");
  });

  it("sends a web query with its purpose, and extraction text with the layout choice", async () => {
    const runPlayground = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      error: null,
      body: run({
        workflow: "research",
        reply: null,
        result: { kind: "Answered", markdown: "Prices rose 5%.", sourceCount: 2, citations: [], guardIntervened: false, figuresVerified: 1, figuresReported: 1, sentencesRemoved: 0 },
      }),
    });
    render(<PlaygroundPanel apiClient={client(runPlayground)} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.selectOptions(screen.getByLabelText("Workflow"), "research");
    await userEvent.type(screen.getByLabelText("Web query"), "Salesforce price increases");
    await userEvent.selectOptions(screen.getByLabelText("Purpose"), "SupplierNews");
    await userEvent.click(screen.getByRole("button", { name: "Run" }));
    expect(runPlayground).toHaveBeenLastCalledWith(
      expect.objectContaining({ workflow: "research", input: { query: "Salesforce price increases", purpose: "SupplierNews", language: "en" } }),
    );
    expect(await screen.findByText("Prices rose 5%.")).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText("Workflow"), "extraction");
    await userEvent.type(screen.getByLabelText(/Contract text/), "Supplier: Acme");
    await userEvent.click(screen.getByLabelText(/Document-first/));
    await userEvent.click(screen.getByRole("button", { name: "Run" }));
    expect(runPlayground).toHaveBeenLastCalledWith(expect.objectContaining({ workflow: "extraction", input: { text: "Supplier: Acme", documentFirst: false } }));
  });

  it("shows each extraction stage's raw payload and a drafted email", async () => {
    const extraction = run({
      workflow: "extraction",
      reply: null,
      result: {
        pages: 1,
        characters: 40,
        documentFirst: true,
        stages: [
          { stage: "Metadata", ok: true, error: null, model: "gpt-5.4-nano-dev", payload: { supplierName: "Acme" } },
          { stage: "Risk", ok: false, error: "JsonException: bad", model: null, payload: null },
        ],
      },
    });
    const runPlayground = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, error: null, body: extraction });
    render(<PlaygroundPanel apiClient={client(runPlayground)} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.selectOptions(screen.getByLabelText("Workflow"), "extraction");
    await userEvent.type(screen.getByLabelText(/Contract text/), "x");
    await userEvent.click(screen.getByRole("button", { name: "Run" }));

    const result = await screen.findByLabelText("Extraction result");
    expect(result).toHaveTextContent("Metadata");
    expect(result).toHaveTextContent('"supplierName": "Acme"');
    expect(result).toHaveTextContent("JsonException: bad");
  });

  it("changes any registered prompt in a variant, not only the orchestrator's", async () => {
    const savePlaygroundVariant = vi.fn().mockResolvedValue({ ok: true, statusCode: 201, body: VARIANT, error: null });
    render(<PlaygroundPanel apiClient={client(vi.fn(), { savePlaygroundVariant })} />);
    await screen.findByText("Four validated contracts.");

    await userEvent.click(screen.getByRole("button", { name: "Variants" }));
    await userEvent.click(screen.getByRole("button", { name: "New variant" }));
    await userEvent.type(screen.getByLabelText("Name"), "Sharper analyst");
    await userEvent.selectOptions(screen.getByLabelText("Prompt to change"), "contract-analyst");
    const prompt = screen.getByLabelText(/Prompt text/);
    expect(prompt).toHaveValue("You analyse contracts.");
    await userEvent.clear(prompt);
    await userEvent.type(prompt, "Be sharper.");
    await userEvent.click(screen.getByRole("button", { name: "Save variant" }));

    expect(savePlaygroundVariant).toHaveBeenCalledWith(
      null,
      expect.objectContaining({ overrides: expect.objectContaining({ prompts: { "contract-analyst": "Be sharper." } }) }),
    );
  });

  const RUBRICS: PlaygroundRubricBody[] = [
    {
      id: "default:ask",
      name: "Ask answer",
      workflow: "ask",
      isDefault: true,
      criteria: [
        { id: "grounding", label: "Grounding", description: "From the evidence", weight: 5 },
        { id: "tone", label: "Tone", description: "Direct", weight: 2 },
      ],
    },
    {
      id: "rb-1",
      name: "My rubric",
      workflow: "ask",
      isDefault: false,
      criteria: [{ id: "usefulness", label: "Usefulness", description: "Actionable", weight: 4 }],
    },
  ];

  function judgement(overrides: Partial<PlaygroundJudgementBody> = {}): PlaygroundJudgementBody {
    return {
      judgementId: "j1",
      workflow: "ask",
      comparisonId: "cmp",
      createdAt: "2026-11-19T10:00:00Z",
      rubric: RUBRICS[0],
      judgeModel: "gpt-5.4-dev",
      passes: 1,
      runs: [
        { runId: "run-base", variantId: null, variantName: "Baseline" },
        { runId: "run-v1", variantId: "v1", variantName: "Short prompt" },
      ],
      answers: [
        { runId: "run-base", variantName: "Baseline", weighted: 3.1, summary: "Adequate.", scores: [{ criterionId: "grounding", score: 3, rationale: "One figure unsupported." }, { criterionId: "tone", score: 4, rationale: "Direct." }] },
        { runId: "run-v1", variantName: "Short prompt", weighted: 4.6, summary: "Better grounded.", scores: [{ criterionId: "grounding", score: 5, rationale: "All figures in evidence." }, { criterionId: "tone", score: 4, rationale: "Direct." }] },
      ],
      preferredRunId: "run-v1",
      tie: false,
      passesAgreed: true,
      passDetails: [{ order: ["run-v1", "run-base"], preferredRunId: "run-v1", tie: false, reasoning: "The second answer is grounded." }],
      failures: [],
      usage: { promptTokens: 3000, completionTokens: 400, estimatedCostUsd: 0.004 },
      trace: [],
      ...overrides,
    };
  }

  /** Runs one question on two lanes (baseline and "Short prompt") with distinct run ids, ready to be judged. */
  async function twoLaneRun(judgePlayground: ApiClient["judgePlayground"]) {
    const runPlayground = vi.fn().mockImplementation(async (input: { variantId?: string; comparisonId: string }) => ({
      ok: true,
      statusCode: 200,
      error: null,
      body: run({ runId: input.variantId ? "run-v1" : "run-base", comparisonId: input.comparisonId }),
    }));
    render(
      <PlaygroundPanel
        apiClient={client(runPlayground, {
          listPlaygroundRubrics: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { rubrics: RUBRICS }, error: null }),
          judgePlayground,
        })}
      />,
    );
    await screen.findByText("Four validated contracts.");
    await userEvent.click(screen.getByLabelText("Short prompt"));
    await userEvent.type(screen.getByLabelText("Question"), "Quando scade?");
    await userEvent.click(screen.getByRole("button", { name: "Send to 2 variants" }));
    await screen.findAllByText("Scade il 15 gennaio 2027 [1].");
    return runPlayground;
  }

  it("judges the answers of the selected question and shows scores, the preferred variant and the cost", async () => {
    const judgePlayground = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: judgement(), error: null });
    await twoLaneRun(judgePlayground);

    await userEvent.click(await screen.findByRole("button", { name: "Judge" }));

    expect(judgePlayground).toHaveBeenCalledWith({ runIds: ["run-base", "run-v1"], rubricId: "default:ask", passes: 1 });
    const panel = await screen.findByLabelText("Judgement");
    expect(panel).toHaveTextContent("Preferred: Short prompt");
    expect(panel).toHaveTextContent("$0.0040");
    const scores = within(panel).getByRole("table", { name: "Scores" });
    expect(scores).toHaveTextContent("Weighted (1–5)");
    expect(scores).toHaveTextContent("4.60");
    expect(scores).toHaveTextContent("3.10");
    expect(panel).toHaveTextContent("All figures in evidence.");
  });

  it("sends the chosen rubric, judge model and two passes, and warns when the verdict depends on the order", async () => {
    const judgePlayground = vi
      .fn()
      .mockResolvedValue({ ok: true, statusCode: 200, body: judgement({ passes: 2, passesAgreed: false, tie: true, preferredRunId: null }), error: null });
    await twoLaneRun(judgePlayground);

    await userEvent.selectOptions(await screen.findByLabelText("Rubric"), "rb-1");
    await userEvent.selectOptions(screen.getByLabelText("Judge model"), "gpt-5.4-dev");
    await userEvent.click(screen.getByLabelText(/Check order bias/));
    await userEvent.click(screen.getByRole("button", { name: "Judge" }));

    expect(judgePlayground).toHaveBeenCalledWith({ runIds: ["run-base", "run-v1"], rubricId: "rb-1", judgeModelId: "gpt-5.4-dev", passes: 2 });
    const panel = await screen.findByLabelText("Judgement");
    expect(panel).toHaveTextContent("Tie");
    expect(within(panel).getByRole("alert")).toHaveTextContent("depends on the order");
  });

  it("shows the earlier judgement of a comparison and why a judge call failed", async () => {
    const judgePlayground = vi.fn().mockResolvedValue({ ok: false, statusCode: 400, body: null, error: "The runs must be of the same workflow." });
    const runPlayground = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, error: null, body: run({ comparisonId: "cmp" }) });
    render(
      <PlaygroundPanel
        apiClient={client(runPlayground, {
          judgePlayground,
          listPlaygroundJudgements: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { judgements: [judgement()] }, error: null }),
        })}
      />,
    );
    await screen.findByText("Four validated contracts.");
    await userEvent.type(screen.getByLabelText("Question"), "Ciao");
    await userEvent.click(screen.getByRole("button", { name: "Send" }));
    await screen.findByText("Scade il 15 gennaio 2027 [1].");

    expect(await screen.findByLabelText("Judgement")).toHaveTextContent("Preferred: Short prompt");
    await userEvent.click(screen.getByRole("button", { name: "Judge again" }));
    expect(await screen.findByText("The runs must be of the same workflow.")).toBeInTheDocument();
  });

  it("lists built-in and custom rubrics and saves a copy of a built-in one as a custom rubric", async () => {
    const savePlaygroundRubric = vi.fn().mockResolvedValue({ ok: true, statusCode: 201, body: RUBRICS[1], error: null });
    render(
      <PlaygroundPanel
        apiClient={client(vi.fn(), {
          listPlaygroundRubrics: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { rubrics: RUBRICS }, error: null }),
          savePlaygroundRubric,
        })}
      />,
    );
    await screen.findByText("Four validated contracts.");

    await userEvent.click(screen.getByRole("button", { name: "Rubrics" }));
    const table = await screen.findByLabelText("Rubrics");
    expect(table).toHaveTextContent("Ask answer");
    expect(table).toHaveTextContent("My rubric");
    expect(table).toHaveTextContent("Grounding ×5, Tone ×2");

    await userEvent.click(screen.getByRole("button", { name: "Copy and edit" }));
    expect(screen.getByLabelText("Name")).toHaveValue("Ask answer (custom)");
    await userEvent.selectOptions(screen.getByLabelText("Criterion 2 weight"), "5");
    await userEvent.click(screen.getByRole("button", { name: "Save rubric" }));

    expect(savePlaygroundRubric).toHaveBeenCalledWith(null, {
      name: "Ask answer (custom)",
      workflow: "ask",
      criteria: [
        { label: "Grounding", description: "From the evidence", weight: 5 },
        { label: "Tone", description: "Direct", weight: 5 },
      ],
    });
  });

  it("edits a custom rubric in place and refuses to save one with an empty criterion", async () => {
    const savePlaygroundRubric = vi.fn().mockResolvedValue({ ok: false, statusCode: 400, body: null, error: "A criterion needs a label." });
    render(
      <PlaygroundPanel
        apiClient={client(vi.fn(), {
          listPlaygroundRubrics: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { rubrics: RUBRICS }, error: null }),
          savePlaygroundRubric,
        })}
      />,
    );
    await screen.findByText("Four validated contracts.");
    await userEvent.click(screen.getByRole("button", { name: "Rubrics" }));
    await screen.findByLabelText("Rubrics");

    await userEvent.click(screen.getByRole("button", { name: "Edit" }));
    await userEvent.click(screen.getByRole("button", { name: "Add criterion" }));
    expect(screen.getByRole("button", { name: "Save rubric" })).toBeDisabled();
    await userEvent.type(screen.getByLabelText("Criterion 2 label"), "Brevity");
    await userEvent.click(screen.getByRole("button", { name: "Save rubric" }));

    expect(savePlaygroundRubric).toHaveBeenCalledWith("rb-1", expect.objectContaining({ name: "My rubric" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("A criterion needs a label.");
  });

  async function openExport(extra: Partial<ApiClient>, variant: PlaygroundVariantBody = VARIANT) {
    render(
      <PlaygroundPanel
        apiClient={client(vi.fn(), {
          listPlaygroundVariants: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { variants: [variant] }, error: null }),
          listPlaygroundExports: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { exports: [] }, error: null }),
          ...extra,
        })}
      />,
    );
    await screen.findByText("Four validated contracts.");
    await userEvent.click(screen.getByRole("button", { name: "Variants" }));
    await userEvent.click(await screen.findByRole("button", { name: "Propose…" }));
    return screen.findByLabelText("Export variant");
  }

  it("opens a draft pull request for a variant and links to it, saying what is and is not applied", async () => {
    const exportPlaygroundVariant = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      error: null,
      body: { exportId: "e1", target: "pull_request", status: "opened", prUrl: "https://github.com/acme/raffa/pull/7", prNumber: 7, branch: "playground/short-prompt-1", files: ["a", "b", "c"], notes: ["A prompt has to be applied by hand."] },
    });
    const panel = await openExport({
      getPlaygroundExportCapabilities: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { pullRequest: true, repository: "acme/raffa" }, error: null }),
      exportPlaygroundVariant,
    });

    expect(panel).toHaveTextContent("Prompts: agent (the orchestrator becomes a new version in code)");
    expect(panel).toHaveTextContent("a draft on a new branch that a person reviews");
    await userEvent.click(await screen.findByRole("button", { name: "Open a draft pull request on acme/raffa" }));

    expect(exportPlaygroundVariant).toHaveBeenCalledWith("v1", "pull_request");
    const status = await screen.findByRole("status");
    expect(within(status).getByRole("link", { name: "#7" })).toHaveAttribute("href", "https://github.com/acme/raffa/pull/7");
    expect(status).toHaveTextContent("playground/short-prompt-1");
    expect(status).toHaveTextContent("A prompt has to be applied by hand.");
  });

  it("offers only the bundle when GitHub is not connected, and downloads it", async () => {
    const createObjectURL = vi.fn().mockReturnValue("blob:x");
    const revokeObjectURL = vi.fn();
    Object.assign(URL, { createObjectURL, revokeObjectURL });
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);
    const exportPlaygroundVariant = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      error: null,
      body: { exportId: "e2", target: "bundle", status: "created", files: ["README.md"], fileName: "2026-11-19-short-prompt.zip", zipBase64: btoa("PK") },
    });
    await openExport({
      getPlaygroundExportCapabilities: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { pullRequest: false, repository: null }, error: null }),
      exportPlaygroundVariant,
    });

    expect(await screen.findByText(/GitHub is not connected here/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Open a draft pull request/ })).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: "Download bundle (.zip)" }));

    expect(exportPlaygroundVariant).toHaveBeenCalledWith("v1", "bundle");
    expect(await screen.findByRole("status")).toHaveTextContent("Bundle downloaded (1 files).");
    expect(createObjectURL).toHaveBeenCalledTimes(1);
    expect(click).toHaveBeenCalledTimes(1);
    click.mockRestore();
  });

  it("shows the reason GitHub refused, from the server's error body", async () => {
    const exportPlaygroundVariant = vi.fn().mockResolvedValue({
      ok: false,
      statusCode: 502,
      body: null,
      error: JSON.stringify({ error: "GitHub answered 403 while trying to write x. The token needs Contents and Pull requests read and write.", exportId: "e3" }),
    });
    await openExport({
      getPlaygroundExportCapabilities: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { pullRequest: true, repository: "acme/raffa" }, error: null }),
      exportPlaygroundVariant,
    });

    await userEvent.click(await screen.findByRole("button", { name: /Open a draft pull request/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The token needs Contents and Pull requests read and write.");
  });

  it("has nothing to propose for a variant that changes nothing, and lists earlier exports", async () => {
    const empty: PlaygroundVariantBody = { ...VARIANT, overrides: { prompts: {}, models: {}, disabledTools: [], maxToolCalls: null } };
    const panel = await openExport(
      {
        listPlaygroundExports: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          error: null,
          body: { exports: [{ id: "e0", target: "pull_request", status: "opened", branch: "b", prNumber: 5, prUrl: "https://github.com/acme/raffa/pull/5", error: null, files: [], createdAt: "2026-11-18T10:00:00Z" }] },
        }),
      },
      empty,
    );

    expect(panel).toHaveTextContent("changes nothing");
    expect(screen.getByRole("button", { name: "Download bundle (.zip)" })).toBeDisabled();
    expect(await screen.findByText(/Earlier exports of this variant \(1\)/)).toBeInTheDocument();
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
