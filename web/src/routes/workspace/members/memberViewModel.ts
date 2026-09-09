import type { SemanticTag } from "../../../styles/semantics";
import type { MemberStatus } from "./memberStore";

/**
 * Pure helpers for the Workspace & members screen (route `/workspace/members`; ADR-024 V2 IA;
 * screens-v2.md #10; `contigo-v2/markup.html` "WORKSPACE & MEMBERS" block). No React here -- every
 * rule is unit-testable without rendering (`memberViewModel.test.ts`).
 *
 * Roles are Admin vs Procurement only (ADR-018 "Roles (Day-1)"; requirements D8 / R-WEB-07). The
 * backend `InviteRequest.Role` also accepts Legal/Finance/ReadOnly; this screen never offers those.
 */

export type Day1InviteRole = "Admin" | "Procurement";

/** Radio order quoted from the prototype: Procurement first (the default), Workspace Admin second. */
export const INVITE_ROLE_ORDER: readonly Day1InviteRole[] = ["Procurement", "Admin"];

export const INVITE_ROLE_LABEL: Record<Day1InviteRole, string> = {
  Admin: "Workspace Admin",
  Procurement: "Procurement",
};

/**
 * One-line permission summaries under each radio. The prototype reads "Asks, reviews, triages
 * renewals" / "Also uploads, deletes, manages members"; requirements decision D8 ("Who uploads:
 * Admin and Procurement. Only Admin deletes documents and manages members") and R-WEB-07 win over
 * the prototype, so upload moves to the Procurement line and the Admin line keeps only what is
 * really Admin-only.
 */
export const INVITE_ROLE_SUMMARY: Record<Day1InviteRole, string> = {
  Procurement: "Asks, uploads, reviews, triages renewals",
  Admin: "Also deletes documents and manages members",
};

/** `markup.html` `kbOff` tip, verbatim. Shown while the knowledge base has no validated contract yet. */
export const MEMBERS_TIP = "Tip: invite the team once the first contract is validated — there is nothing for them to ask before that.";

/** `markup.html` `inviteSent`, verbatim. */
export const INVITATION_SENT_MESSAGE = "Invitation sent.";

/** Header meta line: "{{ wsName }} · tenant {{ id }}" -- the real `X-Tenant-Id`, never a made-up code like the prototype's HF-CH-001. */
export function formatWorkspaceLine(workspaceName: string, tenantId: string): string {
  return `${workspaceName} · tenant ${tenantId}`;
}

export function workspaceDomainFromEmail(email: string): string | null {
  const at = email.lastIndexOf("@");
  if (at <= 0 || at === email.length - 1) return null;
  const domain = email.slice(at + 1).trim().toLowerCase();
  return domain.length > 0 ? domain : null;
}

/** "Work email" placeholder: `name@{{ domain }}` (prototype `name@helvetiafoods.ch`), a neutral one when no tenant domain is known. */
export function inviteEmailPlaceholder(tenantDomain: string | null): string {
  return tenantDomain !== null ? `name@${tenantDomain}` : "name@company.com";
}

/** `markup.html` `inviteError`: "Use an @helvetiafoods.ch address." -- with the real tenant domain. */
export function formatDomainError(tenantDomain: string): string {
  return `Use an @${tenantDomain} address.`;
}

/**
 * Client-side invite validation (screens-v2.md #10: "email must match the workspace domain"). There
 * is no tenant-domain field on `WorkspaceTenant` -- the honest proxy is the signed-in Admin's own
 * email domain. Format errors are distinct from the domain mismatch so the named UX state stays
 * reachable without inventing a backend rule.
 */
export function validateInviteEmail(email: string, tenantDomain: string | null): string | null {
  const trimmed = email.trim();
  if (trimmed.length === 0) return "An email is required.";
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmed)) return "Enter a valid email address.";

  if (tenantDomain !== null) {
    const inviteDomain = workspaceDomainFromEmail(trimmed);
    if (inviteDomain !== tenantDomain) {
      return formatDomainError(tenantDomain);
    }
  }

  return null;
}

export function memberRoleLabel(role: string): string {
  if (role === "Admin") return INVITE_ROLE_LABEL.Admin;
  if (role === "Procurement") return INVITE_ROLE_LABEL.Procurement;
  return role;
}

/** `app.jsx` members: Active rows `tag-neutral`, a freshly invited row `tag-accent`. Text carries the meaning, the variant only adds emphasis. */
export function getMemberStatusTag(status: MemberStatus): SemanticTag {
  return status === "Invited" ? { variant: "accent", label: "Invited" } : { variant: "neutral", label: "Active" };
}
