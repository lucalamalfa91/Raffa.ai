import { describe, expect, it } from "vitest";
import type { DocumentListItemBody } from "../../../src/api/client";
import {
  DOCUMENT_PROCESSING_STAGES,
  buildKbSummary,
  filterDocumentsByAttention,
  formatUploadedAt,
  getDocumentTypeLabel,
  getFilterHint,
  getRowAction,
  getRowStatus,
  getRowStatusTag,
  getStagePercent,
  isAttentionStatus,
  type DocumentCountsBody,
} from "../../../src/routes/documents/documentTable";

function item(overrides: Partial<DocumentListItemBody> = {}): DocumentListItemBody {
  return {
    id: "doc-1",
    contractId: "contract-1",
    supplierName: "Salesforce",
    fileName: "Salesforce_MSA.pdf",
    documentType: "Msa",
    processingStatus: "Completed",
    stage: null,
    pageCount: 12,
    createdAt: "2026-09-06T08:05:00Z",
    weakFactCount: 0,
    rejectionReason: null,
    ...overrides,
  };
}

function counts(overrides: Partial<DocumentCountsBody> = {}): DocumentCountsBody {
  return { all: 0, needsAttention: 0, needsReview: 0, processing: 0, rejected: 0, ...overrides };
}

// Task E13/F04/US01/T01's widened admitted-type vocabulary (web/openapi/raffa-api.v1.json).
describe("getDocumentTypeLabel", () => {
  it.each<{ documentType: DocumentListItemBody["documentType"]; label: string }>([
    { documentType: "Msa", label: "MSA" },
    { documentType: "OrderForm", label: "Order Form" },
    { documentType: "Sow", label: "SOW" },
    { documentType: "Amendment", label: "Amendment" },
    { documentType: "RenewalLetter", label: "Renewal Letter" },
    { documentType: "Quote", label: "Quote" },
    { documentType: "Invoice", label: "Invoice" },
    { documentType: "PriceList", label: "Price List" },
    { documentType: "Nda", label: "NDA" },
    { documentType: "Dpa", label: "DPA" },
    { documentType: "Other", label: "Other" },
  ])("maps $documentType to '$label'", ({ documentType, label }) => {
    expect(getDocumentTypeLabel(documentType)).toBe(label);
  });
});

describe("formatUploadedAt", () => {
  it("formats a fixed UTC date/time regardless of the host timezone/locale", () => {
    expect(formatUploadedAt("2026-09-06T08:05:00Z")).toBe("06/09/2026, 08:05");
  });
});

describe("getRowStatus / getRowStatusTag", () => {
  it.each<{ processingStatus: DocumentListItemBody["processingStatus"]; status: string; variant: string; label: string }>([
    { processingStatus: "Uploaded", status: "processing", variant: "neutral", label: "Processing" },
    { processingStatus: "Processing", status: "processing", variant: "neutral", label: "Processing" },
    { processingStatus: "NeedsReview", status: "needs_review", variant: "outline", label: "Needs review" },
    { processingStatus: "Completed", status: "completed", variant: "neutral", label: "Completed" },
    { processingStatus: "Failed", status: "failed", variant: "accent", label: "Failed" },
    // Task E16/F03/US01/T01 (ADR-019 w15 clause 1): a refusal is a row, read as "Not added" in the
    // outline treatment -- never `failed`'s accent, and never a blank tag.
    { processingStatus: "Rejected", status: "rejected", variant: "outline", label: "Not added" },
  ])("maps $processingStatus to row status $status ($variant)", ({ processingStatus, status, variant, label }) => {
    const rowStatus = getRowStatus(processingStatus);
    expect(rowStatus).toBe(status);
    expect(getRowStatusTag(rowStatus)).toEqual({ variant, label });
  });
});

describe("getRowAction", () => {
  it("offers 'Review N fields' for needs_review, with the real weakFactCount (screens-v2.md #3)", () => {
    expect(getRowAction(item({ processingStatus: "NeedsReview", weakFactCount: 3 }))).toEqual({
      kind: "review",
      label: "Review 3 fields",
    });
    expect(getRowAction(item({ processingStatus: "NeedsReview", weakFactCount: 1 }))).toEqual({
      kind: "review",
      label: "Review 1 field",
    });
  });

  it("offers 'Ask about it' for completed, non-Quote documents", () => {
    expect(getRowAction(item({ processingStatus: "Completed", documentType: "Msa" }))).toEqual({
      kind: "ask",
      label: "Ask about it",
    });
  });

  it("offers 'Retry upload' for failed documents", () => {
    expect(getRowAction(item({ processingStatus: "Failed" }))).toEqual({ kind: "retry", label: "Retry upload" });
  });

  it("returns null while still processing (the stage text renders instead, not an action)", () => {
    expect(getRowAction(item({ processingStatus: "Processing" }))).toBeNull();
  });

  // ADR-020 w15 §1.4: a refused file offers no next step -- nothing to review, ask or retry.
  it("returns null for a rejected document", () => {
    expect(getRowAction(item({ processingStatus: "Rejected", contractId: null }))).toBeNull();
  });

  // OQ-askv2-008: a Quote is routed to Quote check, never the review/ask flow, at any resolved status.
  it.each<DocumentListItemBody["processingStatus"]>(["Completed", "NeedsReview"])(
    "offers 'Open Quote check' for a Quote-typed document even when %s",
    (processingStatus) => {
      expect(getRowAction(item({ processingStatus, documentType: "Quote", weakFactCount: 2 }))).toEqual({
        kind: "quote",
        label: "Open Quote check",
      });
    },
  );
});

// Task E16/F03/US01/T01 (ADR-027 §C9, ADR-012 w15 §13.5): the client filter mirrors the server's
// `counts.needsAttention` definition -- not Completed and not Rejected -- so a chip's number and
// the rows it filters to are the same set. This suite, not `tsc`, is what keeps the two in step.
describe("isAttentionStatus / filterDocumentsByAttention", () => {
  const items = [
    item({ id: "a", processingStatus: "Processing" }),
    item({ id: "b", processingStatus: "NeedsReview" }),
    item({ id: "c", processingStatus: "Failed" }),
    item({ id: "d", processingStatus: "Completed" }),
    item({ id: "e", processingStatus: "Rejected", contractId: null, rejectionReason: "not_a_contract" }),
    item({ id: "f", processingStatus: "Uploaded" }),
  ];

  it("attention is every status except Completed and Rejected (the server's own needsAttention set)", () => {
    expect(items.map((candidate) => isAttentionStatus(candidate.processingStatus))).toEqual([true, true, true, false, false, true]);
    expect(filterDocumentsByAttention(items, "attention").map((candidate) => candidate.id)).toEqual(["a", "b", "c", "f"]);
  });

  it("all is everything Raffa.ai keeps -- `counts.all`'s definition, which excludes Rejected", () => {
    expect(filterDocumentsByAttention(items, "all").map((candidate) => candidate.id)).toEqual(["a", "b", "c", "d", "f"]);
  });

  it("rejected is the refused rows alone", () => {
    expect(filterDocumentsByAttention(items, "rejected").map((candidate) => candidate.id)).toEqual(["e"]);
  });
});

// ADR-012 w15 §13.6/§18: the summary is a function of the server's `counts`, never of the fetched
// page, and the "M askable" segment is gone -- askability is a contract-level fact the shell carries.
describe("buildKbSummary", () => {
  it("reads 'N documents' off counts.all and 'K waiting for your review' off counts.needsReview", () => {
    expect(buildKbSummary(counts({ all: 4, needsAttention: 2, needsReview: 1, processing: 1 }))).toBe(
      "4 documents · 1 waiting for your review",
    );
  });

  it("omits the waiting clause when nothing needs review, uses singular 'document', and never says 'askable'", () => {
    const summary = buildKbSummary(counts({ all: 1 }));
    expect(summary).toBe("1 document");
    expect(summary).not.toMatch(/askable/);
  });

  it("does not count refused files -- `all` already excludes them", () => {
    expect(buildKbSummary(counts({ all: 0, rejected: 2 }))).toBe("0 documents");
  });
});

describe("getFilterHint", () => {
  it("quotes raffa-v2/app.jsx's own attention hint, and ADR-020 w15 §1.5's hints for the other two chips", () => {
    expect(getFilterHint("attention")).toBe("Completed documents are hidden — they are already askable.");
    expect(getFilterHint("all")).toBe("Everything Raffa.ai keeps, including validated documents.");
    expect(getFilterHint("rejected")).toBe("Files Raffa.ai did not keep — never counted, never askable.");
  });
});

describe("getStagePercent", () => {
  it("returns 0 for null (not yet started) or an unrecognised stage", () => {
    expect(getStagePercent(null)).toBe(0);
    expect(getStagePercent("Unknown stage")).toBe(0);
  });

  it("computes percent from the stage's position among the six real stages (R-DOC-09)", () => {
    expect(DOCUMENT_PROCESSING_STAGES).toEqual([
      "Uploading",
      "Classifying",
      "OCR / text",
      "Sections & tables",
      "Extracting facts",
      "Validating schema",
    ]);
    expect(getStagePercent("Uploading")).toBe(17); // 1/6
    expect(getStagePercent("Validating schema")).toBe(100); // 6/6
  });
});
