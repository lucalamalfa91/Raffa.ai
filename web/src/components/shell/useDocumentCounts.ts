import { useEffect, useState } from "react";
import type { ApiClient, DocumentListPageBody } from "../../api/client";
import { loadCurrentWorkspace } from "../../routes/signin/workspaceStore";

/** ADR-027 §D7/§C5/§C9's tenant-wide `counts` object, as `GET /api/documents` returns it. */
export type DocumentCountsBody = DocumentListPageBody["counts"];

/**
 * The rail's Documents badge and the shell's shared "what does the tenant hold" answer (task
 * E16/F03/US01/T01, wave w15; ADR-012 w15 §6, NW-10). Mirrors `useValidatedContractCount.ts`'s
 * shape exactly: fetched once by `AppShell.tsx`, passed down as props and through the router
 * `<Outlet context>`, never re-polled by the shell itself. Reads the *server's* `counts` off a
 * one-row `listDocuments({ pageSize: 1 })` -- the rail costs one row, not a hundred -- replacing
 * `routes/documents/documentStore.ts`, a `sessionStorage` tracker nothing had written since task
 * E13/F09/US01/T03 (so the badge was not wrong, it was always absent).
 *
 * `null` before the first fetch resolves, on a failed fetch, or while no workspace is current: an
 * honest absence the badge renders as *no badge* (`navItems.ts#getDocumentsBadge`'s own "never a
 * fabricated `0 docs`" rule), never a number the shell has not confirmed.
 */
export function useDocumentCounts(apiClient: ApiClient): DocumentCountsBody | null {
  const [counts, setCounts] = useState<DocumentCountsBody | null>(null);
  const workspace = loadCurrentWorkspace();

  useEffect(() => {
    if (!workspace) return;
    let cancelled = false;

    void apiClient.listDocuments(workspace.id, { pageSize: 1 }).then((result) => {
      if (cancelled) return;
      setCounts(result.ok && result.page ? result.page.counts : null);
    });

    return () => {
      cancelled = true;
    };
    // workspace?.id (a primitive), not workspace itself -- loadCurrentWorkspace() returns a fresh
    // object every call, the same convention useValidatedContractCount.ts follows.
  }, [apiClient, workspace?.id]);

  return counts;
}
