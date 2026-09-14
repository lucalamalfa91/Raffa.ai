import { useCallback, useEffect, useState } from "react";
import type { ApiClient, WorkspaceMemberBody } from "../../../api/client";
import { useShellContext } from "../../../components/shell/shellContext";
import { canManageMembers, type WorkspaceRole } from "../../../components/shell/navItems";
import InvitePane, { type InviteFailure } from "./InvitePane";
import MembersTable, { type MembersActionError } from "./MembersTable";
import {
  MEMBERS_TIP,
  formatWorkspaceLine,
  inviteDomainWarning,
  inviteOutcomeFrom,
  requestAccessMailto,
  validateInviteEmail,
  workspaceDomainFromEmail,
  type Day1InviteRole,
  type InviteOutcome,
} from "./memberViewModel";
import "./members.css";

export interface MembersRouteProps {
  apiClient: ApiClient;
  userLabel: string;
  /**
   * Interface contract with the phase-4 sibling `E14/F03/US02/T01` (this task's own header comment):
   * that task adds `workspaceId` to `WorkspaceShellAppProps` and passes both `workspaceId` and
   * `role` into this component -- neither is threaded via a client store this route reads itself
   * (no `loadCurrentWorkspace()` call here; ADR-012 w14 footer: "a client store never stands in for
   * a missing GET"). Optional only so this file keeps compiling against `WorkspaceShellApp.tsx`'s
   * call site before that same-phase, no-`depends_on` sibling lands in the same worktree -- `undefined`
   * degrades to the same "no workspace" state a genuinely absent workspace already renders, never a
   * silently reintroduced session read.
   */
  workspaceId?: string;
  /** Optional bonus: `WorkspaceShellAppProps` already carries a workspace name one level up and it
   * costs the sibling nothing extra to also thread it here, but the interface contract only promises
   * `workspaceId` + `role`, so every render path below works correctly without it too (see
   * `formatWorkspaceLine`). */
  workspaceName?: string;
  role?: WorkspaceRole;
}

type RosterState =
  | { phase: "loading" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; members: readonly WorkspaceMemberBody[] };

/**
 * Route `/workspace/members` -- Workspace & members, V2 (ADR-024 V2 IA; screens-v2.md #10;
 * `raffa-v2/markup.html` "WORKSPACE & MEMBERS" block). Header ("Setup" kicker · "Workspace &
 * members" · the tenant meta line), the `kbOff` tip while nothing is validated yet, then the
 * two-column body: members table left, "Invite a colleague" (Admin) or a real `mailto:` request
 * (Procurement, read-only) right.
 *
 * Task E15/F02/US01/T01 (wave w14; closes NW-04 web / the NW-58 members-screen half): the roster is
 * now a server read (`getWorkspaceMembers`), re-read after every invite/revoke/remove -- never an
 * optimistic local mutation -- and the invite result renders the server's own `mailDelivered` fact
 * instead of asserting delivery unconditionally on a 201. The deleted per-browser echo module used to
 * hold both: this browser's own per-tab record of invites made in this tab (so a reload, another
 * Admin or another browser saw a different roster than Postgres held) and two invented facts (a "last
 * active now" timestamp manufactured for every row, and a hardcoded self-row Admin role) that are not
 * back-filled here -- the server has no such column, so this screen simply does not render one.
 */
export default function MembersRoute({ apiClient, userLabel, workspaceId, workspaceName, role }: MembersRouteProps) {
  const shell = useShellContext();
  // Pre-merge, this route is only ever reached through `RequireRole role={role} allow="admin"`
  // (WorkspaceShellApp.tsx, unmodified until the sibling above lands), so an absent `role` prop is
  // always the Admin case in practice; post-merge the sibling always supplies the real, server-derived
  // value and this fallback never applies.
  const effectiveRole: WorkspaceRole = role ?? "admin";
  const isAdmin = canManageMembers(effectiveRole);

  const [roster, setRoster] = useState<RosterState>({ phase: "loading" });
  const [email, setEmail] = useState("");
  const [inviteRole, setInviteRole] = useState<Day1InviteRole>("Procurement");
  const [inviteError, setInviteError] = useState<string | null>(null);
  const [inviteFailure, setInviteFailure] = useState<InviteFailure | null>(null);
  const [outcome, setOutcome] = useState<InviteOutcome | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [pendingActionId, setPendingActionId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<MembersActionError | null>(null);

  const tenantDomain = workspaceDomainFromEmail(userLabel);

  const loadRoster = useCallback(() => {
    if (!workspaceId) return;
    setRoster({ phase: "loading" });
    void apiClient.getWorkspaceMembers(workspaceId).then((result) => {
      if (!result.ok || !result.members) {
        setRoster({
          phase: "error",
          statusCode: result.statusCode,
          message: result.error ?? "The roster could not be loaded.",
        });
        return;
      }
      setRoster({ phase: "ready", members: result.members });
    });
  }, [apiClient, workspaceId]);

  useEffect(() => {
    loadRoster();
  }, [loadRoster]);

  const sendInvite = useCallback(() => {
    if (!workspaceId) return;

    setOutcome(null);
    setInviteFailure(null);
    const validationError = validateInviteEmail(email);
    if (validationError !== null) {
      setInviteError(validationError);
      return;
    }

    setSubmitting(true);
    setInviteError(null);
    const invitedEmail = email.trim();

    void apiClient.inviteWorkspaceMember(workspaceId, { email: invitedEmail, role: inviteRole }).then((result) => {
      setSubmitting(false);

      // Task E17/F02/US01/T01: the declared 502 -- the directory would not provision the guest and
      // NO invitation exists. The server's closed-set reason selects the copy; its prose never
      // reaches the pane.
      if (result.statusCode === 502 && result.failureReason !== null) {
        setInviteFailure({ failureReason: result.failureReason, email: invitedEmail });
        return;
      }

      if (!result.ok || !result.member) {
        // A 400/409 carries the server's own validation sentence; nothing about a mail is asserted.
        setInviteError(result.error ?? "The invitation could not be created.");
        return;
      }

      setEmail("");
      setInviteError(null);
      // The pane branches on the 201's own `deliveryOutcome` string and renders its own
      // `identityProvisioned` boolean -- it infers neither (ADR-020 w15 §3.8).
      setOutcome(inviteOutcomeFrom(result.member));
      // The roster is a re-read, never an optimistic append (AC-1/N3): the new row (and its real
      // server-derived status) comes back the same way every other change to it does.
      loadRoster();
    });
  }, [apiClient, email, inviteRole, workspaceId, loadRoster]);

  const handleRevoke = useCallback(
    (invitationId: string) => {
      if (!workspaceId) return;
      setActionError(null);
      setPendingActionId(invitationId);
      void apiClient.revokeInvitation(workspaceId, invitationId).then((result) => {
        setPendingActionId(null);
        if (!result.ok) {
          setActionError({ id: invitationId, message: result.error ?? "The invitation could not be revoked." });
          return;
        }
        loadRoster();
      });
    },
    [apiClient, workspaceId, loadRoster],
  );

  const handleRemove = useCallback(
    (membershipId: string, isSelf: boolean) => {
      if (!workspaceId) return;
      setActionError(null);
      setPendingActionId(membershipId);
      void apiClient.removeMember(workspaceId, membershipId).then((result) => {
        if (!result.ok) {
          setPendingActionId(null);
          setActionError({ id: membershipId, message: result.error ?? "The member could not be removed." });
          return;
        }
        if (isSelf) {
          // ADR-020 w14 design footer (screen 10, D-58.7): "on success the app returns to /signin,
          // because it must not sit in a shell for a tenant the user no longer belongs to." This
          // route's own interface contract with E14/F03/US02/T01 carries no onSignOut/router path (only
          // workspaceId + role), and it must not call the deleted-in-spirit per-tab storage helpers to
          // improvise one -- a full reload re-runs the app's own workspace resolution from scratch
          // (NW-03's revalidation rule), the same "hard navigation, no router context assumed" pattern
          // `WorkspacePickerScreen.tsx`'s own "Continue to <workspace> ->" link already uses.
          window.location.assign("/");
          return;
        }
        setPendingActionId(null);
        loadRoster();
      });
    },
    [apiClient, workspaceId, loadRoster],
  );

  if (!workspaceId) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before managing members.</p>
      </div>
    );
  }

  const members = roster.phase === "ready" ? roster.members : [];
  const adminEmails = members.filter((candidate) => candidate.status === "Active" && candidate.role === "Admin").map((candidate) => candidate.email);

  return (
    <div className="members-screen">
      <header className="screen-header">
        <div>
          <p className="screen-kicker">Setup</p>
          <h2 className="screen-title">Workspace &amp; members</h2>
          <p className="screen-header-summary">{formatWorkspaceLine(workspaceId, workspaceName)}</p>
        </div>
      </header>

      {shell?.kbReady === false && (
        <p className="members-tip" role="note">
          {MEMBERS_TIP}
        </p>
      )}

      <div className="members-body">
        <div className="members-table-column">
          {roster.phase === "loading" && (
            <div className="members-skeleton" role="status" aria-live="polite">
              <p className="micro-meta">Loading members…</p>
              {Array.from({ length: 3 }, (_, index) => (
                <div key={index} className="skeleton members-skeleton-row" />
              ))}
            </div>
          )}

          {roster.phase === "error" && (
            <div className="error-state" role="alert">
              <h4>Members unavailable</h4>
              <p className="micro-meta">
                {roster.message}
                {roster.statusCode !== null && ` (HTTP ${roster.statusCode})`}
              </p>
              <button type="button" className="btn btn-secondary" onClick={loadRoster}>
                Retry
              </button>
            </div>
          )}

          {roster.phase === "ready" && (
            <MembersTable
              members={roster.members}
              currentUserEmail={userLabel}
              canManage={isAdmin}
              pendingActionId={pendingActionId}
              actionError={actionError}
              onRevoke={handleRevoke}
              onRemove={handleRemove}
            />
          )}
        </div>

        {isAdmin ? (
          <InvitePane
            email={email}
            role={inviteRole}
            tenantDomain={tenantDomain}
            error={inviteError}
            failure={inviteFailure}
            domainWarning={inviteDomainWarning(email, tenantDomain, inviteRole)}
            outcome={outcome}
            submitting={submitting}
            onEmailChange={(value) => {
              setEmail(value);
              setOutcome(null);
              setInviteError(null);
              setInviteFailure(null);
            }}
            onRoleChange={setInviteRole}
            onSubmit={sendInvite}
          />
        ) : (
          <RequestAccessPane adminEmails={adminEmails} workspaceName={workspaceName} />
        )}
      </div>
    </div>
  );
}

/**
 * The Procurement variant (AC-9; ADR-020 w14 footer D-58.8): the roster stays visible and read-only
 * (rendered above, with no `Actions` column), and this pane replaces the invite form with the
 * explanation block plus a real `mailto:` to the workspace's now-knowable Admins -- resolving the
 * contradiction between `markup.html:403` (hides the grid outright) and ADR-018 `:107-108` /
 * `ia-v2.md` (read-only) in favour of the latter, now that NW-04 makes a real roster available. "A
 * dead button is the one option that is not available" (D-58.8), so the affordance is omitted rather
 * than rendered inert when no live Admin is on the roster yet.
 */
function RequestAccessPane({ adminEmails, workspaceName }: { adminEmails: readonly string[]; workspaceName?: string }) {
  const mailto = requestAccessMailto(adminEmails, workspaceName);
  return (
    <aside className="members-invite-pane" aria-label="Request access">
      <h4>You don&apos;t manage this workspace</h4>
      <p className="micro-meta">Only a Workspace Admin can invite, revoke or remove members. Ask one of your workspace&apos;s Admins for access.</p>
      {mailto !== null && (
        <a className="btn btn-secondary" href={mailto}>
          Request access
        </a>
      )}
    </aside>
  );
}
