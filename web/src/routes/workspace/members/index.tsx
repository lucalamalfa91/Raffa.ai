import { useCallback, useState } from "react";
import type { ApiClient } from "../../../api/client";
import { loadCurrentWorkspace } from "../../signin/workspaceStore";
import InvitePane from "./InvitePane";
import MembersTable from "./MembersTable";
import { loadWorkspaceMembers, rememberInvitedMember } from "./memberStore";
import {
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
 * Route `/workspace/members` (ADR-018; screens.md #2 "Members & roles"; ADR-020 screen 2;
 * task E06/F04/US01/T01, us-01-workspace-members-invite AC-1/AC-2). Wired into
 * `../../../components/shell/WorkspaceShellApp.tsx` in place of that shell task's
 * `ScaffoldScreen` placeholder. AC-3 (non-admin "You don't manage this workspace") stays on
 * `RequireRole` around this route -- this component only renders for Workspace Admin.
 *
 * Invite is a real `POST /api/workspaces/{tenantId}/invites`. The table is session-local
 * (see `memberStore.ts`) because no list-members endpoint exists yet.
 */
export default function MembersRoute({ apiClient, userLabel }: MembersRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [members, setMembers] = useState(() =>
    workspace ? loadWorkspaceMembers(workspace.id, userLabel) : [],
  );
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Day1InviteRole>("Procurement");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const sendInvite = useCallback(() => {
    if (!workspace) return;

    const validationError = validateInviteEmail(email, workspaceDomainFromEmail(userLabel));
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
    });
  }, [apiClient, email, role, userLabel, workspace]);

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
      <p className="screen-kicker">R0</p>
      <h2 className="screen-title">Workspace & members</h2>
      <p className="micro-meta">Members, roles, and invitations for this workspace.</p>

      <div className="members-body">
        <div className="members-table-column">
          <MembersTable members={members} />
        </div>
        <InvitePane
          email={email}
          role={role}
          error={error}
          submitting={submitting}
          onEmailChange={setEmail}
          onRoleChange={setRole}
          onSubmit={sendInvite}
        />
      </div>
    </div>
  );
}
