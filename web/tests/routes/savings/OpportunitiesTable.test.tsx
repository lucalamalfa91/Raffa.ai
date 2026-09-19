import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import OpportunitiesTable from "../../../src/routes/savings/OpportunitiesTable";
import type { OpportunityRowView } from "../../../src/routes/savings/savingsViewModel";

function row(overrides: Partial<OpportunityRowView> = {}): OpportunityRowView {
  return {
    key: "opp-1",
    supplierLabel: "Salesforce",
    supplierTitle: undefined,
    action: "Renewal",
    estimate: "CHF 10,000",
    currency: "CHF",
    confidence: { variant: "neutral", label: "High · 92%" },
    status: { variant: "neutral", label: "Identified" },
    statusValue: "Identified",
    navigation: { kind: "contract", contractId: "contract-1" },
    ...overrides,
  };
}

describe("OpportunitiesTable pagination", () => {
  it("defaults to 10 rows and pages the rest", async () => {
    const rows = Array.from({ length: 12 }, (_, index) =>
      row({
        key: `opp-${index}`,
        supplierLabel: `Saving ${index}`,
        navigation: { kind: "contract", contractId: `contract-${index}` },
      }),
    );
    render(
      <MemoryRouter>
        <OpportunitiesTable rows={rows} />
      </MemoryRouter>,
    );

    expect(screen.getAllByRole("row")).toHaveLength(11);
    expect(screen.getByRole("link", { name: "Saving 0" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Saving 10" })).not.toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Opportunity pages" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByRole("link", { name: "Saving 10" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Saving 0" })).not.toBeInTheDocument();
  });
});
