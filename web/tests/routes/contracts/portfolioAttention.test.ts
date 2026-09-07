import { describe, expect, it } from "vitest";
import type { PortfolioListItem } from "../../../src/api/client";
import {
  ATTENTION_BUCKETS,
  compareBySeverityThenDeadline,
  computeAttentionBucketCounts,
  computeAttentionRow,
  daysUntil,
} from "../../../src/routes/contracts/portfolioAttention";

// Task-01's own named "Tests required" row: "unit | filter + attention strip click, AC-2/AC-3".

function item(overrides: Partial<PortfolioListItem> = {}): PortfolioListItem {
  return {
    contractId: "11111111-1111-1111-1111-111111111111",
    supplierId: "22222222-2222-2222-2222-222222222222",
    type: "Msa",
    annualSpend: 100_000,
    startDate: "2025-01-01",
    endDate: "2026-01-01",
    renewalDate: null,
    cancellationDeadline: null,
    autoRenewal: false,
    status: "active",
    risk: null,
    ...overrides,
  };
}

const NOW = new Date("2026-01-01T00:00:00Z");

describe("daysUntil", () => {
  it("returns null for a null date", () => {
    expect(daysUntil(null, NOW)).toBeNull();
  });

  it("counts whole days forward", () => {
    expect(daysUntil("2026-01-15", NOW)).toBe(14);
  });

  it("returns 0 for today", () => {
    expect(daysUntil("2026-01-01", NOW)).toBe(0);
  });

  it("returns a negative count for a date already in the past", () => {
    expect(daysUntil("2025-12-20", NOW)).toBe(-12);
  });

  it("is not shifted by the host's local timezone (UTC on both sides)", () => {
    // A `now` with a non-midnight-UTC time-of-day must not change the whole-day count.
    expect(daysUntil("2026-01-15", new Date("2026-01-01T23:00:00Z"))).toBe(14);
  });
});

describe("computeAttentionRow (AC-2/AC-3 severity + issue text)", () => {
  it("defaults to severity 0 with no issue when nothing is flagged", () => {
    const row = computeAttentionRow(item(), NOW);
    expect(row.severity).toBe(0);
    expect(row.issue).toBe("No action needed");
    expect(row.isDeadlineSoon).toBe(false);
    expect(row.isNeedsReview).toBe(false);
    expect(row.isFailedOrProcessing).toBe(false);
    expect(row.isHighRisk).toBe(false);
  });

  it("severity 3: an exact 'Failed' status, regardless of anything else", () => {
    const row = computeAttentionRow(item({ status: "Failed", risk: "High" }), NOW);
    expect(row.severity).toBe(3);
    expect(row.issue).toBe("Processing failed — re-upload");
    expect(row.isFailedOrProcessing).toBe(true);
  });

  it("status match is case-insensitive and trims whitespace", () => {
    const row = computeAttentionRow(item({ status: "  FAILED  " }), NOW);
    expect(row.severity).toBe(3);
    expect(row.isFailedOrProcessing).toBe(true);
  });

  it("severity 3: needs review AND a deadline within 45 days combines into one message", () => {
    const row = computeAttentionRow(item({ status: "Needs review", cancellationDeadline: "2026-01-31" }), NOW);
    expect(row.severity).toBe(3);
    expect(row.cancelDays).toBe(30);
    expect(row.issue).toBe("Needs review · notice due in 30 d");
    expect(row.isNeedsReview).toBe(true);
    expect(row.isDeadlineSoon).toBe(true);
  });

  it("severity 3: a deadline within 45 days alone", () => {
    const row = computeAttentionRow(item({ cancellationDeadline: "2026-01-31" }), NOW);
    expect(row.severity).toBe(3);
    expect(row.issue).toBe("Cancellation notice due in 30 d");
  });

  it("a deadline of exactly 45 days counts as soon (<=, not <)", () => {
    const row = computeAttentionRow(item({ cancellationDeadline: "2026-02-15" }), NOW);
    expect(row.cancelDays).toBe(45);
    expect(row.isDeadlineSoon).toBe(true);
    expect(row.severity).toBe(3);
  });

  it("a deadline of 46 days does not count as soon", () => {
    const row = computeAttentionRow(item({ cancellationDeadline: "2026-02-16" }), NOW);
    expect(row.isDeadlineSoon).toBe(false);
    expect(row.severity).toBe(0);
  });

  it("severity 2: needs review with no imminent deadline", () => {
    const row = computeAttentionRow(item({ status: "needs_review" }), NOW);
    expect(row.severity).toBe(2);
    expect(row.issue).toBe("Needs review");
  });

  it("'review' anywhere in the free-text status counts (not just an exact match)", () => {
    const row = computeAttentionRow(item({ status: "Needs Review" }), NOW);
    expect(row.isNeedsReview).toBe(true);
  });

  it("severity 1: the bootstrap 'processing' status alone", () => {
    const row = computeAttentionRow(item({ status: "processing" }), NOW);
    expect(row.severity).toBe(1);
    expect(row.issue).toBe("Processing…");
    expect(row.isFailedOrProcessing).toBe(true);
  });

  it("severity 2: High risk alone", () => {
    const row = computeAttentionRow(item({ risk: "High" }), NOW);
    expect(row.severity).toBe(2);
    expect(row.issue).toBe("High risk");
    expect(row.isHighRisk).toBe(true);
  });

  it("Critical risk folds into the same high-risk bucket as High (ADR-019 has no fourth tier)", () => {
    const row = computeAttentionRow(item({ risk: "Critical" }), NOW);
    expect(row.severity).toBe(2);
    expect(row.issue).toBe("High risk");
    expect(row.isHighRisk).toBe(true);
  });

  it("Medium/Low risk is not high risk", () => {
    expect(computeAttentionRow(item({ risk: "Medium" }), NOW).isHighRisk).toBe(false);
    expect(computeAttentionRow(item({ risk: "Low" }), NOW).isHighRisk).toBe(false);
  });

  it("a failed status takes priority over an also-imminent deadline (first match wins)", () => {
    const row = computeAttentionRow(item({ status: "Failed", cancellationDeadline: "2026-01-05" }), NOW);
    expect(row.issue).toBe("Processing failed — re-upload");
  });
});

describe("compareBySeverityThenDeadline (AC-3 'sorted by severity -> deadline')", () => {
  it("orders higher severity first", () => {
    const low = computeAttentionRow(item({ contractId: "a" }), NOW); // severity 0
    const high = computeAttentionRow(item({ contractId: "b", status: "Failed" }), NOW); // severity 3
    expect([low, high].sort(compareBySeverityThenDeadline)).toEqual([high, low]);
  });

  it("within equal severity, sorts by soonest cancellation deadline first", () => {
    const soon = computeAttentionRow(item({ contractId: "a", cancellationDeadline: "2026-01-10" }), NOW);
    const later = computeAttentionRow(item({ contractId: "b", cancellationDeadline: "2026-01-20" }), NOW);
    expect([later, soon].sort(compareBySeverityThenDeadline)).toEqual([soon, later]);
  });

  it("a row with no deadline sorts after every row that has one, within the same severity", () => {
    const withDeadline = computeAttentionRow(item({ contractId: "a", risk: "High", cancellationDeadline: "2026-06-01" }), NOW);
    const withoutDeadline = computeAttentionRow(item({ contractId: "b", risk: "High" }), NOW);
    expect([withoutDeadline, withDeadline].sort(compareBySeverityThenDeadline)).toEqual([withDeadline, withoutDeadline]);
  });

  it("breaks a full tie deterministically by contract id", () => {
    const a = computeAttentionRow(item({ contractId: "aaaa" }), NOW);
    const b = computeAttentionRow(item({ contractId: "bbbb" }), NOW);
    expect([b, a].sort(compareBySeverityThenDeadline)).toEqual([a, b]);
  });
});

describe("attention-strip buckets (AC-2)", () => {
  it("names the exact four labels from day1-demo.html / the parent story's AC-2", () => {
    expect(ATTENTION_BUCKETS.map((bucket) => bucket.label)).toEqual([
      "Deadlines < 45 d",
      "Need review",
      "Failed / processing",
      "High risk",
    ]);
  });

  it("counts each bucket independently across the whole set of rows", () => {
    const rows = [
      computeAttentionRow(item({ contractId: "a", cancellationDeadline: "2026-01-10" }), NOW), // deadline
      computeAttentionRow(item({ contractId: "b", status: "Needs review" }), NOW), // review
      computeAttentionRow(item({ contractId: "c", status: "Failed" }), NOW), // failed (+ also counts as deadline-eligible? no deadline set)
      computeAttentionRow(item({ contractId: "d", risk: "High" }), NOW), // risk
      computeAttentionRow(item({ contractId: "e" }), NOW), // nothing
    ];

    const counts = computeAttentionBucketCounts(rows);
    expect(counts.find((bucket) => bucket.key === "deadline")?.count).toBe(1);
    expect(counts.find((bucket) => bucket.key === "review")?.count).toBe(1);
    expect(counts.find((bucket) => bucket.key === "failed")?.count).toBe(1);
    expect(counts.find((bucket) => bucket.key === "risk")?.count).toBe(1);
  });

  it("a row can count toward more than one bucket at once (e.g. failed AND an imminent deadline)", () => {
    const rows = [computeAttentionRow(item({ status: "Failed", cancellationDeadline: "2026-01-05" }), NOW)];
    const counts = computeAttentionBucketCounts(rows);
    expect(counts.find((bucket) => bucket.key === "failed")?.count).toBe(1);
    expect(counts.find((bucket) => bucket.key === "deadline")?.count).toBe(1);
  });
});
