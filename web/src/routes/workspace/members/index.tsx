import { useCallback, useEffect, useState } from "react";
import type { ApiClient, WorkspaceMemberBody } from "../../../api/client";
import { canManageMembers, type WorkspaceRole } from "../../../components/shell/navItems";
import { useShellContext } from "../../../components/shell/shellContext";
import InvitePane from "./InvitePane";
import MembersTable from "./MembersTable";
import {
  MEMBERS_TIP,
  buildRequestAccessMailto,
  formatDomainWarning,
  formatWorkspaceLine,
  validateInviteEmail,
  workspaceDomainFromEmail,
  type Day1InviteRole,
  type InviteOutcome,
} from "./memberViewModel";
import "./members.css";

export interface MembersRouteProps {
  apiClient: ApiClient;
  userLabel: string;
  /** Consumed, never derived: the phase-4 shell sibling (`E14/F03/US02/T01`) reads this from the
   * server-revalidated current workspace and passes it down `WorkspaceShellAppProps`. This route
   * must not call `loadCurrentWorkspace()` itself (that was the pre-w14 `sessionStorage` read this
   * task deletes with `memberStore.ts`). */
  workspaceId: string;
  /** Server-derived (ADR-025 §I; `resolveWorkspaceRole()` upstream today, a real claim/membership
   * read once ADR-010 lands) -- gates the Admin-only body via `canManageMembers`, never a
   * client-declared assumption. */
  role: WorkspaceRole;
}

type RosterState =
  | { phase: "loading" }
  | { phase: "error"; message: string }
  | { phase: "ready"; members: WorkspaceMemberBody[] };

/**
 * Route `/workspace/members` -- Workspace & members, V2 (ADR-020 screen 10; screens-v2.md #10;
 * `raffa-v2/markup.html` "WORKSPACE & MEMBERS" block). Header ("Setup" kicker · "Workspace &
 * members" · "tenant {id}"), the `kbOff` tip while nothing is validated yet, then either the
 * two-column Admin body (members table left, "Invite a colleague" right) or the single-column
 * Procurement body (members table, read-only, with a real `mailto:` "Request access" note beneath
 * it -- ADR-020 §10.7). The non-admin *route* guard (`RequireRole`) is the phase-4 shell sibling's;
 * this component renders for **any** live member, Admin or not (ADR-025 Rule D.4a).
 *
 * The roster is a real server read (`GET /api/workspaces/{tenantId}/members`, ADR-026 §D3) -- a
 * list surface per ADR-018 `:112-119`: a skeleton while loading, and an error state with Retry that
 * **never** falls back to the last known roster. An invite, a revoke or a remove all **re-read** the
 * roster on success; none of the three ever mutates it locally (no optimistic append, no optimistic
 * removal) -- the defect this task replaces (`memberStore.ts`, deleted whole) was exactly that kind
 * of local echo.
 */
export default function MembersRoute({ apiClient, userLabel, workspaceId, role }: MembersRouteProps) {
  const shell = useShellContext();
  const isAdmin = canManageMembers(role);

  const [roster, setRoster] = useState<RosterState>({ phase: "loading" });
  const [email, setEmail] = useState("");
  const [inviteRole, setInviteRole] = useState<Day1InviteRole>("Procurement");
  const [error, setError] = useState<string | null>(null);
  const [warning, setWarning] = useState<string | null>(null);
  const [result, setResult] = useState<InviteOutcome | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const tenantDomain = workspaceDomainFromEmail(userLabel);

  const loadMembers = useCallback(() => {
    setRoster({ phase: "loading" });
    void apiClient.getWorkspaceMembers(workspaceId).then((response) => {
      if (!response.ok || response.members === null) {
        setRoster({ phase: "error", message: response.error ?? "The roster could not be loaded." });
        return;
      }
      setRoster({ phase: "ready", members: response.members });
    });
  }, [apiClient, workspaceId]);

  useEffect(() => {
    loadMembers();
  }, [loadMembers]);

  const sendInvite = useCallback(() => {
    const validation = validateInviteEmail(email, tenantDomain);
    if (validation.error !== null) {
      setError(validation.error);
      setWarning(null);
      return;
    }

    setError(null);
    setWarning(
      validation.crossDomain && tenantDomain !== null ? formatDomainWarning(email.trim(), tenantDomain, inviteRole) : null,
    );
    setResult(null);
    setSubmitting(true);

    void apiClient.inviteWorkspaceMember(workspaceId, { email: email.trim(), role: inviteRole }).then((response) => {
      setSubmitting(false);
      // ADR-020 §10.2 / AC-4: the failure path is as wrong to claim a mail as the success path is
      // to claim one unconditionally -- neither "sent" nor "could not be sent" belongs here.
      if (!response.ok || response.member === null) {
        setError(response.error ?? "The invitation could not be created.");
        return;
      }

      setEmail("");
      setResult({
        email: response.member.email,
        mailDelivered: response.member.mailDelivered,
        acceptUrl: response.member.acceptUrl,
        expiresAt: response.member.expiresAt,
      });
      // AC-1 / AC-3: the new row (and its real status) comes from a re-read, never an optimistic
      // local append -- the exact defect `memberStore.ts#rememberInvitedMember` used to be.
      loadMembers();
    });
  }, [apiClient, email, inviteRole, tenantDomain, workspaceId, loadMembers]);

  // See MembersTable.tsx's own header comment for the confirmed id-contract gap these two ids run
  // into against the real backend (roster `id` is `WorkspaceUser.Id`; revoke/remove each need a
  // different, unexposed id). Wired exactly as ADR-020 §10.4 specifies regardless -- there is no
  // `web/src`-only alternative -- and a non-ok response renders inline rather than silently
  // no-opping, so a caller never sees a click that appeared to do nothing.
  const handleRevoke = useCallback(
    (invitationRowId: string) => {
      setActionError(null);
      void apiClient.revokeInvitation(workspaceId, invitationRowId).then((response) => {
        if (!response.ok) {
          setActionError(response.error ?? "This invitation could not be revoked.");
          return;
        }
        loadMembers();
      });
    },
    [apiClient, workspaceId, loadMembers],
  );

  const handleRemove = useCallback(
    (membershipRowId: string) => {
      setActionError(null);
      void apiClient.removeMember(workspaceId, membershipRowId).then((response) => {
        if (!response.ok) {
          setActionError(response.error ?? "This member could not be removed.");
          return;
        }
        // ADR-020 §10.4: on a self-removal the app "must not sit in a shell for a tenant the user
        // no longer belongs to". A full router hand-off to /signin needs App.tsx/workspaceStore.ts,
        // both outside this task's boundary (see the IMPLEMENTER turn) -- so this re-read is the
        // honest, in-boundary fallback: the caller is no longer a live member of the route tenant,
        // so the very next GET answers 404 and the error state below renders instead of a stale
        // roster, rather than silently doing nothing.
        loadMembers();
      });
    },
    [apiClient, workspaceId, loadMembers],
  );

  return (
    <div className="members-screen">
      <header className="screen-header">
        <div>
          <p className="screen-kicker">Setup</p>
          <h2 className="screen-title">Workspace &amp; members</h2>
          <p className="screen-header-summary">{formatWorkspaceLine(workspaceId)}</p>
        </div>
      </header>

      {shell?.kbReady === false && (
        <p className="members-tip" role="note">
          {MEMBERS_TIP}
        </p>
      )}

      {actionError !== null && (
        <p className="hint members-action-error" role="alert">
          {actionError}
        </p>
      )}

      {roster.phase === "loading" && (
        <div className="members-table-skeleton" role="status" aria-live="polite">
          <p className="micro-meta">Loading roster…</p>
          {Array.from({ length: 4 }, (_, index) => (
            <div key={index} className="skeleton members-table-skeleton-row" />
          ))}
        </div>
      )}

      {roster.phase === "error" && (
        <div className="error-state" role="alert">
          <h4>Members unavailable</h4>
          <p className="micro-meta">{roster.message}</p>
          <button type="button" className="btn btn-secondary" onClick={loadMembers}>
            Retry
          </button>
        </div>
      )}

      {roster.phase === "ready" && (
        <div className={isAdmin ? "members-body" : "members-body members-body-readonly"}>
          <div className="members-table-column">
            <MembersTable
              members={roster.members}
              currentUserEmail={userLabel}
              isAdmin={isAdmin}
              onRevoke={handleRevoke}
              onRemove={handleRemove}
            />

            {!isAdmin && (
              <div className="members-readonly-note">
                <h4>You don&apos;t manage this workspace</h4>
                <p className="micro-meta">Only a Workspace Admin can invite, revoke or remove members.</p>
                <a className="btn btn-secondary" href={buildRequestAccessMailto(roster.members)}>
                  Request access
                </a>
              </div>
            )}
          </div>

          {isAdmin && (
            <InvitePane
              email={email}
              role={inviteRole}
              tenantDomain={tenantDomain}
              error={error}
              warning={warning}
              result={result}
              submitting={submitting}
              onEmailChange={(value) => {
                setEmail(value);
                setError(null);
                setWarning(null);
                setResult(null);
              }}
              onRoleChange={setInviteRole}
              onSubmit={sendInvite}
            />
          )}
        </div>
      )}
    </div>
  );
}
