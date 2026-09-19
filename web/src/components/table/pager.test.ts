import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { clampPage, DEFAULT_TABLE_PAGE_SIZE, pageCount, pagerRangeLabel, slicePage, usePagedRows } from "./pager";

describe("pager", () => {
  it("defaults to 10 rows per page", () => {
    expect(DEFAULT_TABLE_PAGE_SIZE).toBe(10);
  });

  it("counts pages from a full last page and a remainder", () => {
    expect(pageCount(0)).toBe(1);
    expect(pageCount(10)).toBe(1);
    expect(pageCount(11)).toBe(2);
    expect(pageCount(20)).toBe(2);
    expect(pageCount(21)).toBe(3);
  });

  it("clamps a requested page onto the real range", () => {
    expect(clampPage(0, 25)).toBe(1);
    expect(clampPage(-2, 25)).toBe(1);
    expect(clampPage(2, 25)).toBe(2);
    expect(clampPage(9, 25)).toBe(3);
    expect(clampPage(2, 0)).toBe(1);
  });

  it("slices the requested page without mutating the source", () => {
    const items = Array.from({ length: 12 }, (_, index) => index + 1);
    expect(slicePage(items, 1)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
    expect(slicePage(items, 2)).toEqual([11, 12]);
    expect(slicePage(items, 9)).toEqual([11, 12]);
    expect(items).toHaveLength(12);
  });

  it("labels the visible range in plain language", () => {
    expect(pagerRangeLabel(1, 0)).toBe("0 items");
    expect(pagerRangeLabel(1, 4)).toBe("1–4 of 4");
    expect(pagerRangeLabel(1, 12)).toBe("1–10 of 12");
    expect(pagerRangeLabel(2, 12)).toBe("11–12 of 12");
  });

  it("usePagedRows paints the first ten items and advances", () => {
    const items = Array.from({ length: 12 }, (_, index) => index);
    const { result } = renderHook(() => usePagedRows(items));

    expect(result.current.pageItems).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
    act(() => result.current.setPage(2));
    expect(result.current.pageItems).toEqual([10, 11]);
  });

  it("usePagedRows returns to page 1 when resetKey changes", () => {
    const items = Array.from({ length: 12 }, (_, index) => index);
    const { result, rerender } = renderHook(({ key }) => usePagedRows(items, key), { initialProps: { key: "all" } });

    act(() => result.current.setPage(2));
    expect(result.current.page).toBe(2);
    rerender({ key: "attention" });
    expect(result.current.page).toBe(1);
  });
});
