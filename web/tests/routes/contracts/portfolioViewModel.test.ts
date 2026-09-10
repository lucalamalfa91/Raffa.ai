import { describe, expect, it } from "vitest";
import type { PortfolioListItem } from "../../../src/api/client";
import {
  buildPortfolioRows,
  buildPortfolioSummary,
  formatCompactAmount,
  formatPortfolioSummary,
  moreColumnsLabel,
  PORTFOLIO_SUMMARY_OFF,
} from "../../../src/routes/contracts/portfolioViewModel";

const NOW = new Date(Date.UTC(2026, 8, 9)); // 2026-09-09

function item(overrides: Partial<PortfolioListItem> = {}): PortfolioListItem {
  return {
    contractId: "contract-1",
    supplierId: null,
    supplierName: "Salesforce",
    type: "Msa",
    annualSpend: 640_000,
    currency: "CHF",
    startDate: "2024-03-01",
    endDate: "2027-01-15",
    renewalDate: "2027-01-15",
    cancellationDeadline: "2026-10-18",
    autoRenewal: true,
    status: "active",
    risk: "High",
    ...overrides,
  };
}

describe("buildPortfolioRows (validated contracts only, sorted by notice deadline -- app.jsx kbContracts)", () => {
  it("keeps only validated contracts: processing, failed and needs-review rows never reach the portfolio", () => {
    const rows = buildPortfolioRows(
      [
        item({ contractId: "ok", status: "active" }),
        item({ contractId: "processing", status: "processing" }),
        item({ contractId: "failed", status: "Failed" }),
        item({ contractId: "review", status: "needs_review" }),
        item({ contractId: "blank", status: "  " }),
      ],
      NOW,
    );

    expect(rows.map((row) => row.item.contractId)).toEqual(["ok"]);
  });

  it("sorts by the soonest notice deadline; rows without a deadline come last, ties break on end date then id", () => {
    const rows = buildPortfolioRows(
      [
        item({ contractId: "later", cancellationDeadline: "2027-03-31" }),
        item({ contractId: "none-b", cancellationDeadline: null, endDate: "2028-01-01" }),
        item({ contractId: "none-a", cancellationDeadline: null, endDate: "2027-06-30" }),
        item({ contractId: "soon", cancellationDeadline: "2026-10-18" }),
      ],
      NOW,
    );

    expect(rows.map((row) => row.item.contractId)).toEqual(["soon", "later", "none-a", "none-b"]);
  });

  it("flags a notice deadline within 45 days as urgent (the locked ADR-019 threshold, inclusive), with the real day count", () => {
    const rows = buildPortfolioRows(
      [
        item({ contractId: "in-39", cancellationDeadline: "2026-10-18" }),
        item({ contractId: "in-45", cancellationDeadline: "2026-10-24" }),
        item({ contractId: "in-46", cancellationDeadline: "2026-10-25" }),
        item({ contractId: "none", cancellationDeadline: null }),
      ],
      NOW,
    );

    expect(rows.map((row) => [row.item.contractId, row.cancelDays, row.isUrgent])).toEqual([
      ["in-39", 39, true],
      ["in-45", 45, true],
      ["in-46", 46, false],
      ["none", null, false],
    ]);
  });
});

describe("formatCompactAmount (app.jsx#chf, generalised to the row's own currency)", () => {
  it("renders millions with one decimal and thousands rounded to k", () => {
    expect(formatCompactAmount(4_240_000, "CHF")).toBe("CHF 4.2M");
    expect(formatCompactAmount(640_000, "CHF")).toBe("CHF 640k");
    expect(formatCompactAmount(58_000, "EUR")).toBe("EUR 58k");
  });

  it("keeps a plain figure under a thousand and no code when the row carried none", () => {
    expect(formatCompactAmount(900, "CHF")).toBe("CHF 900");
    expect(formatCompactAmount(48_000, null)).toBe("48k");
  });
});

describe("buildPortfolioSummary / formatPortfolioSummary (app.jsx pfSummary)", () => {
  it("counts validated contracts, sums annual spend per currency (largest first) and counts urgent deadlines", () => {
    const rows = buildPortfolioRows(
      [
        item({ contractId: "a", annualSpend: 640_000, currency: "CHF", cancellationDeadline: "2026-10-18" }),
        item({ contractId: "b", annualSpend: 1_200_000, currency: "CHF", cancellationDeadline: "2027-03-31" }),
        item({ contractId: "c", annualSpend: 48_000, currency: "EUR", cancellationDeadline: null }),
        item({ contractId: "d", annualSpend: null, currency: "CHF", cancellationDeadline: "2026-10-02" }),
      ],
      NOW,
    );

    const summary = buildPortfolioSummary(rows);

    expect(summary.validatedCount).toBe(4);
    expect(summary.annualSpend).toEqual([
      { currency: "CHF", total: 1_840_000 },
      { currency: "EUR", total: 48_000 },
    ]);
    expect(summary.urgentCount).toBe(2);
    expect(formatPortfolioSummary(summary)).toBe("4 validated contracts · CHF 1.8M + EUR 48k annual · 2 notice deadlines within 45 days");
  });

  it("singularises and drops the deadline clause when nothing is urgent, exactly as the prototype appends it", () => {
    const summary = buildPortfolioSummary(buildPortfolioRows([item({ cancellationDeadline: "2027-03-31" })], NOW));

    expect(formatPortfolioSummary(summary)).toBe("1 validated contract · CHF 640k annual");
  });

  it("falls back to the prototype's own off-tier line when no contract is validated", () => {
    expect(formatPortfolioSummary(buildPortfolioSummary([]))).toBe(PORTFOLIO_SUMMARY_OFF);
    expect(PORTFOLIO_SUMMARY_OFF).toBe("Lights up from validated contracts");
  });
});

describe("moreColumnsLabel (app.jsx colsLabel)", () => {
  it("toggles between the prototype's two labels", () => {
    expect(moreColumnsLabel(false)).toBe("More columns");
    expect(moreColumnsLabel(true)).toBe("Fewer columns");
  });
});
