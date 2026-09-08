import { useEffect, useState } from "react";
import type { ApiClient, PortfolioListItem } from "../../api/client";
import { loadCurrentWorkspace } from "../../routes/signin/workspaceStore";

/**
 * `kbReady` / the secondary rail tier's count badge (ADR-024 V2 IA amendment; task E13/F09/US01/T01,
 * gap G-IA-V2). `app.jsx`: `kbReady=completedCids.length>0` (the prototype's own "at least one
 * document reached `completed`" gate) -- there is no dedicated "validated contracts" endpoint or
 * count field anywhere in this backend yet, so this hook reuses the one real, already-shipped
 * signal that approximates it: `GET /api/contracts` (`apiClient.getPortfolio`, already wired by
 * epic-07/feature-01-portfolio-ui). A contract only exists there once classification/extraction has
 * produced a `Contract` row at all, so "validated" here means "past every transient/blocking status
 * `GET /api/contracts` can report today" -- see `isValidatedContractStatus` below. `Contract.Status`
 * is free text (`../../routes/contracts/portfolioAttention.ts`'s own header comment has the full
 * provenance); this mirrors that file's own case-insensitive normalisation rather than re-deriving a
 * different rule for the same field, but is intentionally re-implemented here (not imported) since
 * `routes/contracts/**` is this task's own "do not touch" boundary (F09/T01's task text) and the two
 * screens are independent, separately-evolving features -- the same "duplicated, not imported"
 * precedent `../../routes/contracts/portfolioTableFormatters.ts`'s own `CONTRACT_TYPE_LABEL` comment
 * already sets for this exact situation.
 */
export interface ValidatedContractCountState {
  /** Number of validated contracts on the tenant's first page (capped at `MAX_PAGE_SIZE` below --
   * see that constant's own comment). `0` before the first fetch resolves or on a failed fetch --
   * "no evidence, no claim": a badge/greyed state never assumes readiness it has not confirmed. */
  count: number;
  /** `count > 0` -- ADR-024's `kbReady` ("From your contracts" tier lights up after the first
   * validated contract). */
  kbReady: boolean;
}

const INITIAL_STATE: ValidatedContractCountState = { count: 0, kbReady: false };

/** Same ceiling `../../routes/contracts/index.tsx`'s own "fetch-once, filter client-side" header
 * comment already uses for its own whole-portfolio read (`PortfolioPageRequest.MaxPageSize`) -- a
 * tenant with more validated contracts than this undercounts here exactly as that screen's own
 * attention-strip counts would, a known, shared limitation, not a new one this hook introduces. */
const MAX_PAGE_SIZE = 100;

function isValidatedContractStatus(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  if (normalized === "" || normalized === "processing" || normalized === "failed") return false;
  // requirements.md R-CMP-03 "Not-validated contracts. If the contract is `needs_review`, Ask says
  // which weak facts block the comparison" -- `needs_review` (in any casing/spacing `GET
  // /api/contracts` might emit) is the one other named not-yet-validated state.
  return !normalized.includes("review");
}

function countValidated(items: readonly PortfolioListItem[]): number {
  return items.filter((item) => isValidatedContractStatus(item.status)).length;
}

/**
 * Fetched once by the shell (`AppShell.tsx`), then passed down to `RailNav`/`GlobalAskBar` as plain
 * props -- this hook does not re-poll on navigation (matching this task's own text, "fetched once by
 * the shell"); a contract that becomes validated mid-session only updates the rail on the next full
 * shell mount, the same interim every other session-scoped read in this app already accepts
 * (`documentStore.ts`, `workspaceStore.ts`).
 */
export function useValidatedContractCount(apiClient: ApiClient): ValidatedContractCountState {
  const [state, setState] = useState<ValidatedContractCountState>(INITIAL_STATE);
  const workspace = loadCurrentWorkspace();

  useEffect(() => {
    if (!workspace) return;
    let cancelled = false;

    void apiClient.getPortfolio(workspace.id, { pageSize: MAX_PAGE_SIZE }).then((result) => {
      if (cancelled) return;
      if (!result.ok || !result.portfolio) {
        setState(INITIAL_STATE);
        return;
      }
      const count = countValidated(result.portfolio.items);
      setState({ count, kbReady: count > 0 });
    });

    return () => {
      cancelled = true;
    };
    // workspace?.id (a primitive), not workspace itself -- loadCurrentWorkspace() returns a fresh
    // object every call, the same convention every other route's own load() callback in this app
    // follows (e.g. ../../routes/savings/index.tsx#loadKpis).
  }, [apiClient, workspace?.id]);

  return state;
}
