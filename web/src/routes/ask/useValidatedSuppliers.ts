import { useEffect, useState } from "react";
import type { ApiClient } from "../../api/client";
import { buildPortfolioRows } from "../contracts/portfolioViewModel";

/** `PortfolioPageRequest.MaxPageSize` (backend) -- the same single page the rail and Portfolio read. */
const PORTFOLIO_PAGE_SIZE = 100;

/**
 * The validated contracts' supplier names, soonest notice deadline first and de-duplicated --
 * the prototype's `kbNames` (`Raffa.ai V2.dc.html` logic: `completedCids.map(id=>C[id].supplier)`),
 * which the Ask screen prints in its conversation header ("3 validated contracts · Microsoft, AWS,
 * DocuSign") and uses to name the starter questions ("When must we give notice to {supplier}?").
 *
 * One `GET /api/contracts` read per workspace (the same page `RailNav.tsx` already fetches for its
 * conversation titles), filtered and ordered by `portfolioViewModel.ts#buildPortfolioRows` -- the
 * one shared "validated" predicate, never re-derived here. Empty until the read resolves, or when
 * no validated contract names a supplier (never a fabricated name).
 */
export function useValidatedSuppliers(apiClient: ApiClient, workspaceId: string | undefined): readonly string[] {
  const [names, setNames] = useState<readonly string[]>([]);

  useEffect(() => {
    if (workspaceId === undefined) {
      setNames([]);
      return;
    }
    let cancelled = false;
    void apiClient.getPortfolio(workspaceId, { pageSize: PORTFOLIO_PAGE_SIZE }).then((result) => {
      if (cancelled) return;
      if (!result.ok || !result.portfolio) {
        setNames([]);
        return;
      }
      const seen = new Set<string>();
      const ordered: string[] = [];
      for (const row of buildPortfolioRows(result.portfolio.items)) {
        const name = row.item.supplierName?.trim() ?? "";
        if (name === "" || seen.has(name)) continue;
        seen.add(name);
        ordered.push(name);
      }
      setNames(ordered);
    });
    return () => {
      cancelled = true;
    };
  }, [apiClient, workspaceId]);

  return names;
}
