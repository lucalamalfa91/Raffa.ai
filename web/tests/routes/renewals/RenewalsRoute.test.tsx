import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import RenewalsRoute from "../../../src/routes/renewals";
import { loadTrackedRenewalActions } from "../../../src/routes/renewals/renewalActionStore";
import type {
  ApiClient,
  GetRenewalsResult,
  PostRenewalActionResult,
  RenewalPipelineItemBody,
  RenewalPriorityBody,
} from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const USER_LABEL = "user@example.test";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, priority: null, error: "No contract found." }),
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn(),
    validateDocument: vi.fn(),
    postRenewalAction: vi.fn(),
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askContigo: vi.fn(),
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    ...overrides,
  };
}

function pipelineItem(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalPipelineItemBody {
  return {
    contractId: "22222222-2222-2222-2222-222222222222",
    supplierId: "33333333-3333-3333-3333-333333333333",
    supplierName: null,
    status: "Determined",
    renewalDate: "2026-12-01",
    daysUntilRenewal: 20,
    annualSpend: 500_000,
    cancellationDeadline: "2026-10-01",
    daysUntilCancellationDeadline: 14,
    autoRenewal: true,
    action: "Finalize decision now",
    insightCard: {
      facts: {
        supplierId: "33333333-3333-3333-3333-333333333333",
        supplierName: null,
        renewalDate: "2026-12-01",
        daysUntilRenewal: 20,
        annualSpend: 500_000,
        cancellationDeadline: "2026-10-01",
        daysUntilCancellationDeadline: 14,
      },
      recommendations: {
        recommendedAction: "Finalize decision now",
        explanation: "The cancellation deadline is 14 day(s) away.",
        annualUpliftPercent: null,
        marketPosition: null,
        potentialSavingsRange: null,
      },
    },
    ...overrides,
  };
}

function priority(overrides: Partial<RenewalPriorityBody> = {}): RenewalPriorityBody {
  return {
    contractId: "22222222-2222-2222-2222-222222222222",
    totalScore: 85,
    components: {
      spendWeight: { score: 20, explanation: "x" },
      timeUrgency: { score: 20, explanation: "x" },
      benchmarkOpportunity: { score: 10, explanation: "x" },
      priceIncreaseRisk: { score: 20, explanation: "x" },
      contractRisk: { score: 15, explanation: "x" },
    },
    ...overrides,
  };
}

function ok(items: RenewalPipelineItemBody[]): GetRenewalsResult {
  return { ok: true, statusCode: 200, renewals: { items, totalCount: items.length }, error: null };
}

/** One `getRenewalPriority` mock answering per contract id; ids missing from `scores` fail like the real endpoint's 404. */
function priorityByContract(scores: Readonly<Record<string, number>>) {
  return vi.fn().mockImplementation((_workspaceId: string, contractId: string) =>
    Promise.resolve(
      contractId in scores
        ? { ok: true, statusCode: 200, priority: priority({ contractId, totalScore: scores[contractId] }), error: null }
        : { ok: false, statusCode: 404, priority: null, error: "No contract found." },
    ),
  );
}

function renderRenewals(apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={["/renewals"]}>
      <Routes>
        <Route path="/renewals" element={<RenewalsRoute apiClient={apiClient} userLabel={USER_LABEL} />} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
        <Route path="/documents" element={<div>DOCUMENTS_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("RenewalsRoute (V2, ADR-024 / screens-v2.md #7)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }),
    );
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    const getRenewals = vi.fn();

    renderRenewals(mockApiClient({ getRenewals }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(getRenewals).not.toHaveBeenCalled();
  });

  it("shows a skeleton while the request is in flight, then replaces it", async () => {
    let resolveFetch!: (value: GetRenewalsResult) => void;
    const pending = new Promise<GetRenewalsResult>((resolve) => {
      resolveFetch = resolve;
    });
    const { container } = renderRenewals(mockApiClient({ getRenewals: vi.fn().mockReturnValue(pending) }));

    expect(container.querySelector(".renewal-skeleton")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Renewals" })).toBeInTheDocument();

    await act(async () => {
      resolveFetch(ok([]));
    });

    expect(container.querySelector(".renewal-skeleton")).not.toBeInTheDocument();
  });

  it("a 503 renders a plain-language error with a Retry that re-fetches", async () => {
    const getRenewals = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: 503, renewals: null, error: "Service Unavailable" })
      .mockResolvedValueOnce(ok([]));
    renderRenewals(mockApiClient({ getRenewals }));

    expect(await screen.findByText(/renewal engine.*temporarily unavailable/i)).toBeInTheDocument();
    expect(screen.getByRole("alert")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /retry/i }));

    expect(await screen.findByText("No renewal dates yet")).toBeInTheDocument();
    expect(getRenewals).toHaveBeenCalledTimes(2);
  });

  it("R-WEB-02 reroute: with nothing validated the screen explains itself and sends the reader to upload", async () => {
    renderRenewals(mockApiClient({ getRenewals: vi.fn().mockResolvedValue(ok([])) }));

    expect(await screen.findByText("No renewal dates yet")).toBeInTheDocument();
    expect(
      screen.getByText("Renewals are computed from validated end dates and notice periods. Upload a contract to start."),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Upload a contract" })).toHaveAttribute("href", "/documents");
    // The header summary is the prototype's own `rnSummary` for the off tier.
    expect(screen.getByText("Computed from validated end dates and notice periods")).toBeInTheDocument();
    // No V1 leftovers: no threshold strip, no "View portfolio".
    expect(screen.queryByRole("button", { name: /0–30 d/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /view portfolio/i })).not.toBeInTheDocument();
  });

  describe("once populated", () => {
    function renderPopulated(items: RenewalPipelineItemBody[], overrides: Partial<ApiClient> = {}) {
      return renderRenewals(
        mockApiClient({
          getRenewals: vi.fn().mockResolvedValue(ok(items)),
          getRenewalPriority: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, priority: priority(), error: null }),
          ...overrides,
        }),
      );
    }

    it("renders the V2 five-column list, the resolved score, the default 'Open' status and the lit summary", async () => {
      renderPopulated([pipelineItem({ supplierName: "Salesforce" })]);

      const table = await screen.findByRole("table");
      const headerCells = within(table).getAllByRole("columnheader").map((cell) => cell.textContent);
      expect(headerCells).toEqual(["Score", "Supplier · contract", "Renews in", "Notice in", "Status"]);
      expect(within(table).getByText("85")).toBeInTheDocument();
      expect(within(table).getByText("Salesforce")).toBeInTheDocument();
      expect(within(table).getByText("· Contract 22222222")).toBeInTheDocument();
      expect(within(table).getByText("20 d")).toBeInTheDocument();
      expect(within(table).getByText("14 d")).toBeInTheDocument();
      expect(within(table).getByText("Open")).toBeInTheDocument();
      expect(screen.getByText("1 contract with validated dates · sorted by priority")).toBeInTheDocument();
    });

    it("sorts by priority score, highest first; a row whose score could not be fetched shows '—' and sorts last", async () => {
      const low = pipelineItem({ contractId: "low", supplierName: "Low Co" });
      const high = pipelineItem({ contractId: "high", supplierName: "High Co" });
      const unranked = pipelineItem({ contractId: "unranked", supplierName: "Unranked Co" });
      renderPopulated([unranked, low, high], { getRenewalPriority: priorityByContract({ low: 40, high: 91 }) });

      const table = await screen.findByRole("table");
      const bodyRows = within(table).getAllByRole("row").slice(1);
      expect(bodyRows.map((row) => within(row).getByRole("button").textContent)).toEqual(["91", "40", "—"]);
      expect(bodyRows.map((row) => row.textContent)).toEqual([
        expect.stringContaining("High Co"),
        expect.stringContaining("Low Co"),
        expect.stringContaining("Unranked Co"),
      ]);
      // Emphasis follows the prototype's own `score >= 80` rule, on top of the number itself.
      expect(within(bodyRows[0]).getByRole("button")).toHaveClass("is-urgent");
      expect(within(bodyRows[1]).getByRole("button")).not.toHaveClass("is-urgent");
      expect(screen.getByText("3 contracts with validated dates · sorted by priority")).toBeInTheDocument();
    });

    it("marks a notice deadline inside the locked 45-day window, never colour alone", async () => {
      const soon = pipelineItem({ contractId: "soon", daysUntilCancellationDeadline: 14 });
      const later = pipelineItem({ contractId: "later", daysUntilCancellationDeadline: 200, daysUntilRenewal: 260 });
      renderPopulated([soon, later], { getRenewalPriority: priorityByContract({ soon: 85, later: 30 }) });

      const table = await screen.findByRole("table");
      expect(within(table).getByText("14 d")).toHaveClass("deadline-critical");
      expect(within(table).getByText("200 d")).not.toHaveClass("deadline-critical");
    });

    it("the 'Why it is here' pane follows the top-priority row by default: heading, recommendation, rationale, two actions, facts link", async () => {
      renderPopulated([pipelineItem({ supplierName: "Salesforce" })]);
      await screen.findByRole("table");

      const pane = screen.getByRole("complementary", { name: "Why it is here" });
      expect(within(pane).getByRole("heading", { level: 3, name: "Salesforce — 14 days to notice" })).toBeInTheDocument();
      expect(within(pane).getByText("Recommended action")).toBeInTheDocument();
      expect(within(pane).getByText("Finalize decision now")).toBeInTheDocument();
      expect(within(pane).getByText("The cancellation deadline is 14 day(s) away.")).toBeInTheDocument();
      expect(within(pane).getByRole("button", { name: "Start negotiation" })).toHaveClass("btn-primary");
      expect(within(pane).getByRole("button", { name: "Assign to me" })).toHaveClass("btn-secondary");
      // The Day-1 third action is not part of V2.
      expect(screen.queryByRole("button", { name: /snooze/i })).not.toBeInTheDocument();
      expect(within(pane).getByRole("link", { name: "See the facts behind this →" })).toHaveAttribute(
        "href",
        "/contracts/22222222-2222-2222-2222-222222222222",
      );
    });

    it("without a determined notice date the pane heading says so instead of counting to nothing", async () => {
      renderPopulated([
        pipelineItem({ supplierName: "Fabrikam", daysUntilCancellationDeadline: null, cancellationDeadline: null }),
      ]);
      await screen.findByRole("table");

      expect(screen.getByRole("heading", { level: 3, name: "Fabrikam — notice date not determined" })).toBeInTheDocument();
      expect(within(screen.getByRole("table")).getAllByText("—").length).toBeGreaterThanOrEqual(1);
    });

    it("selecting a different row (score button or the row itself) swaps the pane to that row's own recommendation", async () => {
      const first = pipelineItem({ contractId: "first", supplierName: "First Co", action: "Finalize decision now" });
      const second = pipelineItem({
        contractId: "second",
        supplierName: "Second Co",
        action: "Prepare negotiation strategy",
        insightCard: {
          facts: first.insightCard.facts,
          recommendations: { ...first.insightCard.recommendations, recommendedAction: "Prepare negotiation strategy" },
        },
      });
      renderPopulated([first, second], { getRenewalPriority: priorityByContract({ first: 90, second: 60 }) });
      const table = await screen.findByRole("table");

      expect(screen.getByText("Finalize decision now")).toBeInTheDocument();
      const firstRow = within(table).getAllByRole("row")[1];
      expect(firstRow).toHaveClass("row-selected");

      const secondButton = screen.getByRole("button", { name: "Show why Second Co · Contract second is here" });
      fireEvent.click(secondButton);

      expect(await screen.findByText("Prepare negotiation strategy")).toBeInTheDocument();
      expect(screen.queryByText("Finalize decision now")).not.toBeInTheDocument();
      expect(secondButton).toHaveAttribute("aria-pressed", "true");
      expect(within(table).getAllByRole("row")[2]).toHaveClass("row-selected");

      // Row click (anywhere but the button) is the prototype's `cg-row` convenience on top.
      fireEvent.click(within(table).getByText("First Co"));
      expect(await screen.findByText("Finalize decision now")).toBeInTheDocument();
      expect(firstRow).toHaveClass("row-selected");
    });

    it("an action posts the real write with the signed-in owner, then the pane and the Status column show the acted state", async () => {
      const item = pipelineItem({ contractId: "contract-x", supplierName: "Salesforce" });
      const postRenewalAction = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        action: {
          contractId: "contract-x",
          owner: USER_LABEL,
          status: "InProgress",
          action: "In negotiation",
          updatedAt: "2026-09-06T09:00:00Z",
        },
        error: null,
      } satisfies PostRenewalActionResult);

      renderPopulated([item], { postRenewalAction });
      await screen.findByRole("table");

      fireEvent.click(screen.getByRole("button", { name: "Start negotiation" }));

      expect(postRenewalAction).toHaveBeenCalledWith(WORKSPACE_ID, "contract-x", {
        owner: USER_LABEL,
        status: "InProgress",
        action: "In negotiation",
      });

      const acted = await screen.findByRole("status");
      expect(acted).toHaveClass("renewal-pane-acted");
      expect(acted.textContent).toContain("In negotiation");
      expect(acted.textContent).toContain(`owner ${USER_LABEL}`);
      expect(within(acted).getByRole("link", { name: "Open contract →" })).toHaveAttribute("href", "/contracts/contract-x");
      expect(screen.queryByRole("button", { name: "Start negotiation" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Assign to me" })).not.toBeInTheDocument();

      const table = screen.getByRole("table");
      expect(within(table).getByText("In negotiation")).toHaveClass("tag-accent");
      expect(within(table).queryByText("Open")).not.toBeInTheDocument();

      // Recorded in the session store Contract 360's tracker reads too.
      const tracked = loadTrackedRenewalActions();
      expect(tracked).toHaveLength(1);
      expect(tracked[0]).toMatchObject({ contractId: "contract-x", owner: USER_LABEL, action: "In negotiation", status: "InProgress" });
    });

    it("'Assign to me' claims ownership without starting work (NotStarted / Assigned)", async () => {
      const postRenewalAction = vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        action: { contractId: "contract-y", owner: USER_LABEL, status: "NotStarted", action: "Assigned", updatedAt: "2026-09-06T09:00:00Z" },
        error: null,
      } satisfies PostRenewalActionResult);
      renderPopulated([pipelineItem({ contractId: "contract-y" })], { postRenewalAction });
      await screen.findByRole("table");

      fireEvent.click(screen.getByRole("button", { name: "Assign to me" }));

      expect(postRenewalAction).toHaveBeenCalledWith(WORKSPACE_ID, "contract-y", {
        owner: USER_LABEL,
        status: "NotStarted",
        action: "Assigned",
      });
      expect(await screen.findByRole("status")).toHaveTextContent("Assigned");
    });

    it("shows an inline error and records nothing when the write fails", async () => {
      const postRenewalAction = vi
        .fn()
        .mockResolvedValue({ ok: false, statusCode: 400, action: null, error: "'owner' is required." });
      renderPopulated([pipelineItem()], { postRenewalAction });
      await screen.findByRole("table");

      fireEvent.click(screen.getByRole("button", { name: "Assign to me" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("'owner' is required.");
      expect(screen.getByRole("button", { name: "Start negotiation" })).toBeEnabled();
      expect(loadTrackedRenewalActions()).toEqual([]);
    });
  });
});
