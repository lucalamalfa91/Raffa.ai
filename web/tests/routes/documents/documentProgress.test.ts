import { describe, expect, it } from "vitest";
import type { DocumentListItemBody } from "../../../src/api/client";
import { getProgressView } from "../../../src/routes/documents/documentProgress";

/**
 * Updated for plan instant-upload-open: `getProgressStages` / the 6-stage checklist is replaced
 * by a two-chip model (`chip: ProcessingChip | null`). The `getProgressView` tests that previously
 * accessed `view.stages` now verify `view.chip` instead.
 */

function item(overrides: Partial<DocumentListItemBody> = {}): DocumentListItemBody {
  return {
    id: "doc-1",
    contractId: "contract-1",
    supplierName: "Salesforce",
    fileName: "Salesforce_MSA.pdf",
    documentType: "Msa",
    processingStatus: "Uploaded",
    stage: null,
    pageCount: 12,
    createdAt: "2026-09-06T08:05:00Z",
    weakFactCount: 0,
    rejectionReason: null,
    ...overrides,
  };
}

describe("getProgressView", () => {
  it("reads 'Queued, starting shortly' for Uploaded with no stage yet, isWaiting, chip=identifying, no link", () => {
    const view = getProgressView(item({ processingStatus: "Uploaded", stage: null }));

    expect(view.isWaiting).toBe(true);
    expect(view.chip).toBe("identifying");
    expect(view.headline).toBe("Queued, starting shortly");
    expect(view.link).toBeNull();
  });

  it("reads the real stage name in headline for Processing, chip reflects stage, no link", () => {
    const view = getProgressView(item({ processingStatus: "Processing", stage: "Extracting facts" }));

    expect(view.isWaiting).toBe(true);
    expect(view.headline).toBe("Extracting facts…");
    // 'Extracting facts' is the slow AI pass → enriching chip
    expect(view.chip).toBe("enriching");
    expect(view.link).toBeNull();
  });

  it("early stages produce identifying chip", () => {
    const view = getProgressView(item({ processingStatus: "Processing", stage: "OCR / text" }));
    expect(view.chip).toBe("identifying");
  });

  it("offers a review link for NeedsReview, no longer waiting, chip null", () => {
    const view = getProgressView(item({ id: "doc-7", processingStatus: "NeedsReview" }));

    expect(view.isWaiting).toBe(false);
    expect(view.chip).toBeNull();
    expect(view.link).toEqual({ href: "/documents?review=doc-7", label: "Review now" });
  });

  it("offers the contract link for a completed, non-Quote document", () => {
    const view = getProgressView(item({ processingStatus: "Completed", documentType: "Msa", contractId: "contract-9" }));

    expect(view.headline).toBe("Done -- it is now askable.");
    expect(view.link).toEqual({ href: "/contracts/contract-9", label: "Open the contract" });
  });

  it("offers Quote check for a completed Quote, never a contract link", () => {
    const view = getProgressView(item({ processingStatus: "Completed", documentType: "Quote", contractId: "contract-9" }));

    expect(view.headline).toBe("Ready in Quote check.");
    expect(view.link).toEqual({ href: "/quotes", label: "Open Quote check" });
  });

  it("offers no link for a completed document with no contract id yet (defensive)", () => {
    const view = getProgressView(item({ processingStatus: "Completed", documentType: "Msa", contractId: null }));

    expect(view.link).toBeNull();
  });

  it("reads the same sentence the row grid uses for Failed, no link", () => {
    const view = getProgressView(item({ processingStatus: "Failed" }));

    expect(view.isWaiting).toBe(false);
    expect(view.headline).toBe("Not yet linked to a contract");
    expect(view.link).toBeNull();
  });

  it("reads the reason sentence for a Rejected document with a known code, no link", () => {
    const view = getProgressView(item({ processingStatus: "Rejected", contractId: null, rejectionReason: "no_readable_text" }));

    expect(view.headline).toBe("Raffa.ai could not read any contract text in this file. Try a clearer scan or the original PDF.");
    expect(view.link).toBeNull();
  });

  it("falls back to an honest generic sentence for a Rejected document this app does not know", () => {
    const view = getProgressView(item({ processingStatus: "Rejected", contractId: null, rejectionReason: null }));

    expect(view.headline).toBe("Raffa.ai did not add this file.");
    expect(view.link).toBeNull();
  });
});
