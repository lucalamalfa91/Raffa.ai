import { useState } from "react";
import type { WorkspaceMemberBody } from "../../../api/client";
import { getMemberStatusTag, isLastActiveAdmin, memberRoleLabel, removeConsequence, revokeConsequence } from "./memberViewModel";

export interface MembersActionError {
  /** The roster row id (invitation id for `Invited`, membership id for `Active`) the error belongs
   * to -- scoped so a stale failure never survives once the roster re-reads onto a different row. */
  id: string;
  message: string;
}

export interface MembersTableProps {
  members: readonly WorkspaceMemberBody[];
  /** The signed-in user's own label (`../../../components/shell/WorkspaceShellApp.tsx` `account.username`) -- marks their own row "You". */
  currentUserEmail: string;
  /**
   * Admin-only `Actions` column (AC-6/AC-9). A display-only decision -- the real gate is the
   * server's own role check on revoke/remove (ADR-025), and `role` reaching this component is
   * already server-derived (never a client-declared header), so this flag does not itself widen or
   * narrow what a caller can do.
   */
  canManage: boolean;
  /** The roster row id whose revoke/remove request is in flight -- disables that row's own buttons only. */
  pendingActionId: string | null;
  actionError: MembersActionError | null;
  onRevoke: (invitationId: string) => void;
  /** `isSelf` lets the caller apply D-58.7's post-removal redirect only to the signed-in Admin's own row. */
  onRemove: (membershipId: string, isSelf: boolean) => void;
}

/**
 * The V2 members table (screens-v2.md #10; `raffa-v2/markup.html` "WORKSPACE & MEMBERS" block):
 * Member (primary line bold, secondary line muted) · Role (22%) · Status (16%, small tag),
 * `font-size:13px`. The prototype's primary line is a display name; the backend stores no name for
 * a member (`InviteRequest` carries email + role only), so the email is the primary line and the
 * only secondary line shown is the honest "You" on the signed-in row -- never a name derived from
 * the address.
 *
 * Task E15/F02/US01/T01 (wave w14) adds a fourth `Actions` column the export does not have
 * (`markup.html:388` is three columns), Admin-only, with two destructive affordances confirmed
 * **inline in the row** -- never a dialog (`--shadow-*` is "dialogs only", ADR-019 `:78`) -- per
 * ADR-020's w14 design footer (screen 10, D-58.5/D-58.6/D-58.7).
 */
export default function MembersTable({ members, currentUserEmail, canManage, pendingActionId, actionError, onRevoke, onRemove }: MembersTableProps) {
  const [confirmingId, setConfirmingId] = useState<string | null>(null);

  return (
    <table className="table members-table">
      <thead>
        <tr>
          <th scope="col">Member</th>
          <th scope="col" className="members-col-role">
            Role
          </th>
          <th scope="col" className="members-col-status">
            Status
          </th>
          {canManage && (
            <th scope="col" className="members-col-actions">
              Actions
            </th>
          )}
        </tr>
      </thead>
      <tbody>
        {members.map((member) => {
          const statusTag = getMemberStatusTag(member.status);
          const isSelf = member.email.toLowerCase() === currentUserEmail.toLowerCase();
          return (
            <tr key={member.id}>
              <td>
                <div className="members-member-primary">{member.email}</div>
                {isSelf && <div className="members-member-secondary">You</div>}
              </td>
              <td>{memberRoleLabel(member.role)}</td>
              <td>
                <span className={`tag tag-${statusTag.variant} members-status-tag`}>{statusTag.label}</span>
              </td>
              {canManage && (
                <td className="members-actions-cell">
                  <MemberActions
                    member={member}
                    members={members}
                    isSelf={isSelf}
                    pending={pendingActionId === member.id}
                    error={actionError?.id === member.id ? actionError.message : null}
                    confirming={confirmingId === member.id}
                    onRequestConfirm={() => setConfirmingId(member.id)}
                    onCancelConfirm={() => setConfirmingId(null)}
                    onRevoke={() => onRevoke(member.id)}
                    onRemove={() => onRemove(member.id, isSelf)}
                  />
                </td>
              )}
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

interface MemberActionsProps {
  member: WorkspaceMemberBody;
  members: readonly WorkspaceMemberBody[];
  isSelf: boolean;
  pending: boolean;
  error: string | null;
  confirming: boolean;
  onRequestConfirm: () => void;
  onCancelConfirm: () => void;
  onRevoke: () => void;
  onRemove: () => void;
}

function MemberActions({ member, members, isSelf, pending, error, confirming, onRequestConfirm, onCancelConfirm, onRevoke, onRemove }: MemberActionsProps) {
  if (member.status === "Invited") {
    if (confirming) {
      const { question, detail } = revokeConsequence(member.email);
      return (
        <ConfirmBlock question={question} detail={detail} confirmLabel="Yes, revoke" pendingLabel="Revoking…" pending={pending} error={error} onConfirm={onRevoke} onCancel={onCancelConfirm} />
      );
    }
    return (
      <button type="button" className="btn btn-ghost" onClick={onRequestConfirm}>
        Revoke
      </button>
    );
  }

  if (member.status === "Active") {
    if (isLastActiveAdmin(members, member)) {
      return (
        <span className="members-action-disabled">
          <button type="button" className="btn btn-ghost" disabled>
            Remove
          </button>
          <span className="hint">This is the last Workspace Admin. Invite another Workspace Admin first.</span>
        </span>
      );
    }
    if (confirming) {
      const { question, detail } = removeConsequence(member.email, isSelf);
      return (
        <ConfirmBlock question={question} detail={detail} confirmLabel="Yes, remove" pendingLabel="Removing…" pending={pending} error={error} onConfirm={onRemove} onCancel={onCancelConfirm} />
      );
    }
    return (
      <button type="button" className="btn btn-ghost" onClick={onRequestConfirm}>
        Remove
      </button>
    );
  }

  return null;
}

interface ConfirmBlockProps {
  question: string;
  detail: string;
  confirmLabel: string;
  pendingLabel: string;
  pending: boolean;
  error: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

/** The two-step inline confirm every destructive row action shares (ADR-020 w14 footer D-58.5:
 * "the `Actions` cell swaps to the question, two buttons, and the consequence line"). */
function ConfirmBlock({ question, detail, confirmLabel, pendingLabel, pending, error, onConfirm, onCancel }: ConfirmBlockProps) {
  return (
    <div className="members-action-confirm">
      <p className="members-action-question">{question}</p>
      <p className="micro-meta">{detail}</p>
      <div className="members-action-buttons">
        <button type="button" className="btn btn-secondary" disabled={pending} onClick={onConfirm}>
          {pending ? pendingLabel : confirmLabel}
        </button>
        <button type="button" className="btn btn-ghost" disabled={pending} onClick={onCancel}>
          Cancel
        </button>
      </div>
      {error !== null && (
        <p className="hint" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
