import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ApiClient, PlaygroundRunBody } from "../../api/client";
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

function client(runPlayground: ApiClient["runPlayground"]): ApiClient {
  return {
    listPlaygroundSandboxes: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, body: { sandboxes: SANDBOXES }, error: null }),
    runPlayground,
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

    expect(runPlayground).toHaveBeenCalledWith({ sandbox: "seeded", question: "Quando scade Salesforce?", history: [] });
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
