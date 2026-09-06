import { NavLink } from "react-router-dom";
import { getVisibleNavItems, WORKSPACE_ROLE_LABEL, type WorkspaceRole } from "./navItems";

export interface RailNavProps {
  workspaceName: string;
  role: WorkspaceRole;
  userLabel: string;
  onSignOut: () => void;
}

/**
 * 224px left rail (ADR-018 Option 1 "single 224px left rail"; design-system.md
 * "Shell: 224px left rail (workspace name, nav with badges, admin link,
 * user) + main column"). Layout/measurements (224px grid column via
 * shell.css, 2px right divider, per-item accent release badge, footer
 * role+sign-out line) are confirmed against the compiled
 * inputs/design/prototypes/day1-demo.html bundle's `<nav>` block, not
 * invented; label text is the parent story's own AC-1 list verbatim (see
 * navItems.ts).
 *
 * Icons are deliberately omitted: design-system.md calls for Lucide inline
 * SVGs, but `lucide-react` (or an equivalent) is not yet a dependency of
 * this package and adding an icon library is outside this task's scope
 * (rail/guards/ask-bar) — a later task can add them without changing this
 * component's structure.
 */
export default function RailNav({ workspaceName, role, userLabel, onSignOut }: RailNavProps) {
  const items = getVisibleNavItems(role);

  return (
    <nav className="shell-rail" aria-label="Primary">
      <div className="shell-rail-header">
        <span className="shell-rail-kicker">Workspace</span>
        <div className="shell-rail-workspace-name">{workspaceName}</div>
      </div>

      <div className="shell-rail-nav">
        {items.map((item) => (
          <NavLink
            key={item.id}
            to={item.path}
            end={item.path === "/"}
            className={({ isActive }) => `shell-rail-item${isActive ? " is-active" : ""}`}
          >
            <span>{item.label}</span>
            <span className="shell-rail-badge">{item.release}</span>
          </NavLink>
        ))}
      </div>

      <div className="shell-rail-footer">
        <div className="shell-rail-user">{userLabel}</div>
        <div className="micro-meta">
          <span>{WORKSPACE_ROLE_LABEL[role]}</span> ·{" "}
          <button type="button" className="shell-rail-signout" onClick={onSignOut}>
            Sign out
          </button>
        </div>
      </div>
    </nav>
  );
}
