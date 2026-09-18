import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ApiClient, RenewalPipelineItemBody } from "../../api/client";
import RenewalsRoute from "./index";

/**
 * Task E29/F03/US01/T01 (renewals-select; parent story us-01-renewals-select AC-1/AC-2; closes
 * NW-84, ADR-012 cl. 51 per `reports/architecture/waves/w19.md`). Proves the one thing a pure
 * view-model test cannot: that the route reads `useSearchParams().get("select")` on mount and wires
 * it into the same selection state a row click already drives.
 *
 * Two rows, deliberately scored so the top-priority default only emerges once both
 * `GET /api/renewals/{contractId}/priority` calls resolve (`daysUntilCancellationDeadline` alone
 * would rank them the other way round) -- an assertion that only holds once the real score has won
 * proves the fallback is score-based, not an accident of the pre-priority tie-break order.
 */

const TOP_CONTRACT_ID = "11111111-1111-1111-1111-111111111111";
const OTHER_CONTRACT_ID = "22222222-2222-2222-2222-222222222222";

function renewalItem(overrides: Partial<RenewalPipelineItemBody>): RenewalPipelineItemBody {
  return {
    contractId: overrides.contractId!,
    supplierId: null,
    supplierName: null,
    status: "Determined",
    renewalDate: "2027-01-01",
    daysUntilRenewal: 120,
    annualSpend: 50000,
    cancellationDeadline: "2026-11-01",
    daysUntilCancellationDeadline: 90,
    autoRenewal: true,
    action: "Renegotiate rate",
    savedAction: null,
    insightCard: {
      facts: {
        supplierId: null,
        supplierName: null,
        renewalDate: "2027-01-01",
        daysUntilRenewal: 120,
        annualSpend: 50000,
        cancellationDeadline: "2026-11-01",
        daysUntilCancellationDeadline: 90,
      },
      recommendations: {
        recommendedAction: "Renegotiate rate",
        explanation: "Spend is above the market band for this category.",
        annualUpliftPercent: null,
        marketPosition: null,
        potentialSavingsRange: null,
      },
    },
    ...overrides,
  } as RenewalPipelineItemBody;
}

const TOP_ROW = renewalItem({
  contractId: TOP_CONTRACT_ID,
  supplierName: "Top Priority Co",
  daysUntilCancellationDeadline: 90,
});

const OTHER_ROW = renewalItem({
  contractId: OTHER_CONTRACT_ID,
  supplierName: "Deep Link Co",
  daysUntilCancellationDeadline: 30,
});

/** Highest score wins the default row; note this ranks the *opposite* way from the two rows' own
 * `daysUntilCancellationDeadline` tie-break above, so a pass proves the score, not the tie-break, won. */
const SCORES: Record<string, number> = {
  [TOP_CONTRACT_ID]: 90,
  [OTHER_CONTRACT_ID]: 40,
};

function apiClientWith(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getRenewals: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      renewals: { items: [TOP_ROW, OTHER_ROW], totalCount: 2 },
      error: null,
    }),
    getRenewalPriority: vi.fn().mockImplementation((_tenantId: string, contractId: string) =>
      Promise.resolve({
        ok: true,
        statusCode: 200,
        priority: { totalScore: SCORES[contractId] ?? 0 },
        error: null,
      }),
    ),
    postRenewalAction: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web): InsightCard now always renders NegotiationTodoList, which
    // fetches on mount -- an unstubbed method here would reject `.then()` on `undefined` and crash
    // every test in this file, not just ones about the TODO list itself. None of the tests below
    // exercise the list's own content or the tick, so an honest empty list is enough.
    getRenewalNegotiationTodos: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, todos: [], error: null }),
    ...overrides,
  } as unknown as ApiClient;
}

beforeEach(() => {
  window.sessionStorage.clear();
  window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: "w-1", name: "Acme Co" }));
});

function renderAt(path: string, apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/renewals" element={<RenewalsRoute apiClient={apiClient} userLabel="Test User" />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("RenewalsRoute ?select= (NW-84)", () => {
  it("AC-1: ?select=<guid> selects that row and shows its insight pane, even though it is not the top-priority row", async () => {
    const apiClient = apiClientWith();
    renderAt(`/renewals?select=${OTHER_CONTRACT_ID}`, apiClient);

    // The insight pane follows the deep-linked row -- proves the query value reached selection state,
    // not just that the list rendered.
    expect(await screen.findByRole("heading", { level: 3, name: /Deep Link Co/i })).toBeInTheDocument();
    // And its row in the table carries the selected affordance (native <button>, ADR-019).
    expect(screen.getByRole("button", { name: /Show why Deep Link Co/i })).toHaveAttribute("aria-pressed", "true");
    // The other (higher-priority) row is listed but not selected.
    expect(screen.getByRole("button", { name: /Show why Top Priority Co/i })).toHaveAttribute("aria-pressed", "false");
  });

  it("AC-2: an id matching no listed row falls back to the top-priority row -- no crash, no error state", async () => {
    const apiClient = apiClientWith();
    renderAt("/renewals?select=not-a-real-contract-id", apiClient);

    // Falls back to the same top-priority default the no-query case uses (score 90 beats score 40).
    expect(await screen.findByRole("heading", { level: 3, name: /Top Priority Co/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Show why Top Priority Co/i })).toHaveAttribute("aria-pressed", "true");

    // Never a 500 / thrown error surfaced to the screen.
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByText(/unavailable/i)).not.toBeInTheDocument();
  });

  it("keeps the existing auto-select default when there is no ?select= at all", async () => {
    const apiClient = apiClientWith();
    renderAt("/renewals", apiClient);

    expect(await screen.findByRole("heading", { level: 3, name: /Top Priority Co/i })).toBeInTheDocument();
  });
});
