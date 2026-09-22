import { describe, expect, it } from "vitest";
import type { PortfolioListItem } from "../../../src/api/client";
import {
  buildPortfolioHighlightHref,
  buildPortfolioRows,
  buildPortfolioSummary,
  filterRowsByContractIds,
  formatCompactAmount,
  formatHighlightNotice,
  formatPortfolioSummary,
  moreColumnsLabel,
  PORTFOLIO_CATEGORY_PARAM,
  PORTFOLIO_SUMMARY_OFF,
  readCategoryFilter,
  readContractIdsFilter,
  withCategoryFilter,
  type PortfolioRow,
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

describe("buildPortfolioRows (validated contracts only, sorted -- `kbContracts`)", () => {
  it("lists validated contracts only; processing, needs_review, failed and blank-status stay out", () => {
    const rows = buildPortfolioRows(
      [
        item({ contractId: "ok", status: "active", documentProcessingStatus: "Completed" }),
        item({ contractId: "uploading", status: "processing", documentProcessingStatus: "Uploaded", fileName: "CT-002_BluePeak_1.pdf" }),
        item({ contractId: "inprogress", status: "processing", documentProcessingStatus: "Processing", fileName: "CT-003_CobaltBridge_1.pdf" }),
        item({ contractId: "failed", status: "Failed", documentProcessingStatus: "Failed" }),
        item({ contractId: "review", status: "needs_review", documentProcessingStatus: "NeedsReview" }),
        item({ contractId: "parked", status: "active", documentProcessingStatus: "NeedsReview" }),
        item({ contractId: "blank", status: "  ", documentProcessingStatus: "Completed" }),
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

  it("counts every listed row -- the list is already validated-only", () => {
    const rows = buildPortfolioRows(
      [
        item({ contractId: "validated", annualSpend: 100_000, currency: "CHF", documentProcessingStatus: "Completed" }),
        item({ contractId: "review", annualSpend: 9_999_999, currency: "CHF", documentProcessingStatus: "NeedsReview", status: "needs_review" }),
      ],
      NOW,
    );

    expect(rows).toHaveLength(1);
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

// ---- `?ids=` highlight filter (the contracts an Ask evidence card links to) ----

const A = "11111111-1111-1111-1111-111111111111";
const B = "22222222-2222-2222-2222-222222222222";
const C = "33333333-3333-3333-3333-333333333333";

function highlightRow(contractId: string): PortfolioRow {
  return { item: { contractId } as PortfolioListItem, cancelDays: null, isUrgent: false };
}

describe("readContractIdsFilter", () => {
  it("reads a comma-separated `ids` list, trimming, de-duplicating and dropping blanks", () => {
    expect(readContractIdsFilter(new URLSearchParams(`ids=${A}, ${B},,${A}`))).toEqual([A, B]);
  });

  it("is empty when the parameter is absent", () => {
    expect(readContractIdsFilter(new URLSearchParams("category=SaaS"))).toEqual([]);
  });
});

describe("buildPortfolioHighlightHref", () => {
  it("builds the Portfolio route filtered to exactly those ids", () => {
    expect(buildPortfolioHighlightHref([A, B])).toBe(`/contracts?ids=${A},${B}`);
  });

  it("falls back to the bare route when no id is given", () => {
    expect(buildPortfolioHighlightHref([])).toBe("/contracts");
    expect(buildPortfolioHighlightHref([" ", ""])).toBe("/contracts");
  });

  it("round-trips through readContractIdsFilter", () => {
    const href = buildPortfolioHighlightHref([A, B]);
    expect(readContractIdsFilter(new URLSearchParams(href.slice(href.indexOf("?") + 1)))).toEqual([A, B]);
  });
});

describe("filterRowsByContractIds", () => {
  const rows = [highlightRow(A), highlightRow(B), highlightRow(C)];

  it("keeps only the highlighted rows, in table order", () => {
    expect(filterRowsByContractIds(rows, [C, A]).map((r) => r.item.contractId)).toEqual([A, C]);
  });

  it("returns every row when nothing is highlighted", () => {
    expect(filterRowsByContractIds(rows, []).map((r) => r.item.contractId)).toEqual([A, B, C]);
  });

  it("matches nothing for ids no longer in the portfolio", () => {
    expect(filterRowsByContractIds(rows, ["gone"])).toEqual([]);
  });
});

describe("formatHighlightNotice", () => {
  it("says how many of the highlighted contracts are shown", () => {
    expect(formatHighlightNotice(2, 7)).toBe("Showing 2 of 7 contracts — the ones highlighted in Ask.");
    expect(formatHighlightNotice(1, 1)).toBe("Showing 1 of 1 contract — the ones highlighted in Ask.");
  });

  it("explains an empty result instead of showing an empty table", () => {
    expect(formatHighlightNotice(0, 7)).toBe("None of the contracts highlighted in Ask is in the portfolio any more.");
  });
});
