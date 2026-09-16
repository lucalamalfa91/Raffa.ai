import { describe, expect, it } from "vitest";
import {
  getConfidenceTag,
  getRiskTag,
  getStatusTag,
  isDeadlineCritical,
} from "../../src/styles/semantics";

// ADR-019 w17 clause 7 — confidence is label-only over the server's three
// decisions. Percentages are floored (clause 8). The two accepted states share
// `.tag-neutral` and differ only in text.

describe("getConfidenceTag", () => {
  it("maps auto_accepted to a neutral 'Accepted automatically · NN%' tag", () => {
    expect(getConfidenceTag(97, "auto_accepted")).toEqual({
      variant: "neutral",
      label: "Accepted automatically · 97%",
    });
    expect(getConfidenceTag(90, "auto_accepted")).toEqual({
      variant: "neutral",
      label: "Accepted automatically · 90%",
    });
  });

  it("maps human_accepted to a neutral 'Accepted by you' tag with no percentage", () => {
    expect(getConfidenceTag(97, "human_accepted")).toEqual({ variant: "neutral", label: "Accepted by you" });
    expect(getConfidenceTag(12, "human_accepted")).toEqual({ variant: "neutral", label: "Accepted by you" });
  });

  it("distinguishes the two accepted states by label, never by variant", () => {
    const automatic = getConfidenceTag(94, "auto_accepted");
    const human = getConfidenceTag(94, "human_accepted");
    expect(automatic.variant).toBe("neutral");
    expect(human.variant).toBe("neutral");
    expect(automatic.label).not.toBe(human.label);
    expect(human.label).not.toMatch(/%/);
  });

  it("maps review_required to an outline 'Review · NN%' tag and floors the percentage", () => {
    expect(getConfidenceTag(89.5, "review_required")).toEqual({ variant: "outline", label: "Review · 89%" });
    expect(getConfidenceTag(71, "review_required")).toEqual({ variant: "outline", label: "Review · 71%" });
  });

  it("keeps a one-argument call compiling: a missing decision is painted as review_required", () => {
    expect(getConfidenceTag(97)).toEqual({ variant: "outline", label: "Review · 97%" });
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
