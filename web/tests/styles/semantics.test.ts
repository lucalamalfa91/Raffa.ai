import { describe, expect, it } from "vitest";
import {
  getConfidenceTag,
  getRiskTag,
  getStatusTag,
  isConfidenceBlocking,
  isDeadlineCritical,
} from "../../src/styles/semantics";

// ADR-019 "Semantic mapping (locked)" / design-system.md "Semantic mapping" --
// this is the single source of truth every screen renders confidence/status/
// risk/deadline from (AC-2, "not colour-only"). Boundary values are asserted
// explicitly because the thresholds carry spec §7.3's confidence bands.

describe("getConfidenceTag", () => {
  it("maps >95% to an accepted neutral tag", () => {
    expect(getConfidenceTag(97)).toEqual({ variant: "neutral", label: "Accepted · 97%" });
    expect(getConfidenceTag(95.1)).toEqual({ variant: "neutral", label: "Accepted · 95%" });
  });

  it("maps the 80-95% band (inclusive of both ends) to a flagged accent tag", () => {
    expect(getConfidenceTag(95)).toEqual({ variant: "accent", label: "Flagged · 95%" });
    expect(getConfidenceTag(88)).toEqual({ variant: "accent", label: "Flagged · 88%" });
    expect(getConfidenceTag(80)).toEqual({ variant: "accent", label: "Flagged · 80%" });
  });

  it("maps <80% to a blocking review outline tag", () => {
    expect(getConfidenceTag(79.9)).toEqual({ variant: "outline", label: "Review · 80%" });
    expect(getConfidenceTag(71)).toEqual({ variant: "outline", label: "Review · 71%" });
  });
});

describe("isConfidenceBlocking", () => {
  it("blocks consequential use strictly under 80%", () => {
    expect(isConfidenceBlocking(79.99)).toBe(true);
    expect(isConfidenceBlocking(80)).toBe(false);
    expect(isConfidenceBlocking(95)).toBe(false);
  });
});

describe("getStatusTag", () => {
  it("maps completed and ready to neutral tags", () => {
    expect(getStatusTag("completed")).toEqual({ variant: "neutral", label: "Completed" });
    expect(getStatusTag("ready")).toEqual({ variant: "neutral", label: "Ready" });
  });

  it("maps needs_review to an outline tag", () => {
    expect(getStatusTag("needs_review")).toEqual({ variant: "outline", label: "Needs review" });
  });

  it("maps failed to an accent tag", () => {
    expect(getStatusTag("failed")).toEqual({ variant: "accent", label: "Failed" });
  });

  // Task E13/F09/US01/T03 (web-documents-v2): a document row is visible (and its status tag
  // rendered) from the moment it is picked, R-DOC-01 AC-1 -- unlike V1, which only ever wrote a
  // table row once a document reached a terminal status.
  it("maps processing to a neutral tag", () => {
    expect(getStatusTag("processing")).toEqual({ variant: "neutral", label: "Processing" });
  });

  // ADR-019 w15 clause 1 (task E16/F03/US01/T01): a refused document is a decision about the file,
  // so it takes the "this row is about a decision" outline treatment with the retired card's own
  // label -- never `failed`'s accent -- and the switch stays exhaustive: no `undefined` fall-through.
  it("maps rejected to the outline 'Not added' tag, never accent and never undefined", () => {
    expect(getStatusTag("rejected")).toEqual({ variant: "outline", label: "Not added" });
  });

  // Task E16/F03/US02/T02 (ADR-020 w15 footer 10): the perceived-instant batch row -- neutral, the
  // same variant as processing, since nothing has gone wrong and nothing needs the user yet.
  it("maps uploaded to a neutral 'Uploaded' tag, distinct from processing", () => {
    expect(getStatusTag("uploaded")).toEqual({ variant: "neutral", label: "Uploaded" });
  });
});

describe("getRiskTag", () => {
  it("maps high risk to an accent tag", () => {
    expect(getRiskTag("high")).toEqual({ variant: "accent", label: "High risk" });
  });

  it("maps medium and low risk to neutral tags with distinct labels", () => {
    expect(getRiskTag("medium")).toEqual({ variant: "neutral", label: "Medium risk" });
    expect(getRiskTag("low")).toEqual({ variant: "neutral", label: "Low risk" });
  });
});

describe("isDeadlineCritical", () => {
  it("is critical at and under 45 days", () => {
    expect(isDeadlineCritical(45)).toBe(true);
    expect(isDeadlineCritical(0)).toBe(true);
  });

  it("is not critical over 45 days", () => {
    expect(isDeadlineCritical(46)).toBe(false);
  });
});
