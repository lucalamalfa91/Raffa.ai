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
});
