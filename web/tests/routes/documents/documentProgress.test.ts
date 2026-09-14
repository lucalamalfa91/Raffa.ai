import { describe, expect, it } from "vitest";
import type { DocumentListItemBody } from "../../../src/api/client";
import { getProgressStages, getProgressView } from "../../../src/routes/documents/documentProgress";
import { DOCUMENT_PROCESSING_STAGES } from "../../../src/routes/documents/documentTable";

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

// Task E16/F03/US02/T02 (wave w15, ADR-020 w15 footer 11): the checklist reads the same source
// documentTable.ts#getStagePercent reads for the row's own inline bar, so the two can never
// disagree on where a document is.
describe("getProgressStages", () => {
  it("marks every stage 'todo' for a null stage -- queued, nothing started yet", () => {
    const stages = getProgressStages(null);
    expect(stages.map((s) => s.name)).toEqual(DOCUMENT_PROCESSING_STAGES);
    expect(stages.every((s) => s.state === "todo")).toBe(true);
  });

  it("marks earlier stages 'done', the real stage 'current', later ones 'todo'", () => {
    const stages = getProgressStages("OCR / text"); // index 2 of 6
    expect(stages.map((s) => s.state)).toEqual(["done", "done", "current", "todo", "todo", "todo"]);
  });

  it("marks the final stage 'current' with nothing left 'todo'", () => {
    const stages = getProgressStages("Validating schema");
    expect(stages.map((s) => s.state)).toEqual(["done", "done", "done", "done", "done", "current"]);
  });

  it("treats an unrecognised stage the same as null -- all 'todo', an honest just-started reading", () => {
    expect(getProgressStages("Some future stage").every((s) => s.state === "todo")).toBe(true);
  });
});

describe("getProgressView", () => {
  it("reads 'Queued, starting shortly' for Uploaded with no stage yet, waiting, no link", () => {
    const view = getProgressView(item({ processingStatus: "Uploaded", stage: null }));

    expect(view.isWaiting).toBe(true);
    expect(view.headline).toBe("Queued, starting shortly");
    expect(view.link).toBeNull();
  });

  it("reads the real stage name for Processing, waiting, no link", () => {
    const view = getProgressView(item({ processingStatus: "Processing", stage: "Extracting facts" }));

    expect(view.isWaiting).toBe(true);
    expect(view.headline).toBe("Extracting facts…");
    expect(view.stages.find((s) => s.name === "Extracting facts")?.state).toBe("current");
    expect(view.link).toBeNull();
  });

  it("offers a review link for NeedsReview, no longer waiting", () => {
    const view = getProgressView(item({ id: "doc-7", processingStatus: "NeedsReview" }));

    expect(view.isWaiting).toBe(false);
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
