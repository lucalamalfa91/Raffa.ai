import { describe, expect, it } from "vitest";
import { addDays, addMonths, daysBetween, isAfter, isoToCaseDate } from "../../../../src/routes/renewals/case/dates";

describe("case dates", () => {
  it("adds days across month and year ends", () => {
    expect(addDays("15 Jan 2027", 1)).toBe("16 Jan 2027");
    expect(addDays("31 Dec 2026", 1)).toBe("01 Jan 2027");
    expect(addDays("08 Sep 2026", -3)).toBe("05 Sep 2026");
  });

  it("adds months", () => {
    expect(addMonths("15 Jan 2027", 24)).toBe("15 Jan 2029");
    expect(addMonths("30 Jun 2027", 12)).toBe("30 Jun 2028");
  });

  it("counts whole days, negative once passed, regardless of daylight saving", () => {
    expect(daysBetween("08 Sep 2026", "18 Oct 2026")).toBe(40);
    expect(daysBetween("08 Sep 2026", "02 Sep 2026")).toBe(-6);
    expect(daysBetween("25 Mar 2026", "01 Apr 2026")).toBe(7);
  });

  it("compares dates", () => {
    expect(isAfter("02 Oct 2026", "29 Sep 2026")).toBe(true);
    expect(isAfter("29 Sep 2026", "02 Oct 2026")).toBe(false);
  });

  it("converts the API's ISO dates", () => {
    expect(isoToCaseDate("2026-10-02")).toBe("02 Oct 2026");
    expect(isoToCaseDate("2027-01-15T00:00:00Z")).toBe("15 Jan 2027");
  });

  it("rejects what it cannot read instead of guessing", () => {
    expect(() => addDays("Sep 8", 1)).toThrow(/Unrecognised case date/);
    expect(() => isoToCaseDate("08/09/2026")).toThrow(/Not an ISO date/);
  });
});
