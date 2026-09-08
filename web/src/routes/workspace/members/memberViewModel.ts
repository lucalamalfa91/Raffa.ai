/**
 * Pure helpers for the members + invite screen (route `/workspace/members`, ADR-018;
 * screens.md #2 "Members & roles"; task E06/F04/US01/T01).
 *
 * Day-1 roles are Admin vs Procurement only (ADR-018 "Roles (Day-1)"). The backend
 * `InviteRequest.Role` also accepts Legal/Finance/ReadOnly; this screen never offers those.
 */

export type Day1InviteRole = "Admin" | "Procurement";

export const INVITE_ROLE_LABEL: Record<Day1InviteRole, string> = {
  Admin: "Workspace Admin",
  Procurement: "Procurement",
};

export const INVITE_ROLE_SUMMARY: Record<Day1InviteRole, string> = {
  Admin: "All routes, including member management and invites.",
  Procurement: "All routes except member management. Cannot invite or change roles.",
};

export function workspaceDomainFromEmail(email: string): string | null {
  const at = email.lastIndexOf("@");
  if (at <= 0 || at === email.length - 1) return null;
  const domain = email.slice(at + 1).trim().toLowerCase();
  return domain.length > 0 ? domain : null;
}

/**
 * Client-side invite validation (screens.md #2: "validation error (non-tenant domain)").
 * There is no tenant-domain field on `WorkspaceTenant` -- the honest proxy is the signed-in
 * Admin's own email domain. Format errors are distinct from the domain mismatch so the named
 * UX state stays reachable without inventing a backend rule.
 */
export function validateInviteEmail(email: string, tenantDomain: string | null): string | null {
  const trimmed = email.trim();
  if (trimmed.length === 0) return "An email is required.";
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmed)) return "Enter a valid email address.";

  if (tenantDomain !== null) {
    const inviteDomain = workspaceDomainFromEmail(trimmed);
    if (inviteDomain !== tenantDomain) {
      return `Invite addresses must use this workspace's domain (@${tenantDomain}).`;
    }
  }

  return null;
}

export function memberRoleLabel(role: string): string {
  if (role === "Admin") return INVITE_ROLE_LABEL.Admin;
  if (role === "Procurement") return INVITE_ROLE_LABEL.Procurement;
  return role;
}
