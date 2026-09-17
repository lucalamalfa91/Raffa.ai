import { describe, expect, it } from "vitest";
import {
  countReadiness,
  DEFAULT_READINESS_FILTER,
  filterByReadiness,
  getReadinessEmptyCopy,
  getReadinessFilterHint,
} from "../../src/components/readiness";

describe("readinessFilter", () => {
  const items = [
    { id: "ok", ready: true },
    { id: "review", ready: false },
  ];

  it("defaults to already-OK", () => {
    expect(DEFAULT_READINESS_FILTER).toBe("ok");
  });

  it("ok shows only ready rows; review the rest; all keeps every loaded row", () => {
    expect(filterByReadiness(items, "ok", (item) => item.ready).map((item) => item.id)).toEqual(["ok"]);
    expect(filterByReadiness(items, "review", (item) => item.ready).map((item) => item.id)).toEqual(["review"]);
    expect(filterByReadiness(items, "all", (item) => item.ready).map((item) => item.id)).toEqual(["ok", "review"]);
  });

  it("counts ready, still-to-review, and all from the loaded page", () => {
    expect(countReadiness(1, 2)).toEqual({ ok: 1, review: 2, all: 3 });
  });

  it("hints that still-to-review is one click away, never hidden forever", () => {
    expect(getReadinessFilterHint("ok")).toMatch(/still in review are hidden/i);
    expect(getReadinessFilterHint("review")).toMatch(/waiting for review/i);
    expect(getReadinessFilterHint("all")).toMatch(/still to review/i);
    expect(getReadinessEmptyCopy("ok")).toMatch(/Switch to To review/i);
  });
});
