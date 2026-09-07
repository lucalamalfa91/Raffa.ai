import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import PortfolioRoute from "../../../src/routes/contracts";
import type { ApiClient, GetPortfolioResult, PortfolioListItem, PortfolioPageBody } from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";

function mockApiClient(getPortfolio: ApiClient["getPortfolio"] = vi.fn()): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    getPortfolio,
    // Task E07/F02/US01/T01 (contract-360): this suite only exercises /contracts (PortfolioRoute
    // itself), never /contracts/:contractId (Contract360Route) -- bare vi.fn() is enough, the same
    // convention this file's own getPortfolio parameter replaced when it was still bare.
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
  };
}

function item(overrides: Partial<PortfolioListItem> = {}): PortfolioListItem {
  return {
    contractId: "contract-1",
    supplierId: null,
    type: "Msa",
    annualSpend: 100_000,
    startDate: "2025-01-01",
    endDate: "2026-01-01",
    renewalDate: null,
    cancellationDeadline: null,
    autoRenewal: false,
    status: "active",
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

function renderPortfolio(apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={["/contracts"]}>
      <Routes>
        <Route path="/contracts" element={<PortfolioRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
        <Route path="/documents" element={<div>DOCUMENTS_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("PortfolioRoute", () => {
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

    renderPortfolio(mockApiClient(getPortfolio));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(getPortfolio).not.toHaveBeenCalled();
  });

  it("AC-4 loading state: shows a skeleton while the request is in flight, then replaces it", async () => {
    let resolveFetch!: (value: GetPortfolioResult) => void;
    const pending = new Promise<GetPortfolioResult>((resolve) => {
      resolveFetch = resolve;
    });
    const { container } = renderPortfolio(mockApiClient(vi.fn().mockReturnValue(pending)));

    expect(container.querySelector(".portfolio-skeleton")).toBeInTheDocument();

    await act(async () => {
      resolveFetch(ok([item()]));
    });

    expect(container.querySelector(".portfolio-skeleton")).not.toBeInTheDocument();
    expect(await screen.findByRole("table")).toBeInTheDocument();
  });

  it("calls getPortfolio with the current workspace id and the backend's own page-size ceiling", () => {
    const getPortfolio = vi.fn().mockResolvedValue(ok([]));
    renderPortfolio(mockApiClient(getPortfolio));

    expect(getPortfolio).toHaveBeenCalledWith(WORKSPACE_ID, { pageSize: 100 });
  });

  it("AC-4 empty state: an empty portfolio shows the first-upload CTA linking to /documents", async () => {
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok([]))));

    expect(await screen.findByText("No contracts yet")).toBeInTheDocument();
    const link = screen.getByRole("link", { name: /upload a contract/i });
    expect(link).toHaveAttribute("href", "/documents");
  });

  it("AC-4 error state: a 503 renders a plain-language message with a Retry that re-fetches", async () => {
    const getPortfolio = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, statusCode: 503, portfolio: null, error: "Service Unavailable" })
      .mockResolvedValueOnce(ok([item()]));
    renderPortfolio(mockApiClient(getPortfolio));

    expect(await screen.findByText(/temporarily unavailable/i)).toBeInTheDocument();
    expect(screen.getByRole("alert")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /retry/i }));

    expect(await screen.findByRole("table")).toBeInTheDocument();
    expect(getPortfolio).toHaveBeenCalledTimes(2);
  });

  it("AC-3: renders a populated table sorted by severity then deadline, with a Contract 360 link", async () => {
    const failed = item({ contractId: "failed-1", status: "Failed" });
    const normal = item({ contractId: "normal-1" });
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok([normal, failed]))));

    const table = await screen.findByRole("table");
    const rows = within(table).getAllByRole("row").slice(1); // drop the header row
    // The failed (severity 3) row sorts before the untouched (severity 0) row, even though it was
    // second in the API response.
    expect(within(rows[0]).getByText(/processing failed/i)).toBeInTheDocument();
    expect(rows[0]).toHaveClass("row-critical");
    expect(rows[1]).not.toHaveClass("row-critical");

    fireEvent.click(within(rows[1]).getByRole("link", { name: "MSA" }));
    expect(await screen.findByText("CONTRACT_360_SCREEN")).toBeInTheDocument();
  });

  it("AC-2: clicking an attention-strip cell filters the table to that bucket, and clicking again clears it", async () => {
    const highRisk = item({ contractId: "risk-1", risk: "High" });
    const plain = item({ contractId: "plain-1" });
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok([highRisk, plain]))));

    await screen.findByRole("table");
    expect(screen.getAllByRole("row")).toHaveLength(3); // header + 2 rows

    const riskCell = screen.getByRole("button", { name: /High risk/ });
    fireEvent.click(riskCell);

    expect(screen.getAllByRole("row")).toHaveLength(2); // header + 1 matching row
    expect(riskCell).toHaveAttribute("aria-pressed", "true");

    fireEvent.click(riskCell);
    expect(screen.getAllByRole("row")).toHaveLength(3); // cleared back to both rows
  });

  it("AC-1: a filter chip narrows the table the same way the attention strip does", async () => {
    const active = item({ contractId: "active-1", status: "active" });
    const expired = item({ contractId: "expired-1", status: "expired" });
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok([active, expired]))));

    await screen.findByRole("table");
    fireEvent.change(screen.getByLabelText("Status"), { target: { value: "active" } });

    expect(screen.getAllByRole("row")).toHaveLength(2); // header + the one "active" row
  });

  it("AC-4 no-match-for-filter state: a filter matching nothing shows the named empty state with its own Clear filters CTA", async () => {
    renderPortfolio(mockApiClient(vi.fn().mockResolvedValue(ok([item({ status: "active" })]))));

    await screen.findByRole("table");
    fireEvent.change(screen.getByLabelText("Status"), { target: { value: "no-such-status" } });

    const heading = await screen.findByText("No contracts match these filters");
    const emptyState = heading.closest(".empty-state");
    expect(emptyState).not.toBeNull();

    // Scoped to this specific empty-state block: PortfolioFilters renders its own, differently-scoped
    // "Clear filters" button at all times (disabled when nothing is active), so an unscoped query here
    // would be ambiguous once both are on screen.
    fireEvent.click(within(emptyState as HTMLElement).getByRole("button", { name: /clear filters/i }));

    expect(await screen.findByRole("table")).toBeInTheDocument();
  });
});
