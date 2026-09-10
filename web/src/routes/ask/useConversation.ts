import { useCallback, useEffect, useState } from "react";
import type { ApiClient, ConversationDetailBody } from "../../api/client";
import { buildTurnsFromConversation, type AskTurnView } from "./askViewModel";

/**
 * Resumes one conversation (`GET /api/conversations/{id}`; R-CONV-02 AC-1; task E13/F09/US01/T04,
 * task text point (4) "Resume"). Mirrors `../contracts/contract360/index.tsx`'s own `FetchState`
 * union shape for the identical reason that screen's header comment gives: a `404` is this screen's
 * own named "not found" state, distinct from a transport/5xx error, not folded into one generic
 * failure branch.
 */
export type ConversationFetchState =
  | { phase: "idle" }
  | { phase: "loading" }
  | { phase: "not-found" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; conversation: ConversationDetailBody; turns: readonly AskTurnView[] };

export interface UseConversationResult {
  state: ConversationFetchState;
  reload: () => void;
}

/**
 * `conversationId === null` (the "new chat" screen, `/ask` with no id yet) stays `{ phase: "idle" }`
 * forever -- this hook never fetches without a real id to resume, the same "nothing to load" state
 * `../contracts/index.tsx` uses for its own no-workspace guard.
 */
export function useConversation(apiClient: ApiClient, tenantId: string | undefined, conversationId: string | null): UseConversationResult {
  const [state, setState] = useState<ConversationFetchState>({ phase: "idle" });

  const load = useCallback(() => {
    if (!tenantId || !conversationId) {
      setState({ phase: "idle" });
      return;
    }

    setState({ phase: "loading" });

    void apiClient.getConversation(tenantId, conversationId).then((result) => {
      if (!result.ok || !result.conversation) {
        if (result.statusCode === 404) {
          setState({ phase: "not-found" });
          return;
        }
        setState({
          phase: "error",
          statusCode: result.statusCode,
          // Same 503-vs-other split as ../contracts/index.tsx's own loadPortfolio (ADR-019
          // accessibility baseline: "names the failing job, never a raw stack trace").
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Raffa.ai's conversation service is temporarily unavailable. Try again in a moment."
              : (result.error ?? "This conversation could not be loaded."),
        });
        return;
      }

      setState({
        phase: "ready",
        conversation: result.conversation,
        turns: buildTurnsFromConversation(result.conversation),
      });
    });
  }, [apiClient, tenantId, conversationId]);

  useEffect(() => {
    load();
  }, [load]);

  return { state, reload: load };
}
