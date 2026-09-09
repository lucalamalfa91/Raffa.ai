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
    // Task E13/F09/US01/T03 (web-documents-v2): this suite does not exercise Documents -- bare
    // vi.fn() is enough, same convention as getPortfolio below.
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, priority: null, error: "No contract found." }),
    // Task E13/F09/US01/T04 (web-ask-v2): this suite never reaches conversations/capabilities/
    // market -- bare vi.fn() is enough, same convention as getCorrectionHistory above.
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    postRenewalAction: vi.fn(),
    // Task E08/F03/US01/T01 (quote-check-ui) / E07/F04/US01/T01 (ask-contigo-ui): this suite never
    // reaches the Quote Check or Ask Contigo screens -- bare vi.fn() is enough, same convention as
    // getCorrectionHistory above. (Pre-existing gap in this file's own mock literal, backfilled here
    // while task E08/F02/US01/T01 was already touching this exact object for its own two additions
    // below.)
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askContigo: vi.fn(),
    // Task E08/F02/US01/T01 (savings-home): this suite never reaches Home's own fetch-outcome
    // matrix -- bare vi.fn() is enough, same convention as getCorrectionHistory above.
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    ...overrides,
  };
}

function pipelineItem(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalPipelineItemBody {
  return {
    contractId: "22222222-2222-2222-2222-222222222222",
    supplierId: "33333333-3333-3333-3333-333333333333",
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

function renderRenewals(apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={["/renewals"]}>
      <Routes>
        <Route path="/renewals" element={<RenewalsRoute apiClient={apiClient} userLabel={USER_LABEL} />} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
        <Route path="/contracts" element={<div>PORTFOLIO_SCREEN</div>} />
        <Route path="/" element={<div>HOME_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("RenewalsRoute", () => {
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

  it("AC-4 loading state: shows a skeleton while the request is in flight, then replaces it", async () => {
    let resolveFetch!: (value: GetRenewalsResult) => void;
    const pending = new Promise<GetRenewalsResult>((resolve) => {
      resolveFetch = resolve;
    });
    const { container } = renderRenewals(mockApiClient({ getRenewals: vi.fn().mockReturnValue(pending) }));

    expect(container.querySelector(".renewal-skeleton")).toBeInTheDocument();

    await act(async () => {
      resolveFetch(ok([]));
    });

    expect(container.querySelector(".renewal-skeleton")).not.toBeInTheDocument();
  });

  it("AC-4 error state ('engine unavailable'): a 503 renders a plain-language message with a Retry that re-fetches", async () => {
    const getRenewals = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: 503, renewals: null, error: "Service Unavailable" })
      .mockResolvedValueOnce(ok([]));
    renderRenewals(mockApiClient({ getRenewals }));

    expect(await screen.findByText(/renewal engine.*temporarily unavailable/i)).toBeInTheDocument();
    expect(screen.getByRole("alert")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /retry/i }));

    expect(await screen.findByText(/no renewals in your pipeline yet/i)).toBeInTheDocument();
    expect(getRenewals).toHaveBeenCalledTimes(2);
  });

  it("AC-4 empty state: zero renewals shows a named empty state linking to the portfolio", async () => {
    renderRenewals(mockApiClient({ getRenewals: vi.fn().mockResolvedValue(ok([])) }));

    expect(await screen.findByText("No renewals in your pipeline yet")).toBeInTheDocument();
    const link = screen.getByRole("link", { name: /view portfolio/i });
    expect(link).toHaveAttribute("href", "/contracts");
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

    it("AC-2: renders the table with the seven named columns, the resolved score, and the default 'Open' status", async () => {
      renderPopulated([pipelineItem()]);

      const table = await screen.findByRole("table");
      const headerCells = within(table).getAllByRole("columnheader").map((cell) => cell.textContent);
      expect(headerCells).toEqual(["Score", "Supplier", "Contract", "Annual spend", "Renews in", "Cancel by", "Status"]);
      expect(within(table).getByText("85")).toBeInTheDocument();
      expect(within(table).getByText("Open")).toBeInTheDocument();
    });

    it("AC-3: the insight card shows the recommendation and the three fixed actions", async () => {
      renderPopulated([pipelineItem()]);
      await screen.findByRole("table");

      expect(screen.getByText("Finalize decision now")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Start negotiation" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Assign to me" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Snooze to 90-day threshold" })).toBeInTheDocument();
    });

    it("AC-3: selecting a different row swaps the insight card to that row's own recommendation", async () => {
      const first = pipelineItem({ contractId: "first", supplierId: "sup-1", action: "Finalize decision now" });
      const second = pipelineItem({
        contractId: "second",
        supplierId: "sup-2",
        action: "Prepare negotiation strategy",
        insightCard: {
          facts: first.insightCard.facts,
          recommendations: { ...first.insightCard.recommendations, recommendedAction: "Prepare negotiation strategy" },
        },
      });
      renderPopulated([first, second]);
      await screen.findByRole("table");

      expect(screen.getByText("Finalize decision now")).toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: /show insight card for.*second/i }));

      expect(await screen.findByText("Prepare negotiation strategy")).toBeInTheDocument();
      expect(screen.queryByText("Finalize decision now")).not.toBeInTheDocument();
    });

    it("AC-1: a threshold-strip bucket filters the table, and clicking it again clears the filter", async () => {
      const near = pipelineItem({ contractId: "near", daysUntilRenewal: 10 });
      const far = pipelineItem({ contractId: "far", daysUntilRenewal: 200 });
      renderPopulated([near, far]);

      await screen.findByRole("table");
      expect(screen.getAllByRole("row")).toHaveLength(3); // header + 2 rows

      // The button's accessible name is its own count *and* label text nodes concatenated (e.g.
      // "10-30 d" for a count of 1) -- match the label as a substring, the same convention
      // tests/routes/contracts/PortfolioRoute.test.tsx already uses for its own AttentionStrip cells.
      const bucket = screen.getByRole("button", { name: /0–30 d/ });
      fireEvent.click(bucket);

      expect(screen.getAllByRole("row")).toHaveLength(2); // header + 1 matching row
      expect(bucket).toHaveAttribute("aria-pressed", "true");

      fireEvent.click(bucket);
      expect(screen.getAllByRole("row")).toHaveLength(3);
    });

    it("AC-4 no-window state: a bucket matching nothing shows its own named empty state with a clear CTA", async () => {
      renderPopulated([pipelineItem({ daysUntilRenewal: 200 })]);

      await screen.findByRole("table");
      fireEvent.click(screen.getByRole("button", { name: /0–30 d/ }));

      expect(await screen.findByText("No renewals in this window")).toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: /show all renewals/i }));

      expect(await screen.findByRole("table")).toBeInTheDocument();
    });

    it("AC-3 + task-01's own required test ('action creates opportunity link to Home'): an action posts the real write, records an opportunity, and confirms with links to Contract 360 and Home", async () => {
      const item = pipelineItem({ contractId: "contract-x" });
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

      // The real, durable write -- owner is this screen's own signed-in userLabel (no assignee picker in V1).
      expect(postRenewalAction).toHaveBeenCalledWith(WORKSPACE_ID, "contract-x", {
        owner: USER_LABEL,
        status: "InProgress",
        action: "In negotiation",
      });

      const confirmationHeading = await screen.findByText(/tracked as an opportunity/i);
      const confirmation = confirmationHeading.closest(".renewal-confirmation") as HTMLElement;
      expect(confirmation).not.toBeNull();
      expect(confirmation.textContent).toContain("In negotiation");
      expect(confirmation.textContent).toContain(`owner ${USER_LABEL}`);
      expect(within(confirmation).getByRole("link", { name: /open contract 360/i })).toHaveAttribute(
        "href",
        "/contracts/contract-x",
      );
      expect(within(confirmation).getByRole("link", { name: /open home/i })).toHaveAttribute("href", "/");

      // The table's own Status column reflects the same acted state this session.
      expect(within(screen.getByRole("table")).getByText("In negotiation")).toBeInTheDocument();

      // The council decision this task carries ("Action creates an opportunity visible on Home"):
      // recorded in the same session-scoped store a future Home screen reads
      // (renewalActionStore.ts's own header comment names task E08/F02/US01/T01 as that consumer).
      const trackedOpportunities = loadTrackedRenewalActions();
      expect(trackedOpportunities).toHaveLength(1);
      expect(trackedOpportunities[0]).toMatchObject({
        contractId: "contract-x",
        owner: USER_LABEL,
        action: "In negotiation",
        status: "InProgress",
      });
    });

    it("shows an inline error and records no opportunity when the write fails", async () => {
      const postRenewalAction = vi
        .fn()
        .mockResolvedValue({ ok: false, statusCode: 400, action: null, error: "'owner' is required." });
      renderPopulated([pipelineItem()], { postRenewalAction });
      await screen.findByRole("table");

      fireEvent.click(screen.getByRole("button", { name: "Assign to me" }));

      expect(await screen.findByText("'owner' is required.")).toBeInTheDocument();
      expect(loadTrackedRenewalActions()).toEqual([]);
    });
  });
});
