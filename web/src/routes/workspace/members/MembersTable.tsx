import { useState } from "react";
import type { WorkspaceMemberBody } from "../../../api/client";
import { getMemberStatusTag, memberRoleLabel } from "./memberViewModel";

export interface MembersTableProps {
  members: readonly WorkspaceMemberBody[];
  /** The signed-in user's own label (`../../../components/shell/WorkspaceShellApp.tsx` `userLabel`)
   * -- marks their own row "You" and drives the self-removal copy. */
  currentUserEmail: string;
  /** Server-derived (`canManageMembers(role)`, `navItems.ts`) -- gates the whole `Actions` column,
   * never a client-declared role (ADR-020 §10.4). */
  isAdmin: boolean;
  /** `member.id` of an `Invited` row -- see this file's own header comment on why that id is not
   * `WorkspaceInvitation.Id`. */
  onRevoke: (id: string) => void;
  /** `member.id` of an `Active` row -- see this file's own header comment on why that id is not
   * `WorkspaceMembership.Id`. */
  onRemove: (id: string) => void;
}

/**
 * The V2 members table (screens-v2.md #10; `raffa-v2/markup.html` "WORKSPACE & MEMBERS" block):
 * Member (primary line bold, secondary line muted) · Role (22%) · Status (16%, small tag), plus a
 * fourth Admin-only `Actions` column the export does not have (`markup.html:388` is three columns;
 * ADR-020 §10.4). The prototype's primary line is a display name; the backend stores no name for a
 * member, so the email is the primary line and the only secondary line shown is the honest "You" on
 * the signed-in row -- never a name derived from the address (ADR-020 §10.6).
 *
 * Two destructive affordances, both Admin-only, both confirmed **inline in the row** (`--shadow-*`
 * is "dialogs only", ADR-019 w14 clause 4; the locked catalogue has no dialog component): an
 * `Invited` row is **revoked**, an `Active` row is **removed**. They render different consequence
 * copy (ADR-020 §10.4's own table) because they are different facts -- revoking an invitation never
 * granted access, so it must never say "they lose access". The last Admin's `Remove` is a visibly
 * disabled control with a `.hint` (ADR-019 w14 clause 2's "a visible reason, not a hidden control"),
 * never a click that reaches the server's own 409.
 *
 * `index.tsx` owns the actual `apiClient.revokeInvitation`/`removeMember` calls and the re-read that
 * follows a success -- this component only renders the confirmation and bubbles the row id up
 * (`onRevoke`/`onRemove`), the same split `../../documents/DocumentStatusTable.tsx`'s own
 * `onDelete` callback already establishes for an identical inline-confirm shape.
 *
 * **A confirmed contract gap, not a defect in this file**: `GET /api/workspaces/{tenantId}/members`
 * returns `id = WorkspaceUser.Id` for every row -- stable across the Active/Invited transition by
 * design (`WorkspaceMembershipService.cs`'s own `WorkspaceMemberRecord` doc comment: "never the
 * membership or invitation row's own id, which would change on removal/re-invite"). But
 * `DELETE /api/workspaces/{tenantId}/invites/{id}` matches strictly on `WorkspaceInvitation.Id`
 * (`WorkspaceInvitationService.cs:243`) and `DELETE /api/workspaces/{tenantId}/members/{membershipId}`
 * matches strictly on `WorkspaceMembership.Id` (`WorkspaceMembershipService.cs:345`) -- two more,
 * independently generated ids the roster never exposes. No `web/src`-only fix exists (the roster is
 * this screen's only source of a row id, per ADR-012 w14 "a client store never stands in for a
 * missing GET" -- caching an id client-side from the invite response would reintroduce exactly that
 * anti-pattern). Wired here exactly as ADR-020 §10.4 specifies, using the roster row's own `id`; see
 * the IMPLEMENTER turn for the full citation trail and who owns the fix (out of this task's
 * `backend/**` boundary).
 */
export default function MembersTable({ members, currentUserEmail, isAdmin, onRevoke, onRemove }: MembersTableProps) {
  const [confirmingId, setConfirmingId] = useState<string | null>(null);

  const liveAdminCount = members.filter((member) => member.status === "Active" && member.role === "Admin").length;

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
          {isAdmin && (
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
          const isLastLiveAdmin = member.status === "Active" && member.role === "Admin" && liveAdminCount <= 1;
          const confirming = confirmingId === member.id;

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
              {isAdmin && (
                <td className="members-actions-cell">
                  {member.status === "Invited" &&
                    (confirming ? (
                      <div className="members-action-confirm">
                        <p className="members-action-question">Revoke the invitation for {member.email}?</p>
                        <p className="members-action-consequence">
                          Their link stops working. They never had access to this workspace.
                        </p>
                        <div className="members-action-buttons">
                          <button
                            type="button"
                            className="btn btn-secondary"
                            onClick={() => {
                              setConfirmingId(null);
                              onRevoke(member.id);
                            }}
                          >
                            Confirm revoke
                          </button>
                          <button type="button" className="btn btn-ghost" onClick={() => setConfirmingId(null)}>
                            Cancel
                          </button>
                        </div>
                      </div>
                    ) : (
                      <button type="button" className="btn btn-ghost" onClick={() => setConfirmingId(member.id)}>
                        Revoke
                      </button>
                    ))}

                  {member.status === "Active" &&
                    (isLastLiveAdmin ? (
                      <div className="members-action-disabled">
                        <button type="button" className="btn btn-ghost" disabled>
                          Remove
                        </button>
                        <p className="hint">This is the last Workspace Admin. Invite another Workspace Admin first.</p>
                      </div>
                    ) : confirming ? (
                      <div className="members-action-confirm">
                        <p className="members-action-question">
                          {isSelf ? "Remove yourself from this workspace?" : `Remove ${member.email}?`}
                        </p>
                        <p className="members-action-consequence">
                          {isSelf
                            ? "You will lose access immediately and will need a new invitation to return."
                            : "They lose access immediately. To bring them back you will need to send a new invitation."}
                        </p>
                        <div className="members-action-buttons">
                          <button
                            type="button"
                            className="btn btn-secondary"
                            onClick={() => {
                              setConfirmingId(null);
                              onRemove(member.id);
                            }}
                          >
                            Confirm remove
                          </button>
                          <button type="button" className="btn btn-ghost" onClick={() => setConfirmingId(null)}>
                            Cancel
                          </button>
                        </div>
                      </div>
                    ) : (
                      <button type="button" className="btn btn-ghost" onClick={() => setConfirmingId(member.id)}>
                        Remove
                      </button>
                    ))}
                </td>
              )}
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
