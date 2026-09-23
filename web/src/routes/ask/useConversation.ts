import { useCallback, useEffect, useRef, useState } from "react";
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

const IDLE: ConversationFetchState = { phase: "idle" };
const LOADING: ConversationFetchState = { phase: "loading" };

export interface UseConversationResult {
  state: ConversationFetchState;
  reload: () => void;
}

/**
 * `conversationId === null` (the "new chat" screen, `/ask` with no id yet) stays `{ phase: "idle" }`
 * forever -- this hook never fetches without a real id to resume, the same "nothing to load" state
 * `../contracts/index.tsx` uses for its own no-workspace guard.
 *
 * The state is tagged with the id it answers for: switching straight from one conversation to
 * another reads "loading" from the very first render (never the previous one's "ready" or
 * "not-found"), and a response that arrives after the user has moved on is dropped.
 */
export function useConversation(apiClient: ApiClient, tenantId: string | undefined, conversationId: string | null): UseConversationResult {
  const [tagged, setTagged] = useState<{ id: string | null; state: ConversationFetchState }>({ id: null, state: IDLE });
  const requested = useRef<string | null>(null);

  const load = useCallback(() => {
    requested.current = tenantId && conversationId ? conversationId : null;
    if (!tenantId || !conversationId) {
      setTagged({ id: null, state: IDLE });
      return;
    }

    const id = conversationId;
    const settle = (state: ConversationFetchState) => {
      if (requested.current === id) setTagged({ id, state });
    };
    setTagged({ id, state: LOADING });

    void apiClient.getConversation(tenantId, id).then((result) => {
      if (!result.ok || !result.conversation) {
        if (result.statusCode === 404) {
          settle({ phase: "not-found" });
          return;
        }
        settle({
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

      settle({
        phase: "ready",
        conversation: result.conversation,
        turns: buildTurnsFromConversation(result.conversation),
      });
    });
  }, [apiClient, tenantId, conversationId]);

  useEffect(() => {
    load();
  }, [load]);

  const expectedId = tenantId && conversationId ? conversationId : null;
  const state = tagged.id === expectedId ? tagged.state : expectedId === null ? IDLE : LOADING;
  return { state, reload: load };
}
