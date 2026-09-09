import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import AttentionStrip from "../../../src/routes/contracts/AttentionStrip";
import { computeAttentionBucketCounts } from "../../../src/routes/contracts/portfolioAttention";
import type { AttentionRow } from "../../../src/routes/contracts/portfolioAttention";

// Task-01's own named "Tests required" row: "unit | ... + attention strip click" (AC-2).

function rowWith(overrides: Partial<AttentionRow>): AttentionRow {
  return {
    item: {
      contractId: "c-1",
      supplierId: null,
      // Task E13/F03/US01/T02: supplierName is required now (null when unresolved).
      supplierName: null,
      type: "Msa",
      annualSpend: null,
      startDate: null,
      endDate: null,
      renewalDate: null,
      cancellationDeadline: null,
      autoRenewal: false,
      status: "active",
      risk: null,
    },
    cancelDays: null,
    severity: 0,
    issue: "No action needed",
    isDeadlineSoon: false,
    isNeedsReview: false,
    isFailedOrProcessing: false,
    isHighRisk: false,
    ...overrides,
  };
}

/** Finds the one `.attention-cell` button whose visible label text matches `label` exactly. */
function findCell(label: string): HTMLElement {
  const cells = screen.getAllByRole("button");
  const match = cells.find((cell) => within(cell).queryByText(label) !== null);
  if (!match) throw new Error(`No attention cell found with label "${label}"`);
  return match;
}

describe("AttentionStrip", () => {
  it("renders the four AC-2 buckets with their exact labels and counts", () => {
    const rows = [rowWith({ isDeadlineSoon: true }), rowWith({ isHighRisk: true }), rowWith({ isHighRisk: true })];
    render(<AttentionStrip buckets={computeAttentionBucketCounts(rows)} activeKey={null} onToggle={vi.fn()} />);

    expect(within(findCell("Deadlines < 45 d")).getByText("1")).toBeInTheDocument();
    expect(within(findCell("High risk")).getByText("2")).toBeInTheDocument();
    expect(within(findCell("Need review")).getByText("0")).toBeInTheDocument();
    expect(within(findCell("Failed / processing")).getByText("0")).toBeInTheDocument();
  });

  it("click = filter: clicking a cell calls onToggle with that bucket's key", () => {
    const onToggle = vi.fn();
    render(<AttentionStrip buckets={computeAttentionBucketCounts([])} activeKey={null} onToggle={onToggle} />);

    fireEvent.click(findCell("High risk"));

    expect(onToggle).toHaveBeenCalledWith("risk");
    expect(onToggle).toHaveBeenCalledTimes(1);
  });

  it("marks the currently-active bucket with aria-pressed=true, and only that one", () => {
    render(<AttentionStrip buckets={computeAttentionBucketCounts([])} activeKey="review" onToggle={vi.fn()} />);

    expect(findCell("Need review")).toHaveAttribute("aria-pressed", "true");
    expect(findCell("High risk")).toHaveAttribute("aria-pressed", "false");
  });

  it("does not rely on colour alone -- a zero-count bucket still keeps its own visible text label", () => {
    render(<AttentionStrip buckets={computeAttentionBucketCounts([])} activeKey={null} onToggle={vi.fn()} />);

    const cell = findCell("Failed / processing");
    expect(cell).not.toHaveClass("is-urgent");
    expect(cell).toHaveTextContent("Failed / processing");
  });

  // Task E11/F05/US01/T01 (gap G-PORT): day1-demo.html's own attDef.map renders `meta` as a visible
  // third line under the label ("...margin-top:4px">{{ a.meta }}"), not a hover-only tooltip -- this
  // component used to drop it into a `title` attribute instead.
  it("renders each bucket's meta description as visible text, not only a hover tooltip", () => {
    render(<AttentionStrip buckets={computeAttentionBucketCounts([])} activeKey={null} onToggle={vi.fn()} />);

    const cell = findCell("Need review");
    expect(within(cell).getByText("Contract status needs review")).toBeInTheDocument();
    expect(cell).not.toHaveAttribute("title");
  });

  // day1-demo.html's own attDef.map: `bar`/`fg` turn accent/accent-700 for deadline/review/failed
  // once they have a match, but "High risk" alone stays ink-coloured (it already has its own
  // row-level tag/colour) -- so it gets a distinct `is-risk-flagged` class, never `is-urgent`.
  it("flags a matching 'High risk' bucket without the accent is-urgent treatment the other three buckets get", () => {
    const rows = [rowWith({ isHighRisk: true }), rowWith({ isDeadlineSoon: true })];
    render(<AttentionStrip buckets={computeAttentionBucketCounts(rows)} activeKey={null} onToggle={vi.fn()} />);

    const riskCell = findCell("High risk");
    expect(riskCell).toHaveClass("is-risk-flagged");
    expect(riskCell).not.toHaveClass("is-urgent");

    const deadlineCell = findCell("Deadlines < 45 d");
    expect(deadlineCell).toHaveClass("is-urgent");
    expect(deadlineCell).not.toHaveClass("is-risk-flagged");
  });

  it("marks the active bucket with the is-active class that carries its selected-state background", () => {
    render(<AttentionStrip buckets={computeAttentionBucketCounts([])} activeKey="failed" onToggle={vi.fn()} />);

    expect(findCell("Failed / processing")).toHaveClass("is-active");
    expect(findCell("High risk")).not.toHaveClass("is-active");
  });
});
