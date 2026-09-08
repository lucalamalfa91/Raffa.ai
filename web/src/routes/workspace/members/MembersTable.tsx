import { memberRoleLabel } from "./memberViewModel";
import type { WorkspaceMemberRow } from "./memberStore";

export interface MembersTableProps {
  members: readonly WorkspaceMemberRow[];
}

function statusTagClass(status: WorkspaceMemberRow["status"]): string {
  return status === "Invited" ? "tag tag-outline" : "tag tag-neutral";
}

function formatLastActive(lastActiveAt: string | null): string {
  if (lastActiveAt === null) return "—";
  return "This session";
}

/**
 * AC-1 columns quoted from screens.md #2: "Table Member / Role / Status / Last active."
 * Uses the shared `.table` class (ADR-019); no new table styling.
 */
export default function MembersTable({ members }: MembersTableProps) {
  return (
    <table className="table members-table">
      <thead>
        <tr>
          <th scope="col">Member</th>
          <th scope="col">Role</th>
          <th scope="col">Status</th>
          <th scope="col">Last active</th>
        </tr>
      </thead>
      <tbody>
        {members.map((member) => (
          <tr key={member.id}>
            <td>{member.email}</td>
            <td>{memberRoleLabel(member.role)}</td>
            <td>
              <span className={statusTagClass(member.status)}>{member.status}</span>
            </td>
            <td>{formatLastActive(member.lastActiveAt)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
