/**
 * Contigo — left-rail navigation model (ADR-018 web information architecture;
 * task E06/F03/US02/T01, parent story us-02-navigation-shell AC-1/AC-2).
 *
 * The eight rail items, their order, and their exact label text are locked
 * verbatim from the parent story's own AC-1 list
 * ("Home, Portfolio, Renewals, Ask (⌘K), Quote check, Documents, Review
 * queue, Workspace & members"), which matches
 * inputs/design/prototypes/ia.md's "Navigation (left rail)" section. Route
 * paths come from ia.md's "Route map" / ADR-018's locked route-map table.
 * Release tags are ia.md's per-item release column, rendered the same way
 * the compiled inputs/design/prototypes/day1-demo.html bundle renders an
 * accent-coloured `{{ n.badge }}` at the end of each nav row.
 *
 * Two paths are this task's own inference, not a row in the locked route
 * map — flagged individually below — because ia.md/ADR-018 only ever name a
 * *detail* route for these two nav destinations, never the list/landing
 * route the rail item itself must point at (the same list -> detail shape
 * already established by /contracts -> /contracts/:id):
 *   - "Review queue": ia.md/ADR-018 name only /contracts/:id/review (a single
 *     contract's field review). /review is the list landing for that detail
 *     (`src/routes/review/`, ReviewQueueRoute).
 *   - "Quote check": ia.md/ADR-018 name only /quotes/:id. /quotes is the
 *     upload landing for that detail (`src/routes/quotes/`).
 *
 * "Workspace & members" is the one adminOnly item (ADR-018 "Roles (Day-1)":
 * "Procurement — all routes except member management"); every other item is
 * visible to both Day-1 roles, matching ADR-018's "permission gate, never a
 * fork in the IA" and the council decision carried into the parent story
 * ("Roles are a permission gate, never an IA fork").
 */

/**
 * The two Day-1 nav-facing roles (ADR-018 "Roles (Day-1)"). This is
 * deliberately narrower than the backend's full five-value catalog
 * (Contigo.Identity.Workspace.Domain.WorkspaceRoleName: Admin / Procurement /
 * Legal / Finance / ReadOnly) — "Legal / Finance / Read-only exist in the
 * permission model ... but are not separate nav variants in V1" (ia.md).
 */
export type WorkspaceRole = "admin" | "procurement";

/** Display labels, matching the backend enum names and the compiled prototype's own `role:isAdmin?'Workspace Admin':'Procurement'` demo-data mapping (day1-demo.html). */
export const WORKSPACE_ROLE_LABEL: Record<WorkspaceRole, string> = {
  admin: "Workspace Admin",
  procurement: "Procurement",
};

export interface NavItem {
  /** Stable id for React keys/tests — not shown in the UI. */
  id: string;
  /** Exact label text, locked from the parent story's AC-1 list. */
  label: string;
  /** Route path this item links to (react-router, relative to the shell). */
  path: string;
  /** Release tag from ia.md's "Navigation (left rail)" list (R0–R4). */
  release: string;
  /** True only for "Workspace & members" (ADR-018 "Roles (Day-1)"). */
  adminOnly?: boolean;
}

export const NAV_ITEMS: readonly NavItem[] = [
  { id: "home", label: "Home", path: "/", release: "R3" },
  { id: "portfolio", label: "Portfolio", path: "/contracts", release: "R1" },
  { id: "renewals", label: "Renewals", path: "/renewals", release: "R2" },
  { id: "ask", label: "Ask (⌘K)", path: "/ask", release: "R1" },
  { id: "quote-check", label: "Quote check", path: "/quotes", release: "R4" },
  { id: "documents", label: "Documents", path: "/documents", release: "R0" },
  { id: "review-queue", label: "Review queue", path: "/review", release: "R1" },
  {
    id: "workspace-members",
    label: "Workspace & members",
    path: "/workspace/members",
    release: "R0",
    adminOnly: true,
  },
];

/**
 * AC-2 / the task's own required unit test ("role guard hides admin item for
 * Procurement"): the single, pure, testable place that decides which nav
 * items a role sees. RailNav.tsx renders exactly this list — it does not
 * re-derive visibility itself, and RequireRole.tsx (the route-level half of
 * the same guard) uses the same `WorkspaceRole` values.
 */
export function getVisibleNavItems(role: WorkspaceRole): readonly NavItem[] {
  return NAV_ITEMS.filter((item) => !item.adminOnly || role === "admin");
}
