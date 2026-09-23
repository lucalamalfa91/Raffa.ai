import { useEffect, useId, useMemo, useState } from "react";
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
import { CONVERSATION_LIST_TAKE, useRecentConversations } from "../../routes/ask/useRecentConversations";
import InfoTip, { CopyTip } from "../InfoTip";
import { buildRailContractsTip, TIPS } from "../infoTipCopy";
import {
  conversationDisplayTitle,
  filterConversations,
  indexPortfolioByContractId,
} from "../../routes/ask/conversationTitle";
import { loadCurrentWorkspace } from "../../routes/signin/workspaceStore";
import { useAskSessionStore, useAskSessionsSnapshot } from "../../routes/ask/AskSessionsContext";
import { customTitleFor } from "../../routes/ask/askSessions";
import ConversationRenameField from "../../routes/ask/ConversationRenameField";
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

/** "12 Sep" -- when an archived chat was last used, so an old chat is easy to place. Fixed month
 * names rather than `Intl`, whose short months differ between browsers ("Sep" / "Sept"). */
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
function formatArchivedDate(updatedAt: string): string {
  const date = new Date(updatedAt);
  return Number.isNaN(date.getTime()) ? "" : `${date.getDate()} ${MONTHS[date.getMonth()]}`;
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
  const askSnapshot = useAskSessionsSnapshot(askSessions);
  const { sessions, listVersion } = askSnapshot;
  const { conversations, archivedConversations, activeConversationId, reload } = useRecentConversations(apiClient, listVersion);
  const [chatQuery, setChatQuery] = useState("");
  const [archiveExpanded, setArchiveExpanded] = useState(false);
  // Chats taken out of the archive in this tab: listed as recent at once, ahead of the reload.
  const [restoredIds, setRestoredIds] = useState<ReadonlySet<string>>(() => new Set());
  const archiveListId = useId();
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameError, setRenameError] = useState<{ conversationId: string; message: string } | null>(null);
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
  // A name given in this tab shows at once, ahead of the list's next reload.
  const titleOf = (conversation: (typeof conversations)[number]) =>
    conversationDisplayTitle(
      { scopeContractId: conversation.scopeContractId, customTitle: customTitleFor(askSnapshot, conversation.id, conversation.customTitle) },
      portfolioById,
    );
  // Once the server lists a restored chat as in use, it no longer needs the local override.
  useEffect(() => {
    if (restoredIds.size === 0) return;
    const confirmed = [...restoredIds].filter((id) => conversations.some((conversation) => conversation.id === id));
    if (confirmed.length === 0) return;
    setRestoredIds((current) => new Set([...current].filter((id) => !confirmed.includes(id))));
  }, [conversations, restoredIds]);

  const recentConversations = useMemo(() => {
    const restored = archivedConversations.filter(
      (conversation) => restoredIds.has(conversation.id) && !conversations.some((recent) => recent.id === conversation.id),
    );
    return [...restored, ...conversations];
  }, [conversations, archivedConversations, restoredIds]);
  const archive = useMemo(
    () => archivedConversations.filter((conversation) => !restoredIds.has(conversation.id)),
    [archivedConversations, restoredIds],
  );

  const searching = chatQuery.trim() !== "";
  const visibleConversations = filterConversations(recentConversations, chatQuery, titleOf);
  const visibleArchive = filterConversations(archive, chatQuery, titleOf);
  // A search looks in the archive too, and opens it on a match: "I can't find my chat" ends there.
  const archiveOpen = searching ? visibleArchive.length > 0 : archiveExpanded;
  const archiveCount = archive.length >= CONVERSATION_LIST_TAKE ? `${CONVERSATION_LIST_TAKE}+` : String(archive.length);

  /** An archived chat opens like any other, and clicking it also takes it out of the archive. */
  const restoreChat = (conversationId: string) => {
    if (!workspace) return;
    setRestoredIds((current) => new Set(current).add(conversationId));
    void apiClient.restoreConversation(workspace.id, conversationId).then((result) => {
      if (result.ok) {
        reload();
        return;
      }
      setRestoredIds((current) => new Set([...current].filter((id) => id !== conversationId)));
    });
  };

  const renameChat = (conversationId: string, name: string) => {
    setRenamingId(null);
    setRenameError(null);
    if (!workspace) return;
    void askSessions.rename({ apiClient, tenantId: workspace.id, conversationId, name }).then((result) => {
      if (!result.ok) setRenameError({ conversationId, message: "Could not rename this chat. Try again." });
    });
  };

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

  const renderRow = (conversation: (typeof conversations)[number], archived: boolean) => {
    const title = titleOf(conversation);
    // Parallel Ask sessions (`askSessions.ts`): a chat still answering in the
    // background spins; one whose reply landed while it was not on screen is
    // highlighted until it is opened.
    const session = sessions.get(conversation.id);
    const pending = session?.pending === true;
    const unread = !pending && session?.unread === true;
    const active = conversation.id === activeConversationId;
    if (renamingId === conversation.id) {
      return (
        <div key={conversation.id} className="shell-rail-conv-row is-renaming">
          <ConversationRenameField
            className="shell-rail-conv-rename"
            initialValue={title}
            label={`Rename ${title}`}
            onCommit={(name) => renameChat(conversation.id, name)}
            onCancel={() => setRenamingId(null)}
          />
        </div>
      );
    }
    return (
      <div key={conversation.id}>
        <div className={`shell-rail-conv-row${unread ? " is-unread" : ""}`}>
          <Link
            to={`/ask/${conversation.id}`}
            className={`shell-rail-conv-item${active ? " is-active" : ""}${unread ? " is-unread" : ""}`}
            onClick={archived ? () => restoreChat(conversation.id) : undefined}
            onDoubleClick={(event) => {
              event.preventDefault();
              setRenamingId(conversation.id);
            }}
          >
            <span className="shell-rail-conv-title">{title}</span>
            {archived && (
              <span className="shell-rail-conv-meta">
                <span className="visually-hidden">, archived, last used </span>
                {formatArchivedDate(conversation.updatedAt)}
              </span>
            )}
            {pending && <span className="shell-rail-conv-status is-pending" aria-hidden="true" />}
            {unread && <span className="shell-rail-conv-status is-unread" aria-hidden="true" />}
            {pending && <span className="visually-hidden"> · Raffa is answering</span>}
            {unread && <span className="visually-hidden"> · new reply</span>}
          </Link>
          {/* Shown on hover/focus (always on touch screens) -- see shell.css. */}
          <span className="shell-rail-conv-actions">
            <button
              type="button"
              className="shell-rail-conv-action"
              aria-label={`Rename ${title}`}
              onClick={() => {
                setRenameError(null);
                setRenamingId(conversation.id);
              }}
            >
              Rename
            </button>
            <button
              type="button"
              className="shell-rail-conv-action shell-rail-conv-delete"
              aria-label={`Delete ${title}`}
              onClick={() => deleteChat(conversation.id)}
            >
              Delete
            </button>
          </span>
        </div>
        {renameError?.conversationId === conversation.id && (
          <p className="shell-rail-conv-error" role="alert">
            {renameError.message}
          </p>
        )}
      </div>
    );
  };

  return (
    <nav className="shell-rail" aria-label="Primary">
      <div className="shell-rail-header">
        <span className="shell-rail-kicker">
          Workspace
          <CopyTip tip={TIPS.railWorkspace} />
        </span>
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
                {(recentConversations.length > 0 || archive.length > 0) && (
                  <div className="shell-rail-search">
                    <svg className="icon shell-rail-search-icon" viewBox="0 0 24 24" aria-hidden="true">
                      <circle cx="11" cy="11" r="7" />
                      <path d="m20 20-3.5-3.5" />
                    </svg>
                    <input
                      type="search"
                      className="shell-rail-conv-search"
                      placeholder="Search chats"
                      aria-label="Search chats"
                      autoComplete="off"
                      spellCheck={false}
                      value={chatQuery}
                      onChange={(event) => setChatQuery(event.target.value)}
                      onKeyDown={(event) => {
                        if (event.key === "Escape" && chatQuery !== "") {
                          event.preventDefault();
                          setChatQuery("");
                        }
                      }}
                    />
                    {chatQuery !== "" && (
                      <button type="button" className="shell-rail-search-clear" aria-label="Clear search" onClick={() => setChatQuery("")}>
                        <svg className="icon" viewBox="0 0 24 24" aria-hidden="true">
                          <path d="M6 6l12 12M18 6 6 18" />
                        </svg>
                      </button>
                    )}
                  </div>
                )}
                {visibleConversations.map((conversation) => renderRow(conversation, false))}
                {searching && visibleConversations.length === 0 && visibleArchive.length === 0 && (
                  <p className="shell-rail-search-empty" role="status">
                    No chats match “{chatQuery.trim()}”.
                  </p>
                )}
                <Link to="/ask" state={{ newChat: true }} className="shell-rail-new-chat">
                  + New chat
                </Link>
                {archive.length > 0 && (
                  <div className="shell-rail-archive">
                    <div className="shell-rail-archive-header">
                      <button
                        type="button"
                        className="shell-rail-archive-toggle"
                        aria-expanded={archiveOpen}
                        aria-controls={archiveListId}
                        onClick={() => setArchiveExpanded(!archiveOpen)}
                      >
                        <span className={`shell-rail-archive-chevron${archiveOpen ? " is-open" : ""}`} aria-hidden="true" />
                        <span className="shell-rail-archive-label">Archive</span>
                        <span className="shell-rail-archive-count">{archiveCount}</span>
                      </button>
                      <InfoTip label="About the archive">
                        <p>Chats you have not used for a week move here, so your list stays short.</p>
                        <p>Can&apos;t find a chat? It is in the archive. Click it and it is back in your chats, ready to use.</p>
                      </InfoTip>
                    </div>
                    {archiveOpen && (
                      <div id={archiveListId} className="shell-rail-archive-list">
                        {visibleArchive.map((conversation) => renderRow(conversation, true))}
                      </div>
                    )}
                  </div>
                )}
              </div>
            )}
          </div>
        ))}
      </div>

      <div className="shell-rail-section-kicker">
        From your contracts
        <span className={`shell-rail-kb-dot${kbReady ? " is-ready" : ""}`} aria-hidden="true" />
        <CopyTip tip={buildRailContractsTip(kbReady)} />
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
