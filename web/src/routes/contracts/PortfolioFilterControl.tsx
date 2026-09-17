import { useEffect, useState } from "react";

export interface PortfolioFilterControlProps {
  /**
   * The category filter currently in force -- the route's own `?category=` value
   * (`portfolioViewModel.ts#readCategoryFilter`), `""` when absent. This component holds no store
   * of its own: it only proposes a next value via `onApply`/`onClear`, and mirrors this prop back
   * into its draft input whenever it changes from elsewhere (Clear, browser back/forward).
   */
  category: string;
  /** Commits the typed draft as the new `?category=` value (AC-1). */
  onApply: (category: string) => void;
  /** Removes `?category=` entirely -- AC-3, re-loads the full portfolio. */
  onClear: () => void;
}

/**
 * Portfolio's category filter (task E24/F01/US02/T01, story us-02-portfolio-category-web; closes
 * NW-23). A free-text field, not a dropdown: the backend join (`ISupplierCategoryLookup`, task
 * E24/F01/US01/T01) is the only place a `Supplier.Category` value is ever resolved, and no endpoint
 * lists the distinct set for a tenant -- this task's own scope excludes adding one ("do not touch
 * the backend category join"). A hard-coded option list would either invent categories this
 * tenant's suppliers do not carry or silently omit real ones, which is exactly the "never a
 * hard-coded enum" rule the council decision carries into this story. The value the operator types
 * is meaningful only insofar as it matches a real `Supplier.Category` the host resolves; an
 * unmatched value narrows to an empty list rather than a fabricated one (backend AC-3).
 *
 * The draft is local and commits only on submit (Enter, or the Apply button) -- typing never
 * re-fetches on every keystroke. The committed value lives only in the URL
 * (`portfolioViewModel.ts#withCategoryFilter`, wired by `index.tsx`); this component is never the
 * source of truth for it (ADR-012 "a client store never stands in for a missing GET").
 */
export default function PortfolioFilterControl({ category, onApply, onClear }: PortfolioFilterControlProps) {
  const [draft, setDraft] = useState(category);

  // Resync the draft whenever the committed value changes from elsewhere -- Clear (below), or a
  // browser back/forward navigation landing on a different `?category=`.
  useEffect(() => {
    setDraft(category);
  }, [category]);

  return (
    <form
      className="portfolio-filter-control"
      role="search"
      aria-label="Filter portfolio by supplier category"
      onSubmit={(event) => {
        event.preventDefault();
        onApply(draft);
      }}
    >
      <div className="field" style={{ marginBottom: 0, minWidth: "200px" }}>
        <label htmlFor="portfolio-filter-category">Category</label>
        <input
          id="portfolio-filter-category"
          className="input"
          type="text"
          placeholder="Supplier category"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
        />
      </div>
      <button type="submit" className="btn btn-secondary">
        Apply
      </button>
      {category.length > 0 && (
        <button
          type="button"
          className="btn-ghost"
          onClick={() => {
            setDraft("");
            onClear();
          }}
        >
          Clear filter
        </button>
      )}
    </form>
  );
}
