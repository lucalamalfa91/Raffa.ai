import { useCallback, useEffect, useState } from "react";
import { useLocation } from "react-router-dom";
import type { ApiClient, ConversationSummaryBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";

/**
 * The rail's "last 5 conversations" slot (`GET /api/conversations`; R-CONV-02; task
 * E13/F09/US01/T04, gap G-IA-V2 "rail slot"; `components/shell/RailNav.tsx`'s own header comment:
 * "the last 5 conversations render here once F09/T04 wires `GET /api/conversations`"). Lives in
 * `routes/ask/` (not `components/shell/`) because "recent conversations" is an Ask-domain concept
 * that reuses `ConversationSummaryBody` -- `RailNav.tsx` already imports a sibling read
 * (`loadTrackedDocuments`) from `routes/documents/documentStore.ts` across the identical folder
 * boundary, the established precedent for this exact shape.
 *
 * **Refetches on navigation, unlike `useValidatedContractCount` (which fetches once per shell
 * mount and never again).** That hook's own doc comment accepts a mid-session validated contract
 * only updating the rail "on the next full shell mount" because becoming validated is a rare,
 * multi-step event. A **new conversation** is a routine, every-few-clicks event ("+ New chat" then
 * ask a question) that must appear in the rail immediately, and the one reliable signal available
 * without prop-drilling a "a conversation was just created" callback through `GlobalAskBar`/
 * `AskRoute`/`RailNav` is the URL itself: every real "a conversation might now exist that didn't
 * before" moment in this app is also a navigation (to `/ask/<newId>`, to `/ask` for a fresh chat,
 * or between two existing conversations). Depending on `location.pathname` in the effect below
 * re-runs the fetch on exactly those moments, not on every unrelated re-render (React Router keeps
 * `location` referentially stable between navigations, the same guarantee
 * `contract360ViewModel.ts`'s own `?clause=`/`?page=` handling already relies on).
 */
export interface RecentConversationsState {
  conversations: readonly ConversationSummaryBody[];
  /** `/ask/<id>` (route param) matches one of `conversations[].id` -- RailNav's own "active one in
   * accent" (task text point (5)). `null` on `/ask` itself (no conversation open) or any other
   * screen. */
  activeConversationId: string | null;
  reload: () => void;
}

const EMPTY_STATE: readonly ConversationSummaryBody[] = [];

/** `/ask/<conversationId>` -- the one route shape `WorkspaceShellApp.tsx` maps to `AskRoute`
 * carrying a resumable id (`/ask` itself, with no trailing segment, is the "new chat" screen).
 * Plain `pathname` parsing, the same `URLSearchParams`/string convention
 * `components/shell/workspaceRole.ts` and `contract360ViewModel.ts` already use for their own
 * router-state reads, rather than `useParams()` -- this hook mounts inside `RailNav.tsx`, which
 * renders in `AppShell.tsx` on the *parent* layout route, one level above wherever `:conversationId`
 * is actually bound, so `useParams()` there would never see it. */
export function activeConversationIdFromPathname(pathname: string): string | null {
  const match = /^\/ask\/([^/?#]+)/.exec(pathname);
  return match ? decodeURIComponent(match[1]) : null;
}

export function useRecentConversations(apiClient: ApiClient): RecentConversationsState {
  const location = useLocation();
  const workspace = loadCurrentWorkspace();
  const [conversations, setConversations] = useState<readonly ConversationSummaryBody[]>(EMPTY_STATE);

  const load = useCallback(() => {
    if (!workspace) {
      setConversations(EMPTY_STATE);
      return;
    }

    void apiClient.listConversations(workspace.id).then((result) => {
      setConversations(result.ok && result.conversations ? result.conversations : EMPTY_STATE);
    });
    // workspace?.id (a primitive), not workspace itself -- loadCurrentWorkspace() returns a fresh
    // object every call, the same convention every other hook in this app already follows
    // (useValidatedContractCount.ts, ../documents/useDocumentsList.ts).
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    load();
    // See this module's own header comment for why `location.pathname` is a deliberate dependency
    // here, unlike useValidatedContractCount's identical-shaped effect.
  }, [load, location.pathname]);

  return {
    conversations,
    activeConversationId: activeConversationIdFromPathname(location.pathname),
    reload: load,
  };
}
