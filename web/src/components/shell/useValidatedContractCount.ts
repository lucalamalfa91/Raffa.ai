import { useEffect, useState } from "react";
import type { ApiClient } from "../../api/client";
import { loadCurrentWorkspace } from "../../routes/signin/workspaceStore";

/**
 * `kbReady` / the secondary rail tier's count badge (ADR-024 V2 IA amendment; task E13/F09/US01/T01,
 * gap G-IA-V2).
 *
 * Task E14/F03/US02/T01 (wave w14 "workspace is real"; ADR-012/ADR-026 w14 footers): this hook used
 * to approximate "validated" with `GET /api/contracts` (`apiClient.getPortfolio`) counted
 * client-side against `isValidatedContractStatus` -- a heuristic its own comment admitted was capped
 * at a page size and therefore undercounted any tenant past that ceiling. `GET /api/workspaces`
 * (ADR-026 §D1) now carries the real definition as a field, `contractCount` -- "a contract is
 * validated iff at least one linked document is `Completed`", computed server-side with a real
 * `CountAsync`, not materialised and measured client-side -- so this hook reads *that* instead. The
 * client-side predicate stops being a count definition here and is not duplicated: see
 * `routes/contracts/contractStatus.ts`'s own updated header comment for why it survives only as a
 * row-level display/filter helper elsewhere, and this hook's own promise to `AppShell.tsx`
 * (`../../routes/ask/index.tsx`, `RailNav.tsx`'s secondary badges) that a screen never has to
 * re-derive this number a different way -- **the two must never both produce a number.**
 *
 * `apiClient.listWorkspaces()` is a second, independent request from the one `App.tsx`'s own
 * resolution already made -- this hook has no access to that earlier result, only to the same
 * `apiClient` and the same revalidating endpoint every other caller of it uses -- but it is exactly
 * as cheap and exactly as authoritative: **revalidation is the mechanism**, not a compromise, the
 * same principle `workspaceStore.ts`'s own header comment states for the session hint this hook
 * still reads to know *which* row is this caller's current one. That hint is never trusted for its
 * own sake here either -- it only selects a row out of a response this hook fetched itself.
 */
export interface ValidatedContractCountState {
  /** The server's own count for the current workspace (`WorkspaceSummaryBody.contractCount`).
   * `0` before the first fetch resolves, on a failed fetch, or while no workspace is current --
   * "no evidence, no claim": a badge/greyed state never assumes readiness it has not confirmed. */
  count: number;
  /** `count > 0` -- ADR-024's `kbReady` ("From your contracts" tier lights up after the first
   * validated contract). */
  kbReady: boolean;
}

const INITIAL_STATE: ValidatedContractCountState = { count: 0, kbReady: false };

/**
 * Fetched once by the shell (`AppShell.tsx`), then passed down to `RailNav`/`GlobalAskBar` as plain
 * props -- this hook does not re-poll on navigation (matching this task's own text, "fetched once by
 * the shell"); a contract that becomes validated mid-session only updates the rail on the next full
 * shell mount, the same interim every other session-scoped read in this app already accepts
 * (`documentStore.ts`, `workspaceStore.ts`). `routes/ask/index.tsx` calls this same hook a second
 * time, independently -- both call sites end up reading the identical server field, so the picker
 * and the rail (and Ask) cannot disagree about what "validated" means (N8's "rail matches").
 */
export function useValidatedContractCount(apiClient: ApiClient): ValidatedContractCountState {
  const [state, setState] = useState<ValidatedContractCountState>(INITIAL_STATE);
  const workspace = loadCurrentWorkspace();

  useEffect(() => {
    if (!workspace) return;
    let cancelled = false;

    void apiClient.listWorkspaces().then((result) => {
      if (cancelled) return;
      if (!result.ok || !result.workspaces) {
        setState(INITIAL_STATE);
        return;
      }
      const row = result.workspaces.find((candidate) => candidate.id === workspace.id);
      const count = row?.contractCount ?? 0;
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
