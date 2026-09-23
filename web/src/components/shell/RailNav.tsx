import { useEffect, useMemo, useState } from "react";
import { Link, NavLink, useNavigate } from "react-router-dom";
import type { ApiClient, PortfolioListItem } from "../../api/client";
import {
  buildPrimaryNavItems,
  buildSecondaryNavItems,
  canManageMembers,
  getDocumentsBadge,
  WORKSPACE_ROLE_LABEL,
  type NavBadge,
  type WorkspaceRole,
} from "./navItems";
import { useRecentConversations } from "../../routes/ask/useRecentConversations";
import {
  conversationDisplayTitle,
  filterConversations,
  indexPortfolioByContractId,
} from "../../routes/ask/conversationTitle";
import { loadCurrentWorkspace } from "../../routes/signin/workspaceStore";
import { useAskSessionStore, useAskSessionsSnapshot } from "../../routes/ask/AskSessionsContext";
import type { DocumentCountsBody } from "./useDocumentCounts";

export interface RailNavProps {
  workspaceName: string;
  role: WorkspaceRole;
  userLabel: string;
  /** `useValidatedContractCount`'s result, fetched once by `AppShell.tsx` and passed down -- this
   * component never calls that API itself. */
  kbReady: boolean;
  validatedContractCount: number;
  /** `useDocumentCounts`'s result (task E16/F03/US01/T01, NW-10): the server's tenant-wide
   * `counts`, fetched once by `AppShell.tsx` the same way; `null` until known, which renders as no
   * badge at all. Replaces the `sessionStorage` tracker nothing had written since E13/F09/US01/T03. */
  documentCounts: DocumentCountsBody | null;
  onSignOut: () => void;
  /**
   * Task E13/F09/US01/T04 (web-ask-v2, gap G-CONVERSATIONS): threaded through so this component can
   * call `useRecentConversations` itself for the rail's own "last 5 conversations" slot (task text
   * point (5): "RailNav.tsx consumes a useRecentConversations hook"). Unlike `kbReady`/
   * `validatedContractCount` above (fetched once by `AppShell.tsx`, since a validated contract is a
   * rare event), conversations are routine enough (every "+ New chat") that this component owns its
   * own re-fetch-on-navigation timing -- see that hook's own doc comment for why.
   */
  apiClient: ApiClient;
}

function RailBadge({ badge }: { badge: NavBadge | null }) {
  if (badge === null) return null;
  return (
    <span className={`shell-rail-badge${badge.tone === "attention" ? " is-attention" : ""}`}>{badge.text}</span>
  );
}

/**
 * 232px left rail, V2 two-tier information architecture (ADR-024 amendment to ADR-018/ADR-020; task
 * E13/F09/US01/T01, gap G-IA-V2). Layout/measurements (232px grid column via shell.css, item
 * padding, "From your contracts" kicker) are confirmed against
 * `inputs/design/prototypes/raffa-v2/markup.html`'s own `<nav>` block, not invented; the two-tier
 * model itself (`navItems.ts`) is `app.jsx`'s `primaryNav`/`kbNav`. Icons are still omitted --
 * `lucide-react` (or an equivalent) is not a dependency of this package yet, unchanged from the
 * Day-1 rail this replaces.
 */
export default function RailNav({
  workspaceName,
  role,
  userLabel,
  kbReady,
  validatedContractCount,
  documentCounts,
  onSignOut,
  apiClient,
}: RailNavProps) {
  const navigate = useNavigate();
  const workspace = loadCurrentWorkspace();
  const askSessions = useAskSessionStore();
  const { sessions, listVersion } = useAskSessionsSnapshot(askSessions);
  const { conversations, activeConversationId, reload } = useRecentConversations(apiClient, listVersion);
  const [chatQuery, setChatQuery] = useState("");
  const [portfolioItems, setPortfolioItems] = useState<readonly PortfolioListItem[]>([]);

  useEffect(() => {
    if (!workspace) {
      setPortfolioItems([]);
      return;
    }
    void apiClient.getPortfolio(workspace.id, { pageSize: 100 }).then((result) => {
      setPortfolioItems(result.ok && result.portfolio ? result.portfolio.items : []);
    });
  }, [apiClient, workspace?.id, conversations]);

  const portfolioById = useMemo(() => indexPortfolioByContractId(portfolioItems), [portfolioItems]);
  const titleOf = (conversation: (typeof conversations)[number]) => conversationDisplayTitle(conversation, portfolioById);
  const visibleConversations = filterConversations(conversations, chatQuery, titleOf);

  const deleteChat = (conversationId: string) => {
    if (!workspace) return;
    void apiClient.deleteConversation(workspace.id, conversationId).then((result) => {
      if (!result.ok) return;
      askSessions.forget(conversationId);
      if (activeConversationId === conversationId) {
        navigate("/ask", { state: { newChat: true } });
      }
      reload();
    });
  };
  // Task E16/F03/US01/T01 (ADR-012 w15 §6): the badge reads the server's `counts` -- `all` for
  // "N docs" (what Raffa.ai keeps, never a refused file), `needsReview` for "N to review" -- and
  // stays absent until the shell has confirmed a number (`getDocumentsBadge`'s honest-absence rule).
  const documentsBadge = documentCounts ? getDocumentsBadge({ total: documentCounts.all, needsReview: documentCounts.needsReview }) : null;

  const primaryItems = buildPrimaryNavItems(documentsBadge);
  const secondaryItems = buildSecondaryNavItems({ kbReady, validatedContractCount });

  return (
    <nav className="shell-rail" aria-label="Primary">
      <div className="shell-rail-header">
        <span className="shell-rail-kicker">Workspace</span>
        <div className="shell-rail-workspace-name">{workspaceName}</div>
      </div>

      <div className="shell-rail-nav">
        {primaryItems.map((item) => (
          <div key={item.id}>
            <NavLink
              to={item.path}
              className={({ isActive }) => `shell-rail-primary-item${isActive ? " is-active" : ""}`}
            >
              <span className="shell-rail-item-label">{item.label}</span>
              <RailBadge badge={item.badge} />
            </NavLink>

            {item.hasConversationSlot && (
              <div className="shell-rail-conversations">
                {conversations.length > 0 && (
                  <input
                    type="search"
                    className="input shell-rail-conv-search"
                    placeholder="Search chats"
                    aria-label="Search chats"
                    autoComplete="off"
                    value={chatQuery}
                    onChange={(event) => setChatQuery(event.target.value)}
                  />
                )}
                {visibleConversations.map((conversation) => {
                  const title = titleOf(conversation);
                  // Parallel Ask sessions (`askSessions.ts`): a chat still answering in the
                  // background spins; one whose reply landed while it was not on screen is
                  // highlighted until it is opened.
                  const session = sessions.get(conversation.id);
                  const pending = session?.pending === true;
                  const unread = !pending && session?.unread === true;
                  return (
                    <div key={conversation.id} className={`shell-rail-conv-row${unread ? " is-unread" : ""}`}>
                      <Link
                        to={`/ask/${conversation.id}`}
                        className={`shell-rail-conv-item${conversation.id === activeConversationId ? " is-active" : ""}${unread ? " is-unread" : ""}`}
                      >
                        <span className="shell-rail-conv-title">{title}</span>
                        {pending && <span className="shell-rail-conv-status is-pending" aria-hidden="true" />}
                        {unread && <span className="shell-rail-conv-status is-unread" aria-hidden="true" />}
                        {pending && <span className="visually-hidden"> · Raffa is answering</span>}
                        {unread && <span className="visually-hidden"> · new reply</span>}
                      </Link>
                      <button
                        type="button"
                        className="shell-rail-conv-delete"
                        aria-label={`Delete ${title}`}
                        onClick={() => deleteChat(conversation.id)}
                      >
                        Delete
                      </button>
                    </div>
                  );
                })}
                <Link to="/ask" state={{ newChat: true }} className="shell-rail-new-chat">
                  + New chat
                </Link>
              </div>
            )}
          </div>
        ))}
      </div>

      <div className="shell-rail-section-kicker">
        From your contracts
        <span className={`shell-rail-kb-dot${kbReady ? " is-ready" : ""}`} aria-hidden="true" />
      </div>

      <div className="shell-rail-secondary-nav">
        {secondaryItems.map((item) => (
          <NavLink
            key={item.id}
            to={item.path}
            className={({ isActive }) =>
              `shell-rail-secondary-item${item.greyed ? " is-greyed" : ""}${isActive ? " is-active" : ""}`
            }
          >
            <span className="shell-rail-item-label">{item.label}</span>
            <RailBadge badge={item.badge} />
          </NavLink>
        ))}
      </div>

      <div className="shell-rail-footer">
        {canManageMembers(role) && (
          <Link to="/workspace/members" className="shell-rail-footer-link">
            Workspace &amp; members
          </Link>
        )}
        <div className="shell-rail-user">{userLabel}</div>
        <div className="micro-meta">
          <span>{WORKSPACE_ROLE_LABEL[role]}</span> ·{" "}
          <button type="button" className="shell-rail-signout" onClick={onSignOut}>
            Sign out
          </button>
        </div>
      </div>
    </nav>
  );
}
