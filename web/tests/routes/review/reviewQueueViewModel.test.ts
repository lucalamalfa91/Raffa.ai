import { describe, expect, it } from "vitest";
import { buildReviewQueue } from "../../../src/routes/review/reviewQueueViewModel";
import type { PortfolioListItem } from "../../../src/api/client";
import type { TrackedDocument } from "../../../src/routes/documents/documentStore";

function item(overrides: Partial<PortfolioListItem> = {}): PortfolioListItem {
  return {
    contractId: "contract-1",
    supplierId: null,
    // Task E13/F03/US01/T02: supplierName is required now (null when unresolved).
    supplierName: null,
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

function tracked(overrides: Partial<TrackedDocument> = {}): TrackedDocument {
  return {
    id: "doc-1",
    contractId: "contract-2",
    fileName: "Acme_OrderForm.pdf",
    documentType: "OrderForm",
    processingStatus: "NeedsReview",
    createdAt: "2026-09-06T08:00:00Z",
    ...overrides,
  };
}

describe("buildReviewQueue", () => {
  it("keeps portfolio rows whose status contains review", () => {
    const rows = buildReviewQueue([item({ status: "needs_review" }), item({ contractId: "c-2", status: "active" })], []);
    expect(rows).toHaveLength(1);
    expect(rows[0]?.contractId).toBe("contract-1");
    expect(rows[0]?.statusTag.label).toBe("Needs review");
  });

  it("adds session uploads still in NeedsReview that are not already on the portfolio page", () => {
    const rows = buildReviewQueue([item({ status: "needs_review" })], [tracked()]);
    expect(rows.map((row) => row.id)).toEqual(["contract-1", "doc-1"]);
  });

  it("does not duplicate an upload whose contractId is already a needs-review portfolio row", () => {
    const rows = buildReviewQueue(
      [item({ status: "needs_review" })],
      [tracked({ contractId: "contract-1" })],
    );
    expect(rows).toHaveLength(1);
    expect(rows[0]?.id).toBe("contract-1");
  });

  it("keeps unlinked NeedsReview uploads with a null contractId", () => {
    const rows = buildReviewQueue([], [tracked({ contractId: null })]);
    expect(rows).toHaveLength(1);
    expect(rows[0]?.contractId).toBeNull();
    expect(rows[0]?.label).toBe("Acme_OrderForm.pdf");
  });
});
