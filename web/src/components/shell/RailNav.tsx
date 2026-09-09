import { Link, NavLink } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import {
  buildPrimaryNavItems,
  buildSecondaryNavItems,
  canManageMembers,
  getDocumentsBadge,
  WORKSPACE_ROLE_LABEL,
  type NavBadge,
  type WorkspaceRole,
} from "./navItems";
import { loadTrackedDocuments } from "../../routes/documents/documentStore";
import { useRecentConversations } from "../../routes/ask/useRecentConversations";

export interface RailNavProps {
  workspaceName: string;
  role: WorkspaceRole;
  userLabel: string;
  /** `useValidatedContractCount`'s result, fetched once by `AppShell.tsx` and passed down -- this
   * component never calls that API itself. */
  kbReady: boolean;
  validatedContractCount: number;
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
 * 224px left rail, V2 two-tier information architecture (ADR-024 amendment to ADR-018/ADR-020; task
 * E13/F09/US01/T01, gap G-IA-V2). Layout/measurements (224px grid column via shell.css, item
 * padding, "From your contracts" kicker) are confirmed against
 * `inputs/design/prototypes/contigo-v2/markup.html`'s own `<nav>` block, not invented; the two-tier
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
  onSignOut,
  apiClient,
}: RailNavProps) {
  // Session-local read, not React state -- re-evaluated on every render, the same "read-only
  // consumer" shape `../../routes/renewals/renewalActionStore.ts`'s own consumer
  // (`../../routes/savings/index.tsx`) already establishes: there is nothing to keep in sync beyond
  // re-reading it, and this component re-renders on every nested navigation (`WorkspaceShellApp.tsx`'s
  // `<Routes>` re-renders the whole matched branch, including this layout route, on each location
  // change). There is still no `GET /api/documents` collection endpoint (gap G-DOC-API) -- see
  // `navItems.ts#DocumentCounts`'s own doc comment for the full provenance.
  const trackedDocuments = loadTrackedDocuments();
  // Task E13/F09/US01/T04 (gap G-CONVERSATIONS): last 5 conversations + which one (if any) is
  // active -- see that hook's own doc comment for why this re-fetches on navigation rather than
  // once per shell mount.
  const { conversations, activeConversationId } = useRecentConversations(apiClient);
  const documentsBadge = getDocumentsBadge({
    total: trackedDocuments.length,
    needsReview: trackedDocuments.filter((doc) => doc.processingStatus === "NeedsReview").length,
  });

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
                {/* R-CONV-02 "the rail shows the user's last 5 conversations, resume by click,
                    active one in accent" -- `useRecentConversations` already caps this at the
                    backend's own default (5); `.slice(0, 5)` here is defensive, not load-bearing. */}
                {conversations.slice(0, 5).map((conversation) => (
                  <Link
                    key={conversation.id}
                    to={`/ask/${conversation.id}`}
                    className={`shell-rail-conv-item${conversation.id === activeConversationId ? " is-active" : ""}`}
                  >
                    <span className="shell-rail-conv-title">{conversation.title}</span>
                  </Link>
                ))}
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
