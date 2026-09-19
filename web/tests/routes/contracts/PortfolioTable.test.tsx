import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import type { PortfolioListItem } from "../../../src/api/client";
import PortfolioTable from "../../../src/routes/contracts/PortfolioTable";
import type { PortfolioRow } from "../../../src/routes/contracts/portfolioViewModel";

function item(overrides: Partial<PortfolioListItem> = {}): PortfolioListItem {
  return {
    contractId: "contract-1",
    supplierId: "33333333-3333-3333-3333-333333333333",
    supplierName: "Salesforce",
    type: "Msa",
    annualSpend: 640_000,
    currency: "CHF",
    startDate: "2024-03-01",
    endDate: "2027-01-15",
    renewalDate: "2027-01-15",
    cancellationDeadline: "2027-01-01",
    autoRenewal: true,
    status: "active",
    risk: "High",
    ...overrides,
  };
}

function row(overrides: Partial<PortfolioListItem> = {}): PortfolioRow {
  return {
    item: item(overrides),
    cancelDays: 200,
    isUrgent: false,
    isPending: false,
    isReady: true,
  };
}

describe("PortfolioTable pagination", () => {
  it("defaults to 10 rows and pages the rest", async () => {
    const rows = Array.from({ length: 12 }, (_, index) =>
      row({ contractId: `contract-${index}`, supplierName: `Supplier ${index}` }),
    );
    render(
      <MemoryRouter>
        <PortfolioTable rows={rows} moreColumns={false} />
      </MemoryRouter>,
    );

    expect(screen.getAllByRole("row")).toHaveLength(11);
    expect(screen.getByText("Supplier 0")).toBeInTheDocument();
    expect(screen.queryByText("Supplier 10")).not.toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Portfolio pages" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByText("Supplier 10")).toBeInTheDocument();
    expect(screen.queryByText("Supplier 0")).not.toBeInTheDocument();
  });
});
