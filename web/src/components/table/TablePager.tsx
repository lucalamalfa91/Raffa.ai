import { DEFAULT_TABLE_PAGE_SIZE, pageCount, pagerRangeLabel } from "./pager";
import "./tablePager.css";

export interface TablePagerProps {
  page: number;
  totalItems: number;
  pageSize?: number;
  onPageChange: (page: number) => void;
  /** Accessible name; defaults to "Table pages". */
  label?: string;
}

/**
 * Previous / page numbers / Next under a main list table. Hidden when everything fits on
 * one page (the default 10-row ceiling). Same `.btn` / `.btn-ghost` / `.micro-meta` chrome
 * the rest of the app already uses.
 */
export default function TablePager({
  page,
  totalItems,
  pageSize = DEFAULT_TABLE_PAGE_SIZE,
  onPageChange,
  label = "Table pages",
}: TablePagerProps) {
  if (totalItems <= pageSize) return null;

  const pages = pageCount(totalItems, pageSize);

  return (
    <nav className="table-pager" aria-label={label}>
      <p className="micro-meta table-pager-range">{pagerRangeLabel(page, totalItems, pageSize)}</p>
      <div className="table-pager-controls">
        <button type="button" className="btn btn-ghost" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          Previous
        </button>
        {Array.from({ length: pages }, (_, index) => {
          const number = index + 1;
          const current = number === page;
          return (
            <button
              key={number}
              type="button"
              className={`btn btn-ghost${current ? " is-current" : ""}`}
              aria-label={`Page ${number}`}
              aria-current={current ? "page" : undefined}
              onClick={() => onPageChange(number)}
            >
              {number}
            </button>
          );
        })}
        <button type="button" className="btn btn-ghost" disabled={page >= pages} onClick={() => onPageChange(page + 1)}>
          Next
        </button>
      </div>
    </nav>
  );
}
