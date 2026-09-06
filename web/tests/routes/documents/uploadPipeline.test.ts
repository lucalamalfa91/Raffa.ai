import { describe, expect, it } from "vitest";
import {
  PIPELINE_STAGE_LABELS,
  getPipelineStageViews,
  getResultCardContent,
  getUploadOutcome,
  isTerminalProcessingStatus,
} from "../../../src/routes/documents/uploadPipeline";

describe("PIPELINE_STAGE_LABELS", () => {
  it("has exactly 6 stages (AC-2, quoted from day1-demo.html's pipeLabels)", () => {
    expect(PIPELINE_STAGE_LABELS).toHaveLength(6);
    expect(PIPELINE_STAGE_LABELS).toEqual([
      "Uploaded to object storage",
      "Classifying document type",
      "Text extraction / OCR",
      "Section + table detection",
      "Structured AI extraction",
      "Schema validation & entity resolution",
    ]);
  });
});

describe("getPipelineStageViews", () => {
  it("marks every stage pending before the first one starts", () => {
    const views = getPipelineStageViews(0);

    expect(views[0].state).toBe("current");
    expect(views.slice(1).every((view) => view.state === "pending")).toBe(true);
  });

  it("marks stages before the current index done, the current index current, and the rest pending", () => {
    const views = getPipelineStageViews(2);

    expect(views.map((view) => view.state)).toEqual(["done", "done", "current", "pending", "pending", "pending"]);
    expect(views.map((view) => view.label)).toEqual([...PIPELINE_STAGE_LABELS]);
  });

  it("marks every stage done once the ticker has passed the last one", () => {
    const views = getPipelineStageViews(6);

    expect(views.every((view) => view.state === "done")).toBe(true);
  });
});

describe("isTerminalProcessingStatus", () => {
  it("is true for NeedsReview/Completed/Failed", () => {
    expect(isTerminalProcessingStatus("NeedsReview")).toBe(true);
    expect(isTerminalProcessingStatus("Completed")).toBe(true);
    expect(isTerminalProcessingStatus("Failed")).toBe(true);
  });

  it("is false for Uploaded/Processing (pre-terminal contract values)", () => {
    expect(isTerminalProcessingStatus("Uploaded")).toBe(false);
    expect(isTerminalProcessingStatus("Processing")).toBe(false);
  });
});

describe("getUploadOutcome", () => {
  it("maps each terminal processingStatus to its AC-3 outcome", () => {
    expect(getUploadOutcome("Completed")).toBe("completed");
    expect(getUploadOutcome("NeedsReview")).toBe("needs_review");
    expect(getUploadOutcome("Failed")).toBe("failed");
  });
});

describe("getResultCardContent", () => {
  it("completed: neutral tag, names the file, no fabricated field/confidence figures", () => {
    const content = getResultCardContent("completed", "MSA_Acme.pdf");

    expect(content.tag).toEqual({ variant: "neutral", label: "Completed" });
    expect(content.ctaLabel).toBe("Open Contract 360");
    expect(content.message).toContain("MSA_Acme.pdf");
  });

  it("needs_review: outline tag, names the file", () => {
    const content = getResultCardContent("needs_review", "OrderForm.docx");

    expect(content.tag).toEqual({ variant: "outline", label: "Needs review" });
    expect(content.ctaLabel).toBe("Review extraction");
    expect(content.message).toContain("OrderForm.docx");
  });

  it("failed: accent tag, names the file, suggests (not asserts) a cause", () => {
    const content = getResultCardContent("failed", "Contract.pdf");

    expect(content.tag).toEqual({ variant: "accent", label: "Failed" });
    expect(content.ctaLabel).toBe("Retry upload");
    expect(content.message).toContain("Contract.pdf");
    // Suggests checking for a known failure pattern, but must not assert it
    // as a confirmed cause -- the API returns no failure-reason field.
    expect(content.message).toMatch(/password-protected/i);
    expect(content.message).not.toMatch(/is password-protected/i);
  });
});
