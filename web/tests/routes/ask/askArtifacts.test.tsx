import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import {
  registerAskOfferTransport,
  type ApiClient,
  type ContractListRowV4Body,
  type ConversationDetail4,
  type ConversationMessage4,
} from "../../../src/api/client";
import AskRoute from "../../../src/routes/ask";

const ROWS: ContractListRowV4Body[] = [
  {
    contractId: "c1",
    supplier: "Salesforce",
    name: "Sales Cloud — MSA",
    noticeDeadline: "2026-12-01",
    noticeDays: 27,
    end: "2027-03-01",
    annualValue: 120000,
    currency: "CHF",
    status: "validated",
    caseId: null,
    termStatus: "active",
  },
];

let seq = 0;
const message = (role: "you" | "raffa", markdown: string): ConversationMessage4 => ({
  id: `m${++seq}`,
  role,
  kind: role === "raffa" ? "answer" : "question",
  markdown,
  citations: [],
  actions: [],
  createdAt: "2026-11-04T10:00:00Z",
  payload: null,
  interview: null,
});

const conversation = (id: string, title: string, messages: ConversationMessage4[]): ConversationDetail4 => ({
  id,
  title,
  customTitle: null,
  scopeContractId: null,
  createdAt: "2026-11-04T09:00:00Z",
  updatedAt: "2026-11-04T10:00:00Z",
  archivedAt: null,
  caseId: null,
  messages,
});

/** Two saved conversations and a backend that answers every question in plain text. */
function renderAsk(start: string) {
  const store = new Map<string, ConversationDetail4>([
    ["A", conversation("A", "Notice for Salesforce", [message("you", "When is the Salesforce notice?"), message("raffa", "By 1 December 2026.")])],
    ["B", conversation("B", "GitLab spend", [message("you", "How much do we spend on GitLab?"), message("raffa", "CHF 101k a year.")])],
  ]);
  const asked: string[] = [];
  const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input));
    if (init?.method === "POST" && url.pathname === "/api/conversations/ask") {
      const body = JSON.parse(String(init.body)) as { conversationId: string | null; question: string };
      asked.push(body.question);
      const id = body.conversationId ?? "N";
      const before = store.get(id) ?? conversation(id, body.question, []);
      const after = { ...before, messages: [...before.messages, message("you", body.question), message("raffa", `Answer to: ${body.question}`)] };
      store.set(id, after);
      return json({ conversationId: id, needsContract: false, conversation: after, capabilityCheck: null });
    }
    if (url.pathname === "/api/conversations") {
      const rows = url.searchParams.get("state") === "recent" ? [...store.values()] : [];
      return json(rows.map((c) => ({ ...c, archived: false, questionCount: c.messages.filter((m) => m.role === "you").length })));
    }
    const one = /^\/api\/conversations\/([^/]+)$/.exec(url.pathname);
    if (one) return json(store.get(decodeURIComponent(one[1]!)));
    return json([]);
  });
  const client = {
    getWorkspaceSettings: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, settings: null, error: null }),
    listContractsV4: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, data: { rows: ROWS, document: null }, error: null }),
  } as unknown as ApiClient;
  registerAskOfferTransport(client, { baseUrl: "http://api.test", getAccessToken: async () => null, fetch });
  render(
    <MemoryRouter initialEntries={[start]}>
      <Routes>
        <Route element={<Outlet context={{ workspaceId: "t1", kbReady: true, validatedContractCount: 1 }} />}>
          <Route path="/ask" element={<AskRoute apiClient={client} />} />
          <Route path="/ask/:conversationId" element={<AskRoute apiClient={client} />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
  return { asked };
}

/** The chat rows, top to bottom, as "who: text". */
function thread(): string[] {
  return [...document.querySelectorAll(".k6-thread .k6-qa")].map(
    (row) => `${row.querySelector(".k6-qa-by")?.textContent}: ${row.querySelector(".k6-qa-body")?.textContent}`,
  );
}

const cards = () => document.querySelectorAll(".k6-thread .artifact-card");

async function send(text: string) {
  const input = screen.getByPlaceholderText(/When must we give notice/);
  fireEvent.change(input, { target: { value: text } });
  fireEvent.click(screen.getByRole("button", { name: "Ask" }));
}

describe("Ask Raffa: what Raffa builds sits in its own conversation, at its own turn", () => {
  it("keeps a dashboard at the turn that asked for it: later answers come below it", async () => {
    renderAsk("/ask/A");
    expect(await screen.findByText("By 1 December 2026.")).toBeInTheDocument();

    await send("Crea una dashboard");
    await waitFor(() => expect(cards()).toHaveLength(1));
    await send("And the GitLab notice?");
    expect(await screen.findByText("Answer to: And the GitLab notice?")).toBeInTheDocument();

    expect(thread()).toEqual([
      "You: When is the Salesforce notice?",
      "Raffa.ai: By 1 December 2026.",
      "You: Crea una dashboard",
      "Raffa.ai: Contracts dashboardDashboard · Open",
      "You: And the GitLab notice?",
      "Raffa.ai: Answer to: And the GitLab notice?",
    ]);
  });

  it("starts a new conversation empty, and never shows one conversation's contents in another", async () => {
    renderAsk("/ask/A");
    expect(await screen.findByText("By 1 December 2026.")).toBeInTheDocument();
    await send("Crea una dashboard");
    await waitFor(() => expect(cards()).toHaveLength(1));
    await send("Fammi i KPI");
    await waitFor(() => expect(cards()).toHaveLength(2));

    fireEvent.click(screen.getByRole("button", { name: "+ New" }));
    expect(await screen.findByText("What do you want to know or do?")).toBeInTheDocument();
    expect(cards()).toHaveLength(0);
    expect(screen.queryByRole("complementary", { name: "KPIs" })).toBeNull();

    // Another saved conversation shows none of them; back in the first one, both are where they were.
    fireEvent.click(screen.getByRole("button", { name: /GitLab spend/ }));
    expect(await screen.findByText("CHF 101k a year.")).toBeInTheDocument();
    expect(cards()).toHaveLength(0);
    fireEvent.click(screen.getByRole("button", { name: /Notice for Salesforce/ }));
    expect(await screen.findByText("By 1 December 2026.")).toBeInTheDocument();
    expect(cards()).toHaveLength(2);
  });

  it("moves what was built in a new conversation into it once the first answer saves it", async () => {
    renderAsk("/ask");
    expect(await screen.findByText("What do you want to know or do?")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Show the KPIs/ }));
    await waitFor(() => expect(cards()).toHaveLength(1));
    await send("Which contracts renew first?");
    expect(await screen.findByText("Answer to: Which contracts renew first?")).toBeInTheDocument();

    expect(thread()).toEqual(["Raffa.ai: Contract KPIsKPIs · Open", "You: Which contracts renew first?", "Raffa.ai: Answer to: Which contracts renew first?"]);
    fireEvent.click(screen.getByRole("button", { name: "+ New" }));
    await waitFor(() => expect(cards()).toHaveLength(0));
  });

  it("answers a change to a KPI already built with its next version, not with text", async () => {
    const { asked } = renderAsk("/ask/A");
    expect(await screen.findByText("By 1 December 2026.")).toBeInTheDocument();
    await send("Fammi i KPI");
    await waitFor(() => expect(cards()).toHaveLength(1));

    await send("visualizza il kpi per anno, voglio capire il trend");
    await waitFor(() => expect(cards()).toHaveLength(2));
    await send("crea una nuova versione del KPI");
    await waitFor(() => expect(cards()).toHaveLength(3));

    expect(asked).toEqual([]);
    expect(thread().slice(-4)).toEqual([
      "You: visualizza il kpi per anno, voglio capire il trend",
      "Raffa.ai: Contract KPIsKPIs · Version 2 · Click to open",
      "You: crea una nuova versione del KPI",
      "Raffa.ai: Contract KPIsKPIs · Version 3 · Open",
    ]);
    expect(screen.getByRole("complementary", { name: "KPIs · Version 3" })).toBeInTheDocument();
  });
});
