import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { registerAskOfferTransport, type ActionProposal, type ApiClient, type ConversationDetail4 } from "../../../src/api/client";
import AskRoute from "../../../src/routes/ask";
import { pendingProposal, proposalAnswer } from "../../../src/routes/ask/proposal";

function conversation(proposal: ActionProposal, text: string): ConversationDetail4 {
  return {
    id: "A",
    title: "Splunk",
    customTitle: null,
    scopeContractId: null,
    createdAt: "2026-11-04T09:00:00Z",
    updatedAt: "2026-11-04T10:00:00Z",
    archivedAt: null,
    caseId: null,
    messages: [
      { id: "m1", role: "you", kind: "question", markdown: "avvia il rinnovo con queste leve", citations: [], actions: [], createdAt: "2026-11-04T10:00:00Z", payload: null, interview: null },
      { id: "m2", role: "raffa", kind: "answer", markdown: text, citations: [], actions: [], createdAt: "2026-11-04T10:00:01Z", payload: { actionProposal: proposal }, interview: null },
    ],
  };
}

function renderAsk(proposal: ActionProposal, overrides: Record<string, unknown> = {}) {
  const detail = conversation(proposal, "Posso avviare il rinnovo di Splunk. Confermi?");
  const asked: string[] = [];
  const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input));
    if (init?.method === "POST" && url.pathname === "/api/conversations/ask") asked.push(JSON.parse(String(init.body)).question);
    if (url.pathname === "/api/conversations") return json(url.searchParams.get("state") === "recent" ? [{ ...detail, archived: false, questionCount: 1 }] : []);
    if (/^\/api\/conversations\/A$/.test(url.pathname)) return json(detail);
    return json([]);
  });
  const client = {
    getWorkspaceSettings: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, settings: null, error: null }),
    getRenewalsBoard: vi.fn().mockResolvedValue({
      ok: true,
      body: { viewer: { userId: "u-cpo", isApprover: true }, assignee: { userId: "u-marta" } },
    }),
    openRenewalCase: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, data: { caseId: "case-1" }, error: null }),
    confirmSavingsGoal: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, data: {}, error: null }),
    ...overrides,
  } as unknown as ApiClient & { openRenewalCase: ReturnType<typeof vi.fn>; confirmSavingsGoal: ReturnType<typeof vi.fn> };
  registerAskOfferTransport(client, { baseUrl: "http://api.test", getAccessToken: async () => null, fetch });
  render(
    <MemoryRouter initialEntries={["/ask/A"]}>
      <Routes>
        <Route element={<Outlet context={{ workspaceId: "t1", kbReady: true, validatedContractCount: 1 }} />}>
          <Route path="/ask/:conversationId" element={<AskRoute apiClient={client} />} />
          <Route path="/renewals/:caseId" element={<div>Case page</div>} />
          <Route path="/savings" element={<div>Savings page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
  return { client, asked };
}

const RENEWAL: ActionProposal = { kind: "start_renewal", contractId: "c-splunk", supplier: "Splunk" };
const GOAL: ActionProposal = { kind: "set_savings_goal", goalText: "risparmiare il 15% quest'anno" };

describe("Ask Raffa asks before acting", () => {
  it("runs nothing until the user confirms", async () => {
    const { client } = renderAsk(RENEWAL);
    expect(await screen.findByRole("button", { name: "Yes, start the renewal" })).toBeInTheDocument();
    expect(client.openRenewalCase).not.toHaveBeenCalled();
  });

  it("a typed yes starts the renewal like Rinnovi does and opens its case, without asking Raffa again", async () => {
    const { client, asked } = renderAsk(RENEWAL);
    const input = await screen.findByPlaceholderText(/When must we give notice/);
    fireEvent.change(input, { target: { value: "sì" } });
    fireEvent.click(screen.getByRole("button", { name: "Ask" }));
    expect(await screen.findByText("Case page")).toBeInTheDocument();
    // The CPO's case goes to the colleague set up for it, as "Start & assign" does, and carries this chat's levers.
    expect(client.openRenewalCase).toHaveBeenCalledWith("t1", { contractId: "c-splunk", ownerUserId: "u-marta", conversationId: "A" });
    expect(asked).toEqual([]);
  });

  it("the confirm button does the same", async () => {
    const { client } = renderAsk(RENEWAL);
    fireEvent.click(await screen.findByRole("button", { name: "Yes, start the renewal" }));
    await waitFor(() => expect(client.openRenewalCase).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("Case page")).toBeInTheDocument();
  });

  it("sets the stated objective as the savings goal on yes, and offers Spend and savings", async () => {
    const { client } = renderAsk(GOAL);
    fireEvent.click(await screen.findByRole("button", { name: "Yes, set the goal" }));
    expect(await screen.findByText("Savings goal set.")).toBeInTheDocument();
    expect(client.confirmSavingsGoal).toHaveBeenCalledWith("t1", { text: "risparmiare il 15% quest'anno" });
    fireEvent.click(screen.getByRole("button", { name: "Open Spend and savings" }));
    expect(await screen.findByText("Savings page")).toBeInTheDocument();
  });

  it("a no changes nothing", async () => {
    const { client } = renderAsk(RENEWAL);
    const input = await screen.findByPlaceholderText(/When must we give notice/);
    fireEvent.change(input, { target: { value: "no" } });
    fireEvent.click(screen.getByRole("button", { name: "Ask" }));
    expect(await screen.findByText("Okay, nothing changed.")).toBeInTheDocument();
    expect(client.openRenewalCase).not.toHaveBeenCalled();
  });

  it("shows why it failed and leaves the buttons to retry", async () => {
    const { client } = renderAsk(RENEWAL, { openRenewalCase: vi.fn().mockResolvedValue({ ok: false, statusCode: 409, data: null, error: "Not allowed" }) });
    fireEvent.click(await screen.findByRole("button", { name: "Yes, start the renewal" }));
    expect(await screen.findByText("Not allowed")).toBeInTheDocument();
    expect(client.openRenewalCase).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "Yes, start the renewal" })).toBeEnabled();
  });
});

describe("a renewal the user commands in chat starts by itself", () => {
  function renderCommand(askReply: ConversationDetail4) {
    const before: ConversationDetail4 = { ...askReply, messages: askReply.messages.slice(0, 2) };
    const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
    const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input));
      if (init?.method === "POST" && url.pathname === "/api/conversations/ask") return json({ conversation: askReply, needsContract: false });
      if (url.pathname === "/api/conversations") return json(url.searchParams.get("state") === "recent" ? [{ ...before, archived: false, questionCount: 1 }] : []);
      if (/^\/api\/conversations\/A$/.test(url.pathname)) return json(before);
      return json([]);
    });
    const client = {
      getWorkspaceSettings: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, settings: null, error: null }),
      getRenewalsBoard: vi.fn().mockResolvedValue({ ok: true, body: { viewer: { userId: "u-luca", isApprover: false }, assignee: null } }),
      openRenewalCase: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, data: { caseId: "case-9" }, error: null }),
    } as unknown as ApiClient & { openRenewalCase: ReturnType<typeof vi.fn> };
    registerAskOfferTransport(client, { baseUrl: "http://api.test", getAccessToken: async () => null, fetch });
    render(
      <MemoryRouter initialEntries={["/ask/A"]}>
        <Routes>
          <Route element={<Outlet context={{ workspaceId: "t1", kbReady: true, validatedContractCount: 1 }} />}>
            <Route path="/ask/:conversationId" element={<AskRoute apiClient={client} />} />
            <Route path="/renewals/:caseId" element={<div>Case page</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    return { client };
  }

  function withCommand(proposal: ActionProposal): ConversationDetail4 {
    const base = conversation(RENEWAL, "Strategia per Workday: sconto del 27,7%.");
    base.messages[1] = { ...base.messages[1]!, payload: null };
    return {
      ...base,
      messages: [
        ...base.messages,
        { id: "m3", role: "you", kind: "question", markdown: "avvia il rinnovo con questi termini", citations: [], actions: [], createdAt: "2026-11-04T10:01:00Z", payload: null, interview: null },
        { id: "m4", role: "raffa", kind: "answer", markdown: "Avvio il rinnovo di **Workday**.", citations: [], actions: [], createdAt: "2026-11-04T10:01:01Z", payload: { actionProposal: proposal }, interview: null },
      ],
    };
  }

  it("opens the case with this conversation's context, with nothing to click", async () => {
    const { client } = renderCommand(withCommand({ kind: "start_renewal", contractId: "c-workday", supplier: "Workday", autoRun: true }));
    const input = await screen.findByPlaceholderText(/When must we give notice/);
    fireEvent.change(input, { target: { value: "avvia il rinnovo con questi termini" } });
    fireEvent.click(screen.getByRole("button", { name: "Ask" }));
    expect(await screen.findByText("Case page")).toBeInTheDocument();
    expect(client.openRenewalCase).toHaveBeenCalledWith("t1", { contractId: "c-workday", ownerUserId: "u-luca", conversationId: "A" });
  });

  it("a proposal that is not a command still waits for the yes", async () => {
    const { client } = renderCommand(withCommand({ kind: "start_renewal", contractId: "c-workday", supplier: "Workday" }));
    const input = await screen.findByPlaceholderText(/When must we give notice/);
    fireEvent.change(input, { target: { value: "avvia il rinnovo con questi termini" } });
    fireEvent.click(screen.getByRole("button", { name: "Ask" }));
    expect(await screen.findByRole("button", { name: "Yes, start the renewal" })).toBeInTheDocument();
    expect(client.openRenewalCase).not.toHaveBeenCalled();
  });
});

describe("proposal replies", () => {
  it("reads plain yes / no and nothing else", () => {
    for (const yes of ["sì", "Si", "ok", "va bene", "procedi", "Yes.", "go ahead"]) expect(proposalAnswer(yes)).toBe("yes");
    for (const no of ["no", "annulla", "Cancel", "not now"]) expect(proposalAnswer(no)).toBe("no");
    expect(proposalAnswer("sì ma prima dimmi la scadenza")).toBeNull();
    expect(proposalAnswer("quando scade?")).toBeNull();
  });

  it("only the last message can hold a pending proposal", () => {
    const detail = conversation(RENEWAL, "x");
    expect(pendingProposal(detail.messages)?.id).toBe("m2");
    expect(pendingProposal([...detail.messages, { ...detail.messages[0]!, id: "m3" }])).toBeNull();
  });
});
