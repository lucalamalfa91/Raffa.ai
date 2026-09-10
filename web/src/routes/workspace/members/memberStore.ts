/**
 * Client-side record of members this browser has seen in the current workspace
 * (screens.md #2 "Members table"; story us-01-workspace-members-invite AC-1).
 *
 * Why this is client-side, not server-queried: there is no backend endpoint that lists
 * members. `WorkspaceEndpointExtensions` maps only `POST /api/workspaces` and
 * `POST /api/workspaces/{tenantId}/invites` -- no `GET`. Every invited row here is a real
 * membership this browser created via `ApiClient.inviteWorkspaceMember`; the current Admin
 * row is the same client-side inference `workspaceStore.ts` already makes (whoever created
 * the workspace in this browser is its Admin). Discovery gap, not fabricated data: another
 * device's invites are invisible here until a list endpoint exists.
 */

export type MemberStatus = "Active" | "Invited";

export interface WorkspaceMemberRow {
  id: string;
  email: string;
  role: string;
  status: MemberStatus;
  /** ISO timestamp for Active rows; `null` for Invited (never signed in). */
  lastActiveAt: string | null;
}

const MEMBERS_KEY_PREFIX = "raffa.workspace.members.";

function selfMemberId(workspaceId: string): string {
  return `self:${workspaceId}`;
}

function storageKey(workspaceId: string): string {
  return `${MEMBERS_KEY_PREFIX}${workspaceId}`;
}

function readMembers(storage: Storage, workspaceId: string): WorkspaceMemberRow[] {
  const raw = storage.getItem(storageKey(workspaceId));
  if (!raw) return [];
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as WorkspaceMemberRow[]) : [];
  } catch {
    return [];
  }
}

function writeMembers(storage: Storage, workspaceId: string, members: readonly WorkspaceMemberRow[]): void {
  storage.setItem(storageKey(workspaceId), JSON.stringify(members));
}

/**
 * Members for this workspace, with the current signed-in Admin always present as the first
 * Active row. Re-running this after an invite keeps previously invited rows.
 */
export function loadWorkspaceMembers(
  workspaceId: string,
  currentUserEmail: string,
  storage: Storage = window.sessionStorage,
): WorkspaceMemberRow[] {
  const existing = readMembers(storage, workspaceId);
  const selfId = selfMemberId(workspaceId);
  const withoutSelf = existing.filter((row) => row.id !== selfId);
  const self: WorkspaceMemberRow = {
    id: selfId,
    email: currentUserEmail,
    role: "Admin",
    status: "Active",
    lastActiveAt: new Date().toISOString(),
  };
  const next = [self, ...withoutSelf];
  writeMembers(storage, workspaceId, next);
  return next;
}

export function rememberInvitedMember(
  workspaceId: string,
  member: WorkspaceMemberRow,
  storage: Storage = window.sessionStorage,
): WorkspaceMemberRow[] {
  const existing = readMembers(storage, workspaceId);
  const withoutThisEmail = existing.filter(
    (row) => row.email.toLowerCase() !== member.email.toLowerCase() || row.id === selfMemberId(workspaceId),
  );
  const next = [...withoutThisEmail, member];
  writeMembers(storage, workspaceId, next);
  return next;
}
