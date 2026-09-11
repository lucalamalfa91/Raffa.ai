import type { WorkspaceRole } from "./navItems";

/**
 * Task E14/F03/US02/T01 (wave w14 "workspace is real"; ADR-012 w14 footer
 * clause 5, ADR-025 §E, ADR-026 w14 footer clause 3): the role is now a
 * **server** fact -- `GET /api/workspaces`'s row carries it
 * (`WorkspaceSummaryBody.role`, `src/api/client.ts`) -- so this module's job
 * changes from *inventing* a role (see git history for the deleted
 * `resolveWorkspaceRole()`, whose `?role=` query override and
 * `sessionStorage` mirror defaulted to `"admin"` for whoever was looking --
 * exactly NW-14's own defect) to *parsing the wire value the server sends*.
 *
 * `resolveWorkspaceRole()` is deleted as the product path, both halves: the
 * query override and the `"admin"`-defaulting `sessionStorage` mirror. There
 * is nothing left for a `?role=` URL parameter to do once the server -- not
 * this browser -- decides who is Admin.
 */

/**
 * The wire vocabulary is wider than the nav's: the backend also accepts
 * `Legal` / `Finance` / `ReadOnly` (`memberViewModel.ts:9-11`) while
 * `navItems.ts:29`'s `WorkspaceRole` models only `admin` | `procurement`
 * (NW-54, deferred). Neither ADR-025 nor ADR-026 assigns those three any
 * affordance, so the enum is unspecified from this module's point of view --
 * only the *mapping rule* is specified: **any wire value this nav does not
 * model degrades to the least-privileged modelled role, and never to
 * `"admin"`.** Carrying the old default (`?? "admin"`) forward would
 * re-introduce this exact bug for Legal/Finance/ReadOnly members while fixing
 * it for Procurement -- an unmodelled role must never gain more than the
 * least-privileged modelled affordance set.
 *
 * The role decides which affordances *render* and never what is *permitted*:
 * the 403 an unauthorized write still gets from the API is the authority,
 * this function's output is only a courtesy that keeps the UI honest about
 * it.
 */
export function parseWorkspaceRole(wire: string): WorkspaceRole {
  return wire === "Admin" ? "admin" : "procurement";
}

/**
 * The display counterpart to `parseWorkspaceRole` above, and a *different*
 * key space on purpose (ADR-019 w14 footer clause 5): permissions degrade to
 * least privilege, but a person's own label never does. `memberViewModel.ts`'s
 * `memberRoleLabel` already establishes this exact shape for the members
 * table (map the two modelled wire values to their display strings; pass any
 * other wire value straight through, verbatim); this is the workspace
 * picker's own copy of the same rule, duplicated rather than imported for the
 * same reason `useValidatedContractCount.ts`'s own header comment gives for
 * its duplicated status predicate -- two independent, separately-evolving
 * screens, one small pure rule, not worth a shared module. The two functions
 * must keep emitting the identical two strings for `"Admin"`/`"Procurement"`;
 * they are one vocabulary read through two call sites.
 */
export function workspaceRoleLabel(wire: string): string {
  if (wire === "Admin") return "Workspace Admin";
  if (wire === "Procurement") return "Procurement";
  return wire;
}
