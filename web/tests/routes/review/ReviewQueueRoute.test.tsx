import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import ReviewQueueRoute from "../../../src/routes/review";
import { rememberDocument } from "../../../src/routes/documents/documentStore";
import type { ApiClient, GetPortfolioResult, PortfolioListItem, PortfolioPageBody } from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
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

function item(overrides: Partial<PortfolioListItem> = {}): PortfolioListItem {
  return {
    contractId: "22222222-2222-2222-2222-222222222222",
    supplierId: null,
    type: "Msa",
    annualSpend: 100_000,
    startDate: "2025-01-01",
    endDate: "2026-01-01",
    renewalDate: null,
    cancellationDeadline: null,
    autoRenewal: false,
    status: "needs_review",
    risk: null,
    ...overrides,
  };
}

function page(items: PortfolioListItem[]): PortfolioPageBody {
  return { items, page: 1, pageSize: 100, totalCount: items.length };
}

function ok(items: PortfolioListItem[]): GetPortfolioResult {
  return { ok: true, statusCode: 200, portfolio: page(items), error: null };
}

function renderQueue(apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={["/review"]}>
      <Routes>
        <Route path="/review" element={<ReviewQueueRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId/review" element={<div>FIELD_REVIEW_SCREEN</div>} />
        <Route path="/documents" element={<div>DOCUMENTS_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("ReviewQueueRoute", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }),
    );
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    const getPortfolio = vi.fn();
    renderQueue(mockApiClient({ getPortfolio }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(getPortfolio).not.toHaveBeenCalled();
  });

  it("shows the empty state when nothing needs review", async () => {
    renderQueue(mockApiClient({ getPortfolio: vi.fn().mockResolvedValue(ok([])) }));

    expect(await screen.findByText(/nothing needs review/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /upload a document/i })).toHaveAttribute("href", "/documents");
    expect(screen.queryByText(/ships in epic-07\/feature-03-review-correction-ui/i)).not.toBeInTheDocument();
  });

  it("lists needs-review contracts and links each row to the field-review screen", async () => {
    renderQueue(mockApiClient({ getPortfolio: vi.fn().mockResolvedValue(ok([item()])) }));

    expect(await screen.findByRole("link", { name: "MSA" })).toHaveAttribute(
      "href",
      "/contracts/22222222-2222-2222-2222-222222222222/review",
    );
    expect(screen.getAllByText("Needs review").length).toBeGreaterThan(0);
  });

  it("includes this-session NeedsReview uploads that are not yet on the portfolio page", async () => {
    rememberDocument({
      id: "doc-1",
      contractId: "33333333-3333-3333-3333-333333333333",
      fileName: "Acme_OrderForm.pdf",
      documentType: "OrderForm",
      processingStatus: "NeedsReview",
      createdAt: "2026-09-06T08:00:00Z",
    });

    renderQueue(mockApiClient({ getPortfolio: vi.fn().mockResolvedValue(ok([])) }));

    expect(await screen.findByRole("link", { name: "Acme_OrderForm.pdf" })).toHaveAttribute(
      "href",
      "/contracts/33333333-3333-3333-3333-333333333333/review",
    );
  });

  it("shows a named error + Retry when getPortfolio fails", async () => {
    const getPortfolio = vi.fn().mockResolvedValue({
      ok: false,
      statusCode: 503,
      portfolio: null,
      error: "down",
    });
    renderQueue(mockApiClient({ getPortfolio }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/review queue unavailable/i);
    expect(screen.getByRole("button", { name: /retry/i })).toBeInTheDocument();

    getPortfolio.mockResolvedValueOnce(ok([]));
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /retry/i }));
    });

    expect(await screen.findByText(/nothing needs review/i)).toBeInTheDocument();
  });
});
