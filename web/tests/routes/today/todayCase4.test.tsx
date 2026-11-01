import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ApiClient } from "../../../src/api/client";
import type { CaseEnvelope, CaseCallResult, RenewalsBoard, TodayFeed } from "../../../src/api/renewalCases";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import { trackMissingItalian } from "../../../src/i18n/translate";
import TodayRoute from "../../../src/routes/today";
import RenewalsRoute from "../../../src/routes/renewals";
import CaseRoute from "../../../src/routes/renewals/case/CaseRoute";

const TENANT = "11111111-1111-1111-1111-111111111111";
const ok = <T,>(body: T): CaseCallResult<T> => ({ ok: true, statusCode: 200, body, error: null });

const viewer = { userId: "u-luca", name: "Luca Lamalfa", title: "Head of Procurement", isApprover: true, date: "08 Sep 2026" };

const feed: TodayFeed = {
  viewer,
  emptyWorkspace: false,
  mine: [
    {
      key: "c1:unassigned", kind: "unassigned", caseId: null, contractId: "c1", quoteId: null, documentId: null,
      supplier: "Salesforce", contract: "Sales Cloud Enterprise",
      title: { id: "Notice deadline without an owner" },
      why: { id: "Renews by itself for 12 months if nothing happens by {date}. Start the renewal and assign it.", vars: { date: "18 Oct 2026" } },
      due: "18 Oct 2026", dueDays: 40, dueLabel: null, stage: -1, stageLabel: { id: "Not started" }, who: "Luca Lamalfa", stalled: 0,
    },
  ],
  waiting: [],
  snoozed: [],
  nextDeadline: null,
  assignee: { userId: "u-marta", name: "Marta Keller" },
  documents: [],
};

const envelope: CaseEnvelope = {
  caseId: "case-1", contractId: "c1", version: 3, clip: null, tick: false, closed: false, demo: true,
  capabilities: { canWork: true, canApprove: true, canClose: true },
  view: {
    hdr: { title: "x", supplier: "Salesforce", contract: "Sales Cloud", status: "s", next: "Tu — Rispondi", approver: "Luca Lamalfa", noticeBy: "18 Oct 2026", noticeDays: 40, end: "15 Jan 2027", owner: "Luca Lamalfa", warn: null, people: [], youRole: "" },
    summary: { goals: [], terms: [], decisions: [], refs: "" },
    upcoming: [], emailsList: [], docsList: [], reply: { open: false, text: "", from: "" }, stage: 0,
    permissions: { canWork: true, canApprove: true, canClose: true, role: "approver" },
    page: {
      title: "Rinnovo · Salesforce · Sales Cloud", phase: "Fase 1/6 · Avvio", next: "Tu — Rispondi", notice: "18 Oct 2026 · 40 g", noticeUrgent: false, end: "15 Jan 2027",
      warn: null, whoCan: "", done: [{ kicker: "Caso", title: "Aperto", note: "", lines: ["Come funziona"], rows: [], steps: [], chips: [] }], msgs: [],
      current: {
        k: "q", kicker: "Domanda 1 / 4", title: "Prodotti?", banner: "", lines: [], rows: [], table: null, steps: [], email: null, pre: "",
        fields: [], chips: [], inputs: [], note: "", bullets: [], stats: [],
        buttons: [
          { label: "Uno", act: "a", p: {}, primary: true, off: false },
          { label: "Due", act: "b", p: {}, primary: false, off: false },
          { label: "Tre", act: "c", p: {}, primary: false, off: false },
        ],
        more: [{ label: "Prepara la disdetta", act: "startNotice", p: {}, primary: false, off: false }],
      },
    },
  },
};

function renderAt(path: string, client: Partial<ApiClient>) {
  const api = { uploadSampleDocument: vi.fn(), openRenewalCase: vi.fn(), ...client } as unknown as ApiClient;
  render(
    <MemoryRouter initialEntries={[path]}>
      <I18nProvider>
        <Routes>
          <Route path="/today" element={<TodayRoute apiClient={api} />} />
          <Route path="/renewals" element={<RenewalsRoute apiClient={api} />} />
          <Route path="/renewals/:caseId" element={<CaseRoute apiClient={api} />} />
        </Routes>
      </I18nProvider>
    </MemoryRouter>,
  );
  return api;
}

const missing: string[] = [];
beforeEach(() => {
  window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: TENANT, name: "Helvetia Foods AG" }));
  window.localStorage.setItem("raffa4.lang", "it");
  missing.length = 0;
  trackMissingItalian((id) => missing.push(id));
});
afterEach(() => {
  window.sessionStorage.clear();
  window.localStorage.clear();
  trackMissingItalian(null);
});

describe("Oggi", () => {
  it("shows one focus card with at most three buttons, in Italian", async () => {
    renderAt("/today", { getToday: vi.fn().mockResolvedValue(ok(feed)) });
    const focus = await waitFor(() => {
      const el = document.querySelector('[data-screen-label="Focus"]');
      expect(el).not.toBeNull();
      return el as HTMLElement;
    });
    expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent("Oggi hai 1 cosa da fare.");
    const buttons = within(focus).getAllByRole("button").map((b) => b.textContent);
    expect(buttons).toEqual(["Avvia e assegna a Marta", "Me ne occupo io", "Più tardi · 3 giorni"]);
    expect(within(focus).getByText("Scadenza di disdetta senza responsabile")).toBeInTheDocument();
    expect(missing).toEqual([]);
  });

  it("starts and assigns the case to the buyer, then opens it", async () => {
    const openRenewalCase = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, data: { caseId: "case-1" }, error: null });
    renderAt("/today", { getToday: vi.fn().mockResolvedValue(ok(feed)), openRenewalCase, getRenewalCase: vi.fn().mockResolvedValue(ok(envelope)) });
    await userEvent.click(await screen.findByRole("button", { name: "Avvia e assegna a Marta" }));
    expect(openRenewalCase).toHaveBeenCalledWith(TENANT, { contractId: "c1", ownerUserId: "u-marta" });
    expect(await screen.findByText("Prodotti?")).toBeInTheDocument();
  });

  it("shows Punto zero on an empty workspace", async () => {
    renderAt("/today", { getToday: vi.fn().mockResolvedValue(ok({ ...feed, emptyWorkspace: true, mine: [] })) });
    expect(await screen.findByText("Carica un contratto. Tutto il resto parte da lì.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Carica il primo contratto" })).toBeInTheDocument();
  });
});

describe("Rinnovi", () => {
  it("groups the rows and labels the notice deadline", async () => {
    const board: RenewalsBoard = {
      viewer, openCases: 1, toStart: 0, empty: false, assignee: null, cases: [],
      groups: [{ key: "mine", label: { id: "Your turn" }, items: [{ contractId: "c4", caseId: "k4", supplier: "DocuSign", contract: "eSignature", stage: 2, stageLabel: { id: "Negotiation" }, next: "Luca Lamalfa — Approva", notice: "02 Oct 2026", days: 24, startBy: "", owner: "Marta Keller", myTurn: true, hasCase: true, done: false, waiting: false }] }],
    } as RenewalsBoard;
    renderAt("/renewals", { getRenewalsBoard: vi.fn().mockResolvedValue(ok(board)) });
    expect(await screen.findByText("Tocca a te")).toBeInTheDocument();
    expect(screen.getByText("Disdetta entro")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Fai ora" })).toBeInTheDocument();
  });
});

describe("Pratica", () => {
  it("shows at most three step buttons and the rest under Altro, and posts actions with the version", async () => {
    const post = vi.fn().mockResolvedValue(ok(envelope));
    renderAt("/renewals/case-1", { getRenewalCase: vi.fn().mockResolvedValue(ok(envelope)), postRenewalCaseAction: post });
    const step = await waitFor(() => {
      const el = document.querySelector('[data-screen-label="Passo corrente"]');
      expect(el).not.toBeNull();
      return el as HTMLElement;
    });
    const visible = within(step).getAllByRole("button").filter((b) => !/Altro|Meno/.test(b.textContent ?? ""));
    expect(visible).toHaveLength(3);
    await userEvent.click(within(step).getByRole("button", { name: "Altro ▾" }));
    await userEvent.click(within(step).getByRole("button", { name: "Prepara la disdetta" }));
    expect(post).toHaveBeenCalledWith(TENANT, "case-1", "startNotice", {}, 3);
  });
});
