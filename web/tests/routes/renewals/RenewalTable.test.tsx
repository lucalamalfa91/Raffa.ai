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
  return { item: item(overrides), score: 50, tracked: null };
}

describe("RenewalTable pagination", () => {
  it("defaults to 10 rows and pages the rest", async () => {
    const rows = Array.from({ length: 12 }, (_, index) =>
      row({
        contractId: `contract-${index}`,
        supplierName: `Renewal ${index}`,
      }),
    );
    render(<RenewalTable rows={rows} selectedContractId="contract-0" onSelect={vi.fn()} />);

    expect(screen.getAllByRole("row")).toHaveLength(11);
    expect(screen.getByText("Renewal 0")).toBeInTheDocument();
    expect(screen.queryByText("Renewal 10")).not.toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Renewal pages" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByText("Renewal 10")).toBeInTheDocument();
    expect(screen.queryByText("Renewal 0")).not.toBeInTheDocument();
  });
});
