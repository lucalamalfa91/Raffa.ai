import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import OpportunitiesTable from "../../../src/routes/savings/OpportunitiesTable";
import type { OpportunityRowView } from "../../../src/routes/savings/savingsViewModel";

function row(overrides: Partial<OpportunityRowView> = {}): OpportunityRowView {
  return {
    key: "opp-1",
    contractId: "contract-1",
    supplierLabel: "Salesforce",
    supplierTitle: undefined,
    action: "Renewal",
    currentSpend: "CHF 100,000",
    estimate: "CHF 10,000",
    currency: "CHF",
    confidence: { variant: "neutral", label: "High · 92%" },
    status: { variant: "neutral", label: "Identified" },
    statusValue: "Identified",
    navigation: { kind: "contract", contractId: "contract-1" },
    noticeDays: null,
    noticeUrgent: false,
    askQuestion: "Where can we save with Salesforce?",
    renewalHref: null,
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
    expect(screen.getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Supplier",
      "Lever",
      "Current spend",
      "Estimate",
      "Status",
      "Notice in",
      "Next step",
    ]);
    expect(screen.getByRole("link", { name: "Saving 0" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Saving 10" })).not.toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Opportunity pages" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByRole("link", { name: "Saving 10" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Saving 0" })).not.toBeInTheDocument();
  });
});

describe("OpportunitiesTable next step", () => {
  it("offers Renewals (with the contract selected) while a notice date is ahead, and a scoped Ask on every row", () => {
    render(
      <MemoryRouter>
        <OpportunitiesTable
          rows={[
            row({ noticeDays: 14, noticeUrgent: true, renewalHref: "/renewals?select=contract-1" }),
            row({ key: "opp-2", supplierLabel: "Fabrikam", contractId: "contract-2", navigation: { kind: "contract", contractId: "contract-2" } }),
          ]}
        />
      </MemoryRouter>,
    );

    expect(screen.getByRole("link", { name: "Work the Salesforce saving in Renewals" })).toHaveAttribute("href", "/renewals?select=contract-1");
    expect(screen.queryByRole("link", { name: "Work the Fabrikam saving in Renewals" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ask Raffa about the Salesforce saving" })).toHaveAttribute("href", "/ask?scope=contract-1");
    expect(screen.getByText("14 d")).toHaveClass("deadline-critical");
  });
});
