import { DEFAULT_TABLE_PAGE_SIZE, pageCount, pagerRangeLabel } from "./pager";
import "./tablePager.css";
import { useI18n } from "../../i18n/I18nProvider";

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
  label,
}: TablePagerProps) {
  const { t } = useI18n();
  if (totalItems <= pageSize) return null;

  const pages = pageCount(totalItems, pageSize);

  return (
    <nav className="table-pager" aria-label={label ?? t("Table pages")}>
      <p className="micro-meta table-pager-range">{pagerRangeLabel(page, totalItems, pageSize, t)}</p>
      <div className="table-pager-controls">
        <button type="button" className="btn btn-ghost" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          {t("Previous")}
        </button>
        {Array.from({ length: pages }, (_, index) => {
          const number = index + 1;
          const current = number === page;
          return (
            <button
              key={number}
              type="button"
              className={`btn btn-ghost${current ? " is-current" : ""}`}
              aria-label={t("Page {n}", { n: number })}
              aria-current={current ? "page" : undefined}
              onClick={() => onPageChange(number)}
            >
              {number}
            </button>
          );
        })}
        <button type="button" className="btn btn-ghost" disabled={page >= pages} onClick={() => onPageChange(page + 1)}>
          {t("Next⟦page⟧")}
        </button>
      </div>
    </nav>
  );
}
