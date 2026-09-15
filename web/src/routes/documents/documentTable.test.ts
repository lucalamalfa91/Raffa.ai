import { describe, expect, it } from "vitest";
import {
  getProcessingChip,
  PROCESSING_CHIP_LABEL,
  getStagePercent,
  DOCUMENT_PROCESSING_STAGES,
  type ProcessingChip,
} from "./documentTable";

/**
 * Unit tests for `documentTable.ts` — focused on the new `getProcessingChip` / `PROCESSING_CHIP_LABEL`
 * additions (plan instant-upload-open: quiet chips replace the 90 s "Extracting facts…" progress bar).
 * The existing `getStagePercent` contract is also pinned here so any future refactor of
 * `DOCUMENT_PROCESSING_STAGES` cannot silently break the bar (still used by `getStagePercent`).
 */

describe("getProcessingChip", () => {
  it("null stage (queued, no Worker yet) → identifying", () => {
    expect(getProcessingChip(null)).toBe<ProcessingChip>("identifying");
  });

  it("'Uploading' → identifying", () => {
    expect(getProcessingChip("Uploading")).toBe<ProcessingChip>("identifying");
  });

  it("'Classifying' → identifying", () => {
    expect(getProcessingChip("Classifying")).toBe<ProcessingChip>("identifying");
  });

  it("'OCR / text' → identifying", () => {
    expect(getProcessingChip("OCR / text")).toBe<ProcessingChip>("identifying");
  });

  it("'Sections & tables' → identifying", () => {
    expect(getProcessingChip("Sections & tables")).toBe<ProcessingChip>("identifying");
  });

  it("'Extracting facts' → enriching (the stage that used to pin the bar for ~90 s)", () => {
    expect(getProcessingChip("Extracting facts")).toBe<ProcessingChip>("enriching");
  });

  it("'Validating schema' → enriching", () => {
    expect(getProcessingChip("Validating schema")).toBe<ProcessingChip>("enriching");
  });

  it("unrecognised stage name → identifying (safe default)", () => {
    expect(getProcessingChip("Some future stage")).toBe<ProcessingChip>("identifying");
  });
});

describe("PROCESSING_CHIP_LABEL", () => {
  it("identifying label ends with an ellipsis", () => {
    expect(PROCESSING_CHIP_LABEL.identifying).toMatch(/…$/);
  });

  it("enriching label ends with an ellipsis", () => {
    expect(PROCESSING_CHIP_LABEL.enriching).toMatch(/…$/);
  });

  it("labels are distinct", () => {
    expect(PROCESSING_CHIP_LABEL.identifying).not.toBe(PROCESSING_CHIP_LABEL.enriching);
  });
});

describe("getStagePercent (pinning existing contract)", () => {
  it("null → 0", () => {
    expect(getStagePercent(null)).toBe(0);
  });

  it("first stage → smallest non-zero percent", () => {
    const first = DOCUMENT_PROCESSING_STAGES[0];
    const expected = Math.round((1 / DOCUMENT_PROCESSING_STAGES.length) * 100);
    expect(getStagePercent(first)).toBe(expected);
  });

  it("last stage → 100", () => {
    const last = DOCUMENT_PROCESSING_STAGES[DOCUMENT_PROCESSING_STAGES.length - 1];
    expect(getStagePercent(last)).toBe(100);
  });

  it("unrecognised stage → 0 (honest 'just started')", () => {
    expect(getStagePercent("Unknown stage")).toBe(0);
  });
});
