import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { RenewalPipelineItemBody } from "../../../src/api/client";
import RenewalTable from "../../../src/routes/renewals/RenewalTable";
import type { RenewalTableRow } from "../../../src/routes/renewals/renewalPipelineViewModel";

function item(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalPipelineItemBody {
  return {
    contractId: "11111111-1111-1111-1111-111111111111",
    supplierId: null,
    supplierName: "Acme",
    status: "Determined",
    contractStatus: "active",
    documentProcessingStatus: "Completed",
    renewalDate: "2027-01-01",
    daysUntilRenewal: 120,
    annualSpend: 50000,
    cancellationDeadline: "2026-11-01",
    daysUntilCancellationDeadline: 90,
    autoRenewal: true,
    action: "Renegotiate rate",
    savedAction: null,
    priority: null,
    insightCard: {
      facts: {
        supplierId: null,
        supplierName: "Acme",
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

function row(overrides: Partial<RenewalPipelineItemBody> = {}): RenewalTableRow {
  return { item: item(overrides), score: 50, tracked: null, contract: null };
}

describe("RenewalTable pagination", () => {
  it("defaults to 10 rows and pages the rest", async () => {
    const rows = Array.from({ length: 12 }, (_, index) =>
      row({
        contractId: `contract-${index}`,
        supplierName: `Renewal ${index}`,
      }),
    );
    render(
      <RenewalTable
        rows={rows}
        selectedContractId="contract-0"
        onSelect={vi.fn()}
        checkedIds={new Set()}
        onToggleChecked={vi.fn()}
        onToggleAllChecked={vi.fn()}
      />,
    );

    expect(screen.getAllByRole("row")).toHaveLength(11);
    expect(screen.getByText("Renewal 0")).toBeInTheDocument();
    expect(screen.queryByText("Renewal 10")).not.toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Renewal pages" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByText("Renewal 10")).toBeInTheDocument();
    expect(screen.queryByText("Renewal 0")).not.toBeInTheDocument();
  });
});

describe("RenewalTable selection for bulk actions", () => {
  it("ticks a row without selecting it, names the contract type the portfolio knows, and puts the currency on the spend", async () => {
    const onSelect = vi.fn();
    const onToggleChecked = vi.fn();
    const onToggleAllChecked = vi.fn();
    const rows: RenewalTableRow[] = [
      { ...row({ contractId: "contract-a", supplierName: "Salesforce", annualSpend: 500_000 }), contract: { typeLabel: "MSA", currency: "CHF" } },
      row({ contractId: "contract-b", supplierName: "Fabrikam" }),
    ];
    render(
      <RenewalTable
        rows={rows}
        selectedContractId="contract-a"
        onSelect={onSelect}
        checkedIds={new Set(["contract-b"])}
        onToggleChecked={onToggleChecked}
        onToggleAllChecked={onToggleAllChecked}
      />,
    );

    expect(screen.getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "",
      "Score",
      "Supplier · contract",
      "Annual spend",
      "Renews in",
      "Notice in",
      "Status",
    ]);
    expect(screen.getByText("· MSA")).toBeInTheDocument();
    expect(screen.getByText("CHF 500k")).toBeInTheDocument();
    // Without portfolio info: the id fragment, and no invented currency.
    expect(screen.getByText("· Contract contract")).toBeInTheDocument();
    expect(screen.getByText("50k")).toBeInTheDocument();

    const tickB = screen.getByRole("checkbox", { name: "Select Fabrikam · Contract contract" });
    expect(tickB).toBeChecked();
    await userEvent.click(screen.getByRole("checkbox", { name: "Select Salesforce · MSA" }));
    expect(onToggleChecked).toHaveBeenCalledWith("contract-a");
    expect(onSelect).not.toHaveBeenCalled();

    const all = screen.getByRole("checkbox", { name: "Select all 2 renewals in this list" });
    expect((all as HTMLInputElement).indeterminate).toBe(true);
    await userEvent.click(all);
    expect(onToggleAllChecked).toHaveBeenCalledTimes(1);
  });
});
