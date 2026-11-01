import { useCallback } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import type { CaseAssignee, TodayItem, TodayViewer } from "../../api/renewalCases";

/**
 * Opening work from Oggi or Rinnovi (README "Interactions"): a case item lands on its current step; a
 * notice deadline without an owner opens the case -- the CPO assigns it ("Avvia e assegna a …") or takes
 * it ("Me ne occupo io"); a quote opens its check; a document opens its review. Opening is idempotent on
 * the server: it never creates a second case.
 */
export function useCaseNavigation(apiClient: ApiClient, tenantId: string | undefined) {
  const navigate = useNavigate();

  const openContract = useCallback(
    async (contractId: string, ownerUserId: string | null | undefined) => {
      if (!tenantId || !apiClient.openRenewalCase) return;
      const r = await apiClient.openRenewalCase(tenantId, { contractId, ownerUserId: ownerUserId ?? null });
      const env = r.ok ? r.body : r.current;
      if (env) navigate(`/renewals/${env.caseId}`);
    },
    [apiClient, tenantId, navigate],
  );

  /** The item's main action ("Fai ora", "Avvia e assegna a Marta", "Apri la verifica"). */
  const openItem = useCallback(
    async (item: TodayItem, viewer: TodayViewer, assignee: CaseAssignee | null) => {
      if (item.kind === "quote" && item.quoteId) {
        navigate(`/quotes/${item.quoteId}`);
      } else if (item.kind === "review" && item.documentId) {
        navigate(`/contracts/review/${item.documentId}`);
      } else if (item.kind === "unassigned" && item.contractId) {
        await openContract(item.contractId, viewer.isApprover ? (assignee?.userId ?? viewer.userId) : viewer.userId);
      } else if (item.caseId) {
        navigate(`/renewals/${item.caseId}`);
      }
    },
    [navigate, openContract],
  );

  return { openItem, openContract };
}
