import { describe, expect, it } from "vitest";
import { daysUntil } from "../../../src/routes/contracts/portfolioAttention";

/** A fixed "now": 2026-09-09T10:00Z. */
const NOW = new Date(Date.UTC(2026, 8, 9, 10, 0, 0));

describe("daysUntil", () => {
  it("returns null for a null date", () => {
    expect(daysUntil(null, NOW)).toBeNull();
  });

  it("counts whole days forward", () => {
    expect(daysUntil("2026-09-19", NOW)).toBe(10);
  });

  it("returns 0 for today", () => {
    expect(daysUntil("2026-09-09", NOW)).toBe(0);
  });

  it("returns a negative count for a date already in the past", () => {
    expect(daysUntil("2026-09-01", NOW)).toBe(-8);
  });

  it("is not shifted by the host's local timezone (UTC on both sides)", () => {
    // 23:30 UTC on the 9th is still the 9th in UTC -- the deadline the next day is one day away
    // regardless of where the test runs.
    const lateEvening = new Date(Date.UTC(2026, 8, 9, 23, 30, 0));
    expect(daysUntil("2026-09-10", lateEvening)).toBe(1);
  });
});
