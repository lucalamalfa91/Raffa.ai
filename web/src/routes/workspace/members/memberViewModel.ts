import type { WorkspaceMemberBody } from "../../../api/client";
import type { SemanticTag } from "../../../styles/semantics";

/**
 * Pure helpers for the Workspace & members screen (route `/workspace/members`; ADR-024 V2 IA;
 * screens-v2.md #10; `raffa-v2/markup.html` "WORKSPACE & MEMBERS" block). No React here -- every
 * rule is unit-testable without rendering (`memberViewModel.test.ts`).
 *
 * Roles are Admin vs Procurement only (ADR-018 "Roles (Day-1)"; requirements D8 / R-WEB-07). The
 * backend `InviteRequest.Role` also accepts Legal/Finance/ReadOnly; this screen never offers those.
 *
 * Task E15/F02/US01/T01 (wave w14, NW-04 web / NW-58 members-screen half; ADR-020 w14 design
 * footer, screen 10; ADR-019 w14 footer): the roster and the invite outcome are now server facts
 * (`getWorkspaceMembers`, `inviteWorkspaceMember`'s `mailDelivered`) instead of the deleted
 * per-browser echo module's per-tab cache -- see `index.tsx`'s own header comment for the full
 * defect this closes.
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
 * really Admin-only. Unchanged by this task (w14 table: "no task should regress an accepted
 * decision" -- ADR-020 w14 footer 10.6/10.9).
 */
export const INVITE_ROLE_SUMMARY: Record<Day1InviteRole, string> = {
  Procurement: "Asks, uploads, reviews, triages renewals",
  Admin: "Also deletes documents and manages members",
};

/** `markup.html` `kbOff` tip, verbatim. Shown while the knowledge base has no validated contract yet.
 * Unchanged by this task -- no task should "improve" it into an always-on banner (w14 table). */
export const MEMBERS_TIP = "Tip: invite the team once the first contract is validated — there is nothing for them to ask before that.";

/** Header meta line. Task E15/F02/US01/T01's own interface contract with its phase-4 sibling
 * `E14/F03/US02/T01` threads only `workspaceId` + `role` into this route -- not a workspace name --
 * so this degrades to the tenant id alone unless a name is also available (it costs the sibling
 * nothing extra to pass one, since `WorkspaceShellAppProps` already carries it, so this stays ready
 * for it without depending on it). Still refuses the export's invented "HF-CH-001": the id is always
 * the real `X-Tenant-Id`. */
export function formatWorkspaceLine(tenantId: string, workspaceName?: string): string {
  return workspaceName ? `${workspaceName} · tenant ${tenantId}` : `tenant ${tenantId}`;
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

/**
 * Blocking format checks only -- a malformed or empty address is not an address (w14 table,
 * OQ-w14-005). The domain check that used to live here is no longer a block: see
 * `inviteDomainWarning` below.
 */
export function validateInviteEmail(email: string): string | null {
  const trimmed = email.trim();
  if (trimmed.length === 0) return "An email is required.";
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmed)) return "Enter a valid email address.";
  return null;
}

/**
 * Non-blocking domain warning (ADR-020 w14 design footer, screen 10; ADR-001 w14 footer: the
 * workspace-domain restriction is deferred to the wave that lands ADR-010). The client-side domain
 * check is a **typo guard, never a safeguard**: the server has no domain rule at all, and "workspace
 * domain" is a proxy read off whoever happens to be signed in (see `index.tsx`'s own call site) --
 * far too imprecise to *gate* an invitation, and perfectly adequate to *inform* one. Returns `null`
 * once the address does not parse to a domain (the blocking format check above owns that case), once
 * no tenant domain is known, or once the domains already agree.
 */
export function inviteDomainWarning(email: string, tenantDomain: string | null, role: Day1InviteRole): string | null {
  if (tenantDomain === null) return null;
  const trimmed = email.trim();
  const inviteDomain = workspaceDomainFromEmail(trimmed);
  if (inviteDomain === null || inviteDomain === tenantDomain) return null;
  return `${trimmed} is outside ${tenantDomain}. They will get full ${INVITE_ROLE_LABEL[role]} access to this workspace.`;
}

export function memberRoleLabel(role: string): string {
  if (role === "Admin") return INVITE_ROLE_LABEL.Admin;
  if (role === "Procurement") return INVITE_ROLE_LABEL.Procurement;
  return role;
}

/** The roster's own status vocabulary, taken from the generated schema type rather than a
 * hand-declared union (`role`/`status` are plain strings on the wire, never OpenAPI enums --
 * ADR-026 Amendment #3 -- so a server that starts returning a value this screen does not special-case
 * still renders something honest, the same "permissions degrade, labels do not" rule `memberRoleLabel`
 * above already follows for roles). */
export type MemberStatus = WorkspaceMemberBody["status"];

/**
 * `Active` / `Invited` / `Expired` (ADR-019 w14 footer clause 1). `Expired` is *derived* from this
 * system's existing "needs your decision" treatment (`.tag-outline`, the same variant
 * `getStatusTag`'s `needs_review` and `getConfidenceTag`'s `Review · N%` already use) -- never a new
 * colour. `getWorkspaceMembers` never actually emits `Expired` in w14 (a lapsed invitation drops out
 * of the roster instead of appearing with a terminal status -- ADR-026 §D3), so this branch is
 * forward-compatible rather than reachable today; any other value passes through by its own name
 * rather than being mislabelled "Active".
 */
export function getMemberStatusTag(status: MemberStatus): SemanticTag {
  if (status === "Invited") return { variant: "accent", label: "Invited" };
  if (status === "Expired") return { variant: "outline", label: "Expired" };
  if (status === "Active") return { variant: "neutral", label: "Active" };
  return { variant: "neutral", label: status };
}

/**
 * True when `member` is the tenant's sole live Admin -- the last-Admin guard this screen must never
 * let reach the server as a 409 (ADR-025 Rule D.5a; ADR-020 w14 footer 10.5/D-58.6: "a visible
 * disabled control, not a 403"). Computed purely over the roster already on screen.
 */
export function isLastActiveAdmin(members: readonly WorkspaceMemberBody[], member: WorkspaceMemberBody): boolean {
  if (member.status !== "Active" || member.role !== "Admin") return false;
  return members.filter((candidate) => candidate.status === "Active" && candidate.role === "Admin").length <= 1;
}

export interface ActionConsequence {
  question: string;
  detail: string;
}

/** ADR-020 w14 design footer, screen 10 table (D-58.5): revoke never claims a grant that never
 * existed. */
export function revokeConsequence(email: string): ActionConsequence {
  return {
    question: `Revoke the invitation for ${email}?`,
    detail: "Their link stops working. They never had access to this workspace.",
  };
}

/** Same table, `remove` rows (D-58.5/D-58.7). The self variant drops the templated workspace name
 * for the same reason `formatWorkspaceLine` above degrades gracefully -- this route is never handed
 * one -- and reads naturally with "this workspace" instead, since the Admin confirming is already
 * looking at it. */
export function removeConsequence(email: string, isSelf: boolean): ActionConsequence {
  return isSelf
    ? {
        question: "Remove yourself from this workspace?",
        detail: "You will lose access immediately and will need a new invitation to return.",
      }
    : {
        question: `Remove ${email}?`,
        detail: "They lose access immediately. To bring them back you will need to send a new invitation.",
      };
}

/**
 * The invite result is the server's fact, never a client inference from a 201 (N3b-1). Task
 * E17/F02/US01/T01 (wave w15, NW-69; ADR-026 w15 footer §8, ADR-020 w15 §3.3): the discriminant is
 * the server's own `deliveryOutcome` string -- three arms, one per outcome, and never a fourth: a
 * guest-provisioning failure is a 502 with no invitation at all (`InviteFailure` below), which is
 * what makes "a link renders only when it is usable" true by construction. `mailDelivered` is no
 * longer the discriminant and is never combined with the outcome (ADR-012 w15 §7). The link rides
 * on exactly `mail_failed` and `no_transport`; `identityProvisioned` is the one server boolean the
 * pane renders (the one-time-code sentence), on every arm.
 */
export type InviteOutcome =
  | { outcome: "sent"; email: string; identityProvisioned: boolean }
  | { outcome: "mail_failed"; email: string; acceptUrl: string; expiresAt: string; identityProvisioned: boolean }
  | { outcome: "no_transport"; email: string; acceptUrl: string; expiresAt: string; identityProvisioned: boolean };

/** Builds the pane's fact from the 201 body, keyed on its own `deliveryOutcome` -- the one place
 * the wire vocabulary is read, so the pane branches on a server string it never re-derives. */
export function inviteOutcomeFrom(member: {
  email: string;
  acceptUrl: string;
  expiresAt: string;
  deliveryOutcome: "sent" | "mail_failed" | "no_transport";
  identityProvisioned: boolean;
}): InviteOutcome {
  const { email, acceptUrl, expiresAt, identityProvisioned } = member;
  switch (member.deliveryOutcome) {
    case "sent":
      return { outcome: "sent", email, identityProvisioned };
    case "mail_failed":
      return { outcome: "mail_failed", email, acceptUrl, expiresAt, identityProvisioned };
    case "no_transport":
      return { outcome: "no_transport", email, acceptUrl, expiresAt, identityProvisioned };
  }
}

/** ADR-020 w15 §3.3, verbatim: the 201 sentences, one per outcome. The word "sent" appears in the
 * first only. */
export function inviteOutcomeSentence(outcome: InviteOutcome): string {
  switch (outcome.outcome) {
    case "sent":
      return `Invitation sent to ${outcome.email}.`;
    case "mail_failed":
      return "Invitation created, but the email could not be sent.";
    case "no_transport":
      return `Invitation ready for ${outcome.email}.`;
  }
}

/** ADR-020 w15 §3.6, verbatim: rendered only while the 201's `identityProvisioned` is true, and
 * identical whether the guest was created or already existed (no directory-enumeration oracle). */
export const IDENTITY_ONE_TIME_CODE_LINE = "They will get a one-time code from Microsoft the first time they sign in.";

/** ADR-020 w15 §3.5: every 502 row carries this, so the Admin never wonders whether a
 * half-invitation exists. */
export const NO_INVITATION_CREATED_META = "No invitation was created.";

/**
 * ADR-020 w15 §3.5's copy for the 502's closed reason set, plus the mandatory catch-all: the wire
 * `failureReason` is never rendered, so an unrecognised value, a proxy-mangled body or a later
 * addition to the set can never put a raw enum on screen (ADR-012 w15 §13.2: the literal union
 * describes the contract, not the wire, which is why the last row stays even though `tsc` calls it
 * unreachable). `consent_missing` names "a tenant administrator", not "you": a Raffa.ai workspace
 * Admin is usually not the Entra tenant admin.
 */
export function inviteFailureCopy(failureReason: string, email: string): string {
  switch (failureReason) {
    case "consent_missing":
      return "Raffa.ai is not allowed to add guests to your company directory yet. A tenant administrator has to approve that permission.";
    case "provisioning_failed":
      return `Your company directory would not add ${email}. Check the address, or ask a tenant administrator.`;
    case "directory_unavailable":
      return "Your company directory could not be reached. Try again in a few minutes.";
    default:
      return "Raffa.ai could not create this invitation.";
  }
}

/** `new URL(acceptUrl, origin)` accepts both a site-relative (w14) and an absolute (future
 * transport-wave) `acceptUrl` unchanged -- no client change needed when that wave lands. */
export function composeAcceptLink(acceptUrl: string, origin: string): string {
  return new URL(acceptUrl, origin).toString();
}

const EXPIRY_DATE_FORMATTER = new Intl.DateTimeFormat("en-GB", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  timeZone: "UTC",
});

export function formatExpiryDate(expiresAt: string): string {
  return EXPIRY_DATE_FORMATTER.format(new Date(expiresAt));
}

/** ADR-020's second w14 amendment footer ("lost the link" / "the invitation lapsed" prevention
 * string): the false-outcome meta line, verbatim. */
export function inviteLinkExpiryMeta(expiresAt: string): string {
  return `It expires ${formatExpiryDate(expiresAt)}, can be used once, and is not shown again.`;
}

/**
 * `mailto:` for the Procurement read-only variant's "Request access" (AC-9; ADR-020 w14 footer
 * D-58.8): real, honest, zero backend -- both roles are inside the same tenant and the roster already
 * shows every email, so this exposes nothing the screen does not already show. `null` when the
 * roster carries no live Admin to address (should not happen structurally, but a dead link is worse
 * than no link -- D-58.8: "a dead button is the one option that is not available").
 */
export function requestAccessMailto(adminEmails: readonly string[], workspaceName?: string): string | null {
  if (adminEmails.length === 0) return null;
  const subject = workspaceName ? `Workspace Admin access — ${workspaceName}` : "Workspace Admin access";
  return `mailto:${adminEmails.join(",")}?subject=${encodeURIComponent(subject)}`;
}
