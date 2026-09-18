import { describe, expect, it } from "vitest";
import type { PortfolioListItem } from "../../../src/api/client";
import {
  buildPortfolioRows,
  buildPortfolioSummary,
  formatCompactAmount,
  formatPortfolioSummary,
  moreColumnsLabel,
  PORTFOLIO_CATEGORY_PARAM,
  PORTFOLIO_SUMMARY_OFF,
  readCategoryFilter,
  withCategoryFilter,
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
    fileName: null,
    documentProcessingStatus: "Completed",
    ...overrides,
  };
}

describe("buildPortfolioRows (all contracts including pending, sorted -- app.jsx kbContracts)", () => {
  it("includes validated, pending, and needs_review contracts; failed and blank-status stay excluded", () => {
    const rows = buildPortfolioRows(
      [
        item({ contractId: "ok", status: "active", documentProcessingStatus: "Completed" }),
        item({ contractId: "uploading", status: "processing", documentProcessingStatus: "Uploaded", fileName: "CT-002_BluePeak_1.pdf" }),
        item({ contractId: "inprogress", status: "processing", documentProcessingStatus: "Processing", fileName: "CT-003_CobaltBridge_1.pdf" }),
        item({ contractId: "failed", status: "Failed", documentProcessingStatus: "Failed" }),
        item({ contractId: "review", status: "needs_review", documentProcessingStatus: "NeedsReview" }),
        item({ contractId: "blank", status: "  ", documentProcessingStatus: "Completed" }),
      ],
      NOW,
    );

    const ids = rows.map((row) => row.item.contractId);
    expect(ids).toContain("ok");
    expect(ids).toContain("uploading");
    expect(ids).toContain("inprogress");
    expect(ids).toContain("review");
    expect(ids).not.toContain("failed");
    expect(ids).not.toContain("blank");

    const byId = Object.fromEntries(rows.map((row) => [row.item.contractId, row.isReady]));
    expect(byId["ok"]).toBe(true);
    expect(byId["uploading"]).toBe(false);
    expect(byId["inprogress"]).toBe(false);
    expect(byId["review"]).toBe(false);
  });

  it("w17: marks Uploaded and Processing documents as isPending=true; Completed rows as isPending=false", () => {
    const rows = buildPortfolioRows(
      [
        item({ contractId: "uploaded", status: "processing", documentProcessingStatus: "Uploaded" }),
        item({ contractId: "inprogress", status: "processing", documentProcessingStatus: "Processing" }),
        item({ contractId: "done", status: "active", documentProcessingStatus: "Completed" }),
      ],
      NOW,
    );

    const byId = Object.fromEntries(rows.map((r) => [r.item.contractId, r.isPending]));
    expect(byId["uploaded"]).toBe(true);
    expect(byId["inprogress"]).toBe(true);
    expect(byId["done"]).toBe(false);
  });

  it("sorts ready rows first, then needs-review, then pending, regardless of deadline", () => {
    const rows = buildPortfolioRows(
      [
        item({ contractId: "pending-early", documentProcessingStatus: "Uploaded", cancellationDeadline: "2026-10-01" }),
        item({ contractId: "review-mid", status: "needs_review", documentProcessingStatus: "NeedsReview", cancellationDeadline: "2026-09-20" }),
        item({ contractId: "validated-late", documentProcessingStatus: "Completed", cancellationDeadline: "2027-06-30" }),
        item({ contractId: "validated-soon", documentProcessingStatus: "Completed", cancellationDeadline: "2026-10-18" }),
      ],
      NOW,
    );

    expect(rows.map((r) => r.item.contractId)).toEqual(["validated-soon", "validated-late", "review-mid", "pending-early"]);
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

  it("pending and needs_review contracts are excluded from validatedCount even though they appear in the full list", () => {
    const rows = buildPortfolioRows(
      [
        item({ contractId: "validated", annualSpend: 100_000, currency: "CHF", documentProcessingStatus: "Completed" }),
        item({ contractId: "uploading", annualSpend: null, currency: "CHF", documentProcessingStatus: "Uploaded", status: "processing", fileName: "CT-002_BluePeak_1.pdf" }),
        item({ contractId: "in-progress", annualSpend: null, currency: "CHF", documentProcessingStatus: "Processing", status: "processing", fileName: "CT-003_CobaltBridge_1.pdf" }),
        item({ contractId: "review", annualSpend: 9_999_999, currency: "CHF", documentProcessingStatus: "NeedsReview", status: "needs_review" }),
      ],
      NOW,
    );

    // All three rows appear in the table
    expect(rows).toHaveLength(4);

    // But only the validated contract is counted in the summary
    const summary = buildPortfolioSummary(rows);
    expect(summary.validatedCount).toBe(1);
    expect(summary.annualSpend).toEqual([{ currency: "CHF", total: 100_000 }]);
  });
});

describe("moreColumnsLabel (app.jsx colsLabel)", () => {
  it("toggles between the prototype's two labels", () => {
    expect(moreColumnsLabel(false)).toBe("More columns");
    expect(moreColumnsLabel(true)).toBe("Fewer columns");
  });
});

// `PortfolioRoute.test.tsx` for the route still forwarding `?category=` to getPortfolio.
describe("readCategoryFilter", () => {
  it("AC-2: an absent category normalizes to \"\"", () => {
    expect(readCategoryFilter(new URLSearchParams())).toBe("");
  });

  it("AC-2: a blank category normalizes to \"\"", () => {
    expect(readCategoryFilter(new URLSearchParams("category="))).toBe("");
  });

  it("trims surrounding whitespace, matching PortfolioEndpointExtensions.TryParseFilter's own Trim()", () => {
    expect(readCategoryFilter(new URLSearchParams("category=%20Software%20"))).toBe("Software");
  });

  it("a whitespace-only category also normalizes to \"\"", () => {
    expect(readCategoryFilter(new URLSearchParams("category=%20%20"))).toBe("");
  });

  it("reads a real value verbatim", () => {
    expect(readCategoryFilter(new URLSearchParams("category=Logistics"))).toBe("Logistics");
  });
});

describe("withCategoryFilter", () => {
  it("AC-1: sets the category query parameter", () => {
    const next = withCategoryFilter(new URLSearchParams(), "Software");
    expect(next.get(PORTFOLIO_CATEGORY_PARAM)).toBe("Software");
  });

  it("trims before setting", () => {
    const next = withCategoryFilter(new URLSearchParams(), "  Software  ");
    expect(next.get(PORTFOLIO_CATEGORY_PARAM)).toBe("Software");
  });

  it("AC-3: clearing (\"\") removes the parameter entirely -- re-loads the full portfolio", () => {
    const next = withCategoryFilter(new URLSearchParams("category=Software"), "");
    expect(next.has(PORTFOLIO_CATEGORY_PARAM)).toBe(false);
  });

  it("clearing with a whitespace-only value also removes the parameter", () => {
    const next = withCategoryFilter(new URLSearchParams("category=Software"), "   ");
    expect(next.has(PORTFOLIO_CATEGORY_PARAM)).toBe(false);
  });

  it("leaves every other query parameter this screen does not own untouched", () => {
    const next = withCategoryFilter(new URLSearchParams("status=Active&page=2"), "Software");
    expect(next.get("status")).toBe("Active");
    expect(next.get("page")).toBe("2");
    expect(next.get(PORTFOLIO_CATEGORY_PARAM)).toBe("Software");
  });
});
