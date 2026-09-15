import { describe, expect, it } from "vitest";
import { getProgressView } from "./documentProgress";
import type { DocumentListItemBody } from "../../api/client";

/**
 * Unit tests for `documentProgress.ts` after the chip-model refactor (plan instant-upload-open).
 * The 6-stage `stages` array is replaced by a `chip` field; `isWaiting`, `headline`, and `link`
 * continue to work as before.
 */

type ProgressItem = Parameters<typeof getProgressView>[0];

function item(overrides: Partial<DocumentListItemBody> = {}): ProgressItem {
  return {
    id: "doc-1",
    processingStatus: "Uploaded",
    stage: null,
    contractId: null,
    documentType: "Msa",
    rejectionReason: null,
    ...overrides,
  };
}

describe("getProgressView — Uploaded / Processing states", () => {
  it("Uploaded, null stage → isWaiting=true, chip=identifying, headline 'Queued…'", () => {
    const view = getProgressView(item({ processingStatus: "Uploaded", stage: null }));
    expect(view.isWaiting).toBe(true);
    expect(view.chip).toBe("identifying");
    expect(view.headline).toMatch(/queued/i);
    expect(view.link).toBeNull();
  });

  it("Processing, null stage → isWaiting=true, chip=identifying", () => {
    const view = getProgressView(item({ processingStatus: "Processing", stage: null }));
    expect(view.isWaiting).toBe(true);
    expect(view.chip).toBe("identifying");
  });

  it("Processing, 'Extracting facts' → chip=enriching", () => {
    const view = getProgressView(item({ processingStatus: "Processing", stage: "Extracting facts" }));
    expect(view.isWaiting).toBe(true);
    expect(view.chip).toBe("enriching");
  });

  it("Processing, 'Validating schema' → chip=enriching", () => {
    const view = getProgressView(item({ processingStatus: "Processing", stage: "Validating schema" }));
    expect(view.chip).toBe("enriching");
  });

  it("Processing, 'Classifying' → chip=identifying", () => {
    const view = getProgressView(item({ processingStatus: "Processing", stage: "Classifying" }));
    expect(view.chip).toBe("identifying");
  });

  it("Processing with a real stage → headline includes the stage name", () => {
    const view = getProgressView(item({ processingStatus: "Processing", stage: "OCR / text" }));
    expect(view.headline).toContain("OCR / text");
  });
});

describe("getProgressView — terminal states return chip=null", () => {
  it("NeedsReview → chip null, has link", () => {
    const view = getProgressView(item({ processingStatus: "NeedsReview", id: "doc-1" }));
    expect(view.isWaiting).toBe(false);
    expect(view.chip).toBeNull();
    expect(view.link).not.toBeNull();
  });

  it("Completed with contractId → chip null, has link", () => {
    const view = getProgressView(item({ processingStatus: "Completed", contractId: "c-1", documentType: "Msa" }));
    expect(view.chip).toBeNull();
    expect(view.link).not.toBeNull();
  });

  it("Failed → chip null, no link", () => {
    const view = getProgressView(item({ processingStatus: "Failed" }));
    expect(view.chip).toBeNull();
    expect(view.link).toBeNull();
  });

  it("Rejected → chip null, headline from rejectionReason", () => {
    const view = getProgressView(item({ processingStatus: "Rejected", rejectionReason: "not_a_contract" }));
    expect(view.chip).toBeNull();
    expect(view.headline).toMatch(/recipe/i);
  });

  it("Rejected, unknown reason → chip null, safe fallback headline", () => {
    const view = getProgressView(item({ processingStatus: "Rejected", rejectionReason: null }));
    expect(view.chip).toBeNull();
    expect(view.headline).toBeTruthy();
  });
});
