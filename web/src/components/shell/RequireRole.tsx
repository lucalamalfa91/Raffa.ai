import type { ReactNode } from "react";
import type { WorkspaceRole } from "./navItems";

export interface RequireRoleProps {
  role: WorkspaceRole;
  allow: WorkspaceRole;
  children: ReactNode;
}

/**
 * Route-level defense in depth behind RailNav's hidden nav item (AC-2):
 * RailNav stops a Procurement user *clicking into* "Workspace & members",
 * but a direct URL visit (or the browser back button) must not silently
 * render the real screen either. ADR-018 "Roles (Day-1)": Procurement "sees
 * a 'request access' / read-only state on that surface" — never a blank
 * screen or a 404, since roles are "a permission gate, never a fork in the
 * IA".
 *
 * Copy is adapted from inputs/design/prototypes/day1-demo.html's own
 * non-admin state (`<h4>You don't manage this workspace</h4>`, "Only a
 * Workspace Admin can invite members or change roles...", a
 * `.btn.btn-secondary` "Request access" button). The prototype's sentence
 * also names two fixture admins by name ("Marta Keller, Jonas Frei"); this
 * app has no backend "list members" endpoint yet to look up a real
 * workspace's admins (see src/routes/signin/workspaceStore.ts), so that
 * clause is generalised instead of copied verbatim.
 */
export default function RequireRole({ role, allow, children }: RequireRoleProps) {
  if (role === allow) {
    return <>{children}</>;
  }

  return (
    <div className="empty-state" role="status">
      <h4>You don&apos;t manage this workspace</h4>
      <p className="micro-meta">
        Only a Workspace Admin can invite members or change roles. Ask your Workspace Admin for
        access.
      </p>
      <button type="button" className="btn btn-secondary">
        Request access
      </button>
    </div>
  );
}
