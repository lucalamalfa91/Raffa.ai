import { useCallback, useState } from "react";
import type { ApiClient } from "../../../api/client";
import { useShellContext } from "../../../components/shell/shellContext";
import { loadCurrentWorkspace } from "../../signin/workspaceStore";
import InvitePane from "./InvitePane";
import MembersTable from "./MembersTable";
import { loadWorkspaceMembers, rememberInvitedMember } from "./memberStore";
import {
  MEMBERS_TIP,
  formatWorkspaceLine,
  validateInviteEmail,
  workspaceDomainFromEmail,
  type Day1InviteRole,
} from "./memberViewModel";
import "./members.css";

export interface MembersRouteProps {
  apiClient: ApiClient;
  userLabel: string;
}

/**
 * Route `/workspace/members` -- Workspace & members, V2 (ADR-024 V2 IA; screens-v2.md #10;
 * `contigo-v2/markup.html` "WORKSPACE & MEMBERS" block). Header ("Setup" kicker · "Workspace &
 * members" · "{workspace} · tenant {id}"), the `kbOff` tip while nothing is validated yet, then the
 * two-column body: members table left, "Invite a colleague" right.
 *
 * The non-admin state ("You don't manage this workspace … Request access", R-WEB-07) stays on
 * `RequireRole` around this route -- this component only renders for Workspace Admin.
 *
 * Invite is a real `POST /api/workspaces/{tenantId}/invites`. The table is session-local (see
 * `memberStore.ts`) because no list-members endpoint exists yet -- a discovery gap, never fabricated
 * rows.
 */
export default function MembersRoute({ apiClient, userLabel }: MembersRouteProps) {
  const workspace = loadCurrentWorkspace();
  const shell = useShellContext();
  const [members, setMembers] = useState(() => (workspace ? loadWorkspaceMembers(workspace.id, userLabel) : []));
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Day1InviteRole>("Procurement");
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const tenantDomain = workspaceDomainFromEmail(userLabel);

  const sendInvite = useCallback(() => {
    if (!workspace) return;

    setSent(false);
    const validationError = validateInviteEmail(email, tenantDomain);
    if (validationError !== null) {
      setError(validationError);
      return;
    }

    setSubmitting(true);
    setError(null);

    void apiClient.inviteWorkspaceMember(workspace.id, { email: email.trim(), role }).then((result) => {
      setSubmitting(false);
      if (!result.ok || !result.member) {
        setError(result.error ?? "The invitation could not be sent.");
        return;
      }

      setMembers(
        rememberInvitedMember(workspace.id, {
          id: result.member.id,
          email: result.member.email,
          role: result.member.role,
          status: "Invited",
          lastActiveAt: null,
        }),
      );
      setEmail("");
      setError(null);
      setSent(true);
    });
  }, [apiClient, email, role, tenantDomain, workspace]);

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before managing members.</p>
      </div>
    );
  }

  return (
    <div className="members-screen">
      <header className="screen-header">
        <div>
          <p className="screen-kicker">Setup</p>
          <h2 className="screen-title">Workspace &amp; members</h2>
          <p className="screen-header-summary">{formatWorkspaceLine(workspace.name, workspace.id)}</p>
        </div>
      </header>

      {shell?.kbReady === false && (
        <p className="members-tip" role="note">
          {MEMBERS_TIP}
        </p>
      )}

      <div className="members-body">
        <div className="members-table-column">
          <MembersTable members={members} currentUserEmail={userLabel} />
        </div>
        <InvitePane
          email={email}
          role={role}
          tenantDomain={tenantDomain}
          error={error}
          sent={sent}
          submitting={submitting}
          onEmailChange={(value) => {
            setEmail(value);
            setSent(false);
            setError(null);
          }}
          onRoleChange={setRole}
          onSubmit={sendInvite}
        />
      </div>
    </div>
  );
}
