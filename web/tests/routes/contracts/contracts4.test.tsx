import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ReactNode } from "react";
import type {
  ApiClient,
  ContractListV4Body,
  ContractV4Body,
  DocumentReadingBody,
  V4Result,
} from "../../../src/api/client";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import { trackMissingItalian } from "../../../src/i18n/translate";
import ContractsRoute from "../../../src/routes/contracts";
import Contract360Route from "../../../src/routes/contracts/contract360";
import ReviewRoute from "../../../src/routes/contracts/review";
import { contractsV4Mocks } from "../../helpers/contractsV4Mocks";

const TENANT = "11111111-1111-1111-1111-111111111111";
const CONTRACT = "22222222-2222-2222-2222-222222222222";
const DOC = "33333333-3333-3333-3333-333333333333";

const ok = <T,>(data: T): V4Result<T> => ({ ok: true, statusCode: 200, data, error: null });

function client(overrides: Partial<ApiClient>): ApiClient {
  return { ...contractsV4Mocks(), ...overrides } as unknown as ApiClient;
}

function renderAt(path: string, routes: ReactNode) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <I18nProvider>
        <Routes>
          {routes}
          <Route path="*" element={<div data-testid="elsewhere" />} />
        </Routes>
      </I18nProvider>
    </MemoryRouter>,
  );
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

const list: ContractListV4Body = {
  rows: [
    {
      contractId: CONTRACT,
      supplier: "Salesforce",
      name: "Sales Cloud Enterprise — MSA + Order Form",
      noticeDeadline: "18 Oct 2026",
      noticeDays: 40,
      end: "15 Jan 2027",
      annualValue: 640000,
      currency: "CHF",
      status: "caseOpen",
      caseId: "case-1",
    },
  ],
  document: { documentId: DOC, file: "Other.pdf", status: "review", flagged: 2 },
};

describe("Contratti", () => {
  it("lists validated contracts with notice deadline and end apart, value and status, plus the document banner", async () => {
    const api = client({ listContractsV4: vi.fn().mockResolvedValue(ok(list)) });
    renderAt("/contracts", <Route path="/contracts" element={<ContractsRoute apiClient={api} />} />);

    expect(await screen.findByText("Salesforce")).toBeInTheDocument();
    expect(screen.getByText("1 contratti validati · ordinati per scadenza di disdetta")).toBeInTheDocument();
    // D9: a table — notice date and days in their own columns, the column headers once at the top.
    expect(screen.getByText("18 Oct 2026")).toHaveClass("dt-urgent");
    expect(screen.getByText("40")).toHaveClass("dt-urgent");
    expect(screen.getAllByText("Fine contratto")[0].closest(".dt-th")).not.toBeNull();
    expect(screen.getAllByText("Giorni")[0].closest(".dt-th")).not.toBeNull();
    expect(screen.getByText("CHF 640k")).toBeInTheDocument();
    expect(screen.getAllByText("Pratica aperta").find((e) => e.classList.contains("tag"))).toHaveClass("tag-accent");
    expect(screen.getByText(/2 campi da verificare prima dell’uso/)).toBeInTheDocument();
    expect(api.listContractsV4).toHaveBeenCalledWith(TENANT);
    expect(missing).toEqual([]);
  });

  it("numbers, sorts and filters the rows", async () => {
    const mk = (id: string, supplier: string, days: number, value: number, status: ContractListV4Body["rows"][number]["status"]) => ({
      ...list.rows[0], contractId: id, supplier, noticeDays: days, annualValue: value, status, caseId: null,
    });
    const data: ContractListV4Body = { rows: [mk("1", "Coupa", 72, 281000, "caseOpen"), mk("2", "Zscaler", 133, 258000, "validated"), mk("3", "Microsoft", 139, 220000, "validated")], document: null };
    const api = client({ listContractsV4: vi.fn().mockResolvedValue(ok(data)) });
    renderAt("/contracts", <Route path="/contracts" element={<ContractsRoute apiClient={api} />} />);
    await screen.findByText("Coupa");
    const order = () => screen.getAllByRole("link").map((r) => r.querySelector(".c4-row-supplier")?.textContent).filter(Boolean);
    expect(order()).toEqual(["Coupa", "Zscaler", "Microsoft"]);
    expect(screen.getAllByLabelText(/^N\. \d$/).map((n) => n.textContent)).toEqual(["1", "2", "3"]);
    expect(screen.getByText("3 di 3")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /Fornitore · documento/ }));
    expect(order()).toEqual(["Coupa", "Microsoft", "Zscaler"]);
    await userEvent.click(screen.getByRole("button", { name: /Fornitore · documento/ }));
    expect(order()).toEqual(["Zscaler", "Microsoft", "Coupa"]);

    await userEvent.selectOptions(screen.getByLabelText("Stato"), "validated");
    expect(order()).toEqual(["Zscaler", "Microsoft"]);
    expect(screen.getByText("2 di 3")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Azzera filtri" }));
    await userEvent.type(screen.getByRole("searchbox"), "zzz");
    expect(screen.getByText("Nessun contratto corrisponde ai filtri.")).toBeInTheDocument();
    expect(missing).toEqual([]);
  });

  it("says so when nothing is validated yet", async () => {
    const api = client({ listContractsV4: vi.fn().mockResolvedValue(ok({ rows: [], document: null })) });
    renderAt("/contracts", <Route path="/contracts" element={<ContractsRoute apiClient={api} />} />);
    expect(await screen.findByText(/Nessun contratto validato\. Carica il primo/)).toBeInTheDocument();
  });
});

const contract: ContractV4Body = {
  contractId: CONTRACT,
  supplier: "Salesforce",
  name: "Sales Cloud Enterprise — MSA + Order Form",
  type: "MSA",
  version: 1,
  validated: true,
  annualValue: 640000,
  currency: "CHF",
  dates: { noticeDeadline: "18 Oct 2026", noticeDays: 40, end: "15 Jan 2027", autoRenew: true, renewalMonths: 12, uplift: "7%", start: "01 Mar 2024" },
  decisionPoints: [
    {
      key: "price-increase",
      tag: "discuss",
      kind: { id: "To discuss" },
      title: "Price increase",
      says: "Up to 7% or CPI, whichever is higher, at each renewal.",
      why: { id: "Costs money or freedom at renewal if left as is." },
      proposal: "Cap at 3% or freeze for the renewed term.",
      source: { id: "Contract · {source}", vars: { source: "p.9 §6.2" } },
      policyNote: null,
      decision: null,
      decidedBy: null,
      decidedAt: null,
    },
  ],
  nextAction: { hasCase: false, caseId: null, outcome: null, text: { id: "Start the renewal and assign an owner" } },
  terms: [{ label: "Annual spend", value: "CHF 640,000" }],
  products: [{ name: "Sales Cloud Enterprise", unit: "user / month", quantity: 300, unitPrice: 156, annual: 561600 }],
  oblYou: [{ text: "Give notice", when: "by 18 Oct 2026", high: true }],
  oblSup: [{ text: "Notify any price increase", when: "≥60 days", high: false }],
  std: [{ type: "Confidentiality", normalized: "Mutual, 5 years.", src: "p.28 §18" }],
};

describe("Contratto", () => {
  it("shows decisive dates, the points to decide and the next action; records a choice", async () => {
    const user = userEvent.setup();
    const decided = {
      points: [{ ...contract.decisionPoints[0]!, decision: "negotiate" as const, decidedBy: "Luca Lamalfa", decidedAt: "2026-09-08T10:00:00Z" }],
    };
    const api = client({
      getContractV4: vi.fn().mockResolvedValue(ok(contract)),
      setDecisionPoint: vi.fn().mockResolvedValue(ok(decided)),
    });
    renderAt(`/contracts/${CONTRACT}`, <Route path="/contracts/:contractId" element={<Contract360Route apiClient={api} />} />);

    expect(await screen.findByRole("heading", { name: "Sales Cloud Enterprise — MSA + Order Form" })).toBeInTheDocument();
    expect(screen.getByText("MSA · versione 1 · validato · CHF 640k / anno")).toBeInTheDocument();
    expect(screen.getByText("ultimo giorno · tra 40 giorni")).toBeInTheDocument();
    expect(screen.getByText("poi rinnovo automatico 12 mesi")).toBeInTheDocument();
    expect(screen.getByText("Avvia il rinnovo e assegna un responsabile")).toBeInTheDocument();
    expect(screen.getByText("Costa denaro o libertà al rinnovo se resta così.")).toBeInTheDocument();
    expect(screen.queryByText("Termini chiave")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Lo negoziamo" }));
    expect(api.setDecisionPoint).toHaveBeenCalledWith(TENANT, CONTRACT, "price-increase", "negotiate");
    expect(await screen.findByText("Contratto · p.9 §6.2 · registrato: lo negoziamo · Luca")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Lo negoziamo" })).toHaveAttribute("aria-pressed", "true");

    await user.click(screen.getByRole("button", { name: /^Tutto il contratto/ }));
    expect(screen.getByText("Termini chiave")).toBeInTheDocument();
    expect(screen.getByText("CHF 561,600")).toBeInTheDocument();
    expect(screen.getByText("Salesforce deve")).toBeInTheDocument();
    expect(missing).toEqual([]);
  });

  it("counts every point it shows: none from the contract, four negotiation points from Raffa", async () => {
    const todo = (rank: number, status: "Open" | "Done" | "Superseded" = "Open") => ({
      contractId: CONTRACT, pointKey: `p${rank}`, topic: `Point ${rank}`, rank, current: "today", target: "target", rationale: "why",
      citationKeys: [], source: "ask", status, createdAt: "2026-09-08T10:00:00Z", updatedAt: "2026-09-08T10:00:00Z",
    });
    const api = client({
      getContractV4: vi.fn().mockResolvedValue(ok({ ...contract, supplier: "GitLab", decisionPoints: [] })),
      getRenewalNegotiationTodos: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, todos: [todo(1), todo(2), todo(3), todo(4, "Done"), todo(5, "Superseded")], error: null }),
    });
    renderAt(`/contracts/${CONTRACT}`, <Route path="/contracts/:contractId" element={<Contract360Route apiClient={api} />} />);

    expect(await screen.findByText("Point 1")).toBeInTheDocument();
    const section = document.querySelector(".c4-decisions") as HTMLElement;
    expect(within(section).getByText("· 4 punti · le condizioni standard restano sotto")).toBeInTheDocument();
    expect(within(section).getAllByRole("listitem")).toHaveLength(4);
    expect(screen.queryByText("Point 5")).toBeNull();
    expect(screen.queryByText(/0 punti/)).toBeNull();
    expect(missing).toEqual([]);
  });

  it("says there is nothing to decide when there is no point at all", async () => {
    const api = client({
      getContractV4: vi.fn().mockResolvedValue(ok({ ...contract, decisionPoints: [] })),
      getRenewalNegotiationTodos: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, todos: [], error: null }),
    });
    renderAt(`/contracts/${CONTRACT}`, <Route path="/contracts/:contractId" element={<Contract360Route apiClient={api} />} />);

    expect(await screen.findByText("· niente da decidere · le condizioni standard restano sotto")).toBeInTheDocument();
    expect(missing).toEqual([]);
  });

  it("starts the renewal case when there is none and opens it", async () => {
    const user = userEvent.setup();
    const api = client({
      getContractV4: vi.fn().mockResolvedValue(ok(contract)),
      openRenewalCase: vi.fn().mockResolvedValue(ok({ caseId: "case-9" })),
    });
    renderAt(
      `/contracts/${CONTRACT}`,
      <>
        <Route path="/contracts/:contractId" element={<Contract360Route apiClient={api} />} />
        <Route path="/renewals/:caseId" element={<div data-testid="case" />} />
      </>,
    );
    await user.click(await screen.findByRole("button", { name: "Avvia il rinnovo" }));
    expect(api.openRenewalCase).toHaveBeenCalledWith(TENANT, { contractId: CONTRACT, branch: null });
    expect(await screen.findByTestId("case")).toBeInTheDocument();
  });

  it("opens the existing case without creating another", async () => {
    const user = userEvent.setup();
    const withCase = { ...contract, nextAction: { hasCase: true, caseId: "case-1", outcome: null, text: { id: "Open the case⟦contract⟧" } } };
    const api = client({ getContractV4: vi.fn().mockResolvedValue(ok(withCase)) });
    renderAt(
      `/contracts/${CONTRACT}`,
      <>
        <Route path="/contracts/:contractId" element={<Contract360Route apiClient={api} />} />
        <Route path="/renewals/:caseId" element={<div data-testid="case" />} />
      </>,
    );
    await user.click((await screen.findAllByRole("button", { name: "Apri la pratica" }))[0]!);
    expect(await screen.findByTestId("case")).toBeInTheDocument();
    expect(api.openRenewalCase).not.toHaveBeenCalled();
  });
});

const reading = (over: Partial<DocumentReadingBody>): DocumentReadingBody => ({
  documentId: DOC,
  contractId: CONTRACT,
  file: "Salesforce_MSA_2024.pdf",
  supplier: "Salesforce",
  status: "review",
  stage: 5,
  stageLabel: { id: "Extracting the facts" },
  failure: null,
  fields: [
    {
      key: "sf_renewterm",
      label: "Renewal term",
      value: "12 months (inferred)",
      page: 14,
      src: "p.14 §9.1",
      why: "The term is not stated as a number.",
      flagReason: { id: "Critical field" },
      quote: { before: "Unless terminated, this Order Form shall ", mark: "renew for successive periods", after: " — defined in Schedule 1." },
      critical: true,
      decision: null,
      correctedValue: null,
    },
    {
      key: "sf_uplift",
      label: "Price uplift at renewal",
      value: "7% or CPI, whichever is higher",
      page: 9,
      src: "p.9 §6.2",
      why: "Two readings conflict.",
      flagReason: { id: "Critical field" },
      quote: null,
      critical: true,
      decision: null,
      correctedValue: null,
    },
  ],
  ...over,
});

describe("Revisione", () => {
  it("shows the reading stage while the document is read", async () => {
    const api = client({
      getDocumentReading: vi.fn().mockResolvedValue(ok(reading({ status: "processing", stage: 1, stageLabel: { id: "Uploading" }, fields: [] }))),
    });
    renderAt(`/contracts/review/${DOC}`, <Route path="/contracts/review/:documentId" element={<ReviewRoute apiClient={api} />} />);
    expect(await screen.findByRole("heading", { name: "Raffa sta leggendo il contratto" })).toBeInTheDocument();
    expect(screen.getByText("Caricamento…")).toBeInTheDocument();
    expect(screen.getByText("Documento · Salesforce_MSA_2024.pdf")).toBeInTheDocument();
  });

  it("asks one field at a time, then validates", async () => {
    const user = userEvent.setup();
    const first = reading({});
    const second = reading({ fields: first.fields.map((f, i) => (i === 0 ? { ...f, decision: "accept" as const } : f)) });
    const done = reading({ status: "done", fields: second.fields.map((f) => ({ ...f, decision: f.decision ?? ("correct" as const), correctedValue: "7%" })) });
    const decide = vi.fn().mockResolvedValueOnce(ok(second)).mockResolvedValueOnce(ok(done));
    const api = client({ getDocumentReading: vi.fn().mockResolvedValue(ok(first)), decideReadingField: decide });
    renderAt(`/contracts/review/${DOC}`, <Route path="/contracts/review/:documentId" element={<ReviewRoute apiClient={api} />} />);

    expect(await screen.findByRole("heading", { name: "Due campi da verificare — uno alla volta" })).toBeInTheDocument();
    expect(screen.getByText("Campo 1 di 2 · da verificare")).toBeInTheDocument();
    expect(screen.getByText("renew for successive periods").tagName).toBe("MARK");
    await user.click(screen.getByRole("button", { name: "Accetta: è corretto" }));
    expect(decide).toHaveBeenCalledWith(TENANT, DOC, "sf_renewterm", { decision: "accept", value: null });

    expect(await screen.findByText("Campo 2 di 2 · da verificare")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Correggi" }));
    const input = screen.getByRole("textbox", { name: "Valore corretto" });
    await user.clear(input);
    await user.type(input, "7%");
    await user.click(screen.getByRole("button", { name: "Salva il valore corretto" }));
    expect(decide).toHaveBeenLastCalledWith(TENANT, DOC, "sf_uplift", { decision: "correct", value: "7%" });

    expect(await screen.findByText("Contratto validato.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Salesforce validato" })).toBeInTheDocument();
    expect(missing).toEqual([]);
  });

  it("offers another upload when the document cannot be read", async () => {
    const api = client({
      getDocumentReading: vi.fn().mockResolvedValue(
        ok(reading({ status: "failed", fields: [], failure: { id: "This file has no readable text (a scan without text?). Upload another version." } })),
      ),
    });
    renderAt(`/contracts/review/${DOC}`, <Route path="/contracts/review/:documentId" element={<ReviewRoute apiClient={api} />} />);
    expect(await screen.findByText(/Questo file non ha testo leggibile/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Carica un’altra versione" })).toBeInTheDocument();
  });

  it("outside fixture mode, /contracts/review/new leads to the Documents upload", async () => {
    renderAt("/contracts/review/new", <Route path="/contracts/review/:documentId" element={<ReviewRoute apiClient={client({})} />} />);
    await waitFor(() => expect(screen.getByTestId("elsewhere")).toBeInTheDocument());
  });
});
