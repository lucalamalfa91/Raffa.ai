import { useMemo, useState } from "react";

/** Default visible rows on every main list table so the page stays short. */
export const DEFAULT_TABLE_PAGE_SIZE = 10;

export function pageCount(totalItems: number, pageSize: number = DEFAULT_TABLE_PAGE_SIZE): number {
  if (totalItems <= 0) return 1;
  return Math.ceil(totalItems / pageSize);
}

export function clampPage(page: number, totalItems: number, pageSize: number = DEFAULT_TABLE_PAGE_SIZE): number {
  const pages = pageCount(totalItems, pageSize);
  if (!Number.isFinite(page) || page < 1) return 1;
  return Math.min(page, pages);
}

export function slicePage<T>(items: readonly T[], page: number, pageSize: number = DEFAULT_TABLE_PAGE_SIZE): readonly T[] {
  const safe = clampPage(page, items.length, pageSize);
  const start = (safe - 1) * pageSize;
  return items.slice(start, start + pageSize);
}

export function pagerRangeLabel(page: number, totalItems: number, pageSize: number = DEFAULT_TABLE_PAGE_SIZE): string {
  if (totalItems <= 0) return "0 items";
  const safe = clampPage(page, totalItems, pageSize);
  const start = (safe - 1) * pageSize + 1;
  const end = Math.min(safe * pageSize, totalItems);
  return `${start}–${end} of ${totalItems}`;
}

/**
 * Client-side page of an already-filtered/sorted list. Fetch ceilings stay where they are
 * (`pageSize: 100` on Documents / Portfolio); this only limits what the table paints.
 * `resetKey` returns to page 1 (filter chip, column filters) so a later bucket never opens mid-list.
 */
export function usePagedRows<T>(
  items: readonly T[],
  resetKey: unknown = 0,
  pageSize: number = DEFAULT_TABLE_PAGE_SIZE,
): {
  page: number;
  setPage: (page: number) => void;
  pageItems: readonly T[];
  totalItems: number;
  pageSize: number;
} {
  const [session, setSession] = useState({ resetKey, page: 1 });
  const requestedPage = session.resetKey === resetKey ? session.page : 1;
  const page = clampPage(requestedPage, items.length, pageSize);

  const setPage = (next: number) => {
    setSession({ resetKey, page: next });
  };

  const pageItems = useMemo(() => slicePage(items, page, pageSize), [items, page, pageSize]);

  return { page, setPage, pageItems, totalItems: items.length, pageSize };
}
