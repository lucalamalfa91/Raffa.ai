/**
 * Who is looking at the case.
 *
 * Responsibilities (approver, owner, delegate, collaborator) belong to the *case*; this only decides
 * which person the signed-in user is on it. Until the backend maps accounts to people, an account that
 * is not on the case can pick one of the demo people with `?as=`, and otherwise sees the case as its
 * approver (who can do everything), so the demo is never stuck behind a permission.
 */

export interface ViewerInput {
  /** The signed-in account's label (`WorkspaceShellApp` `userLabel`). */
  userLabel: string;
  /** `?as=` -- demo only. */
  as: string | null;
  members: readonly string[];
  approver: string;
}

export function resolveViewer({ userLabel, as, members, approver }: ViewerInput): string {
  if (members.includes(userLabel)) return userLabel;
  if (as && members.includes(as)) return as;
  return approver;
}
