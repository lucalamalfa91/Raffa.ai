import { getMemberStatusTag, memberRoleLabel } from "./memberViewModel";
import type { WorkspaceMemberRow } from "./memberStore";

export interface MembersTableProps {
  members: readonly WorkspaceMemberRow[];
  /** The signed-in user's own label (`../../../components/shell/WorkspaceShellApp.tsx` `account.username`) -- marks their own row "You". */
  currentUserEmail: string;
}

/**
 * The V2 members table (screens-v2.md #10; `contigo-v2/markup.html` "WORKSPACE & MEMBERS" block):
 * Member (primary line bold, secondary line muted) · Role (22%) · Status (16%, small tag),
 * `font-size:13px`. The prototype's primary line is a display name; the backend stores no name for
 * a member (`InviteRequest` carries email + role only), so the email is the primary line and the
 * only secondary line shown is the honest "You" on the signed-in row -- never a name derived from
 * the address.
 */
export default function MembersTable({ members, currentUserEmail }: MembersTableProps) {
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
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
