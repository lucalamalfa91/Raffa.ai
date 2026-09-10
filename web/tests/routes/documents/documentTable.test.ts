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
    ...overrides,
  };
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

describe("isAttentionStatus / filterDocumentsByAttention", () => {
  const items = [
    item({ id: "a", processingStatus: "Processing" }),
    item({ id: "b", processingStatus: "NeedsReview" }),
    item({ id: "c", processingStatus: "Failed" }),
    item({ id: "d", processingStatus: "Completed" }),
  ];

  it("attention excludes only completed documents (raffa-v2/app.jsx's own attnDocs)", () => {
    expect(items.map((candidate) => isAttentionStatus(candidate.processingStatus))).toEqual([true, true, true, false]);
    expect(filterDocumentsByAttention(items, "attention").map((candidate) => candidate.id)).toEqual(["a", "b", "c"]);
  });

  it("all returns every document unfiltered", () => {
    expect(filterDocumentsByAttention(items, "all")).toEqual(items);
  });
});

describe("buildKbSummary", () => {
  it("counts total/askable/waiting exactly as raffa-v2/app.jsx's own kbSummary", () => {
    const items = [
      item({ id: "a", processingStatus: "Completed" }),
      item({ id: "b", processingStatus: "Completed" }),
      item({ id: "c", processingStatus: "NeedsReview" }),
      item({ id: "d", processingStatus: "Processing" }),
    ];
    expect(buildKbSummary(items)).toBe("4 documents · 2 askable · 1 waiting for your review");
  });

  it("omits the waiting clause when nothing needs review, and uses singular 'document'", () => {
    expect(buildKbSummary([item({ processingStatus: "Completed" })])).toBe("1 document · 1 askable");
  });
});

describe("getFilterHint", () => {
  it("quotes raffa-v2/app.jsx's own filterHint ternary verbatim", () => {
    expect(getFilterHint("attention")).toBe("Completed documents are hidden — they are already askable.");
    expect(getFilterHint("all")).toBe("Everything, including validated documents.");
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
