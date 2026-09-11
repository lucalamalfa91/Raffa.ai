import type { SemanticTag } from "../../../styles/semantics";

/**
 * Pure helpers for the Workspace & members screen (route `/workspace/members`; ADR-024 V2 IA;
 * screens-v2.md #10; `raffa-v2/markup.html` "WORKSPACE & MEMBERS" block). No React here -- every
 * rule is unit-testable without rendering (`members.test.tsx`, `web/tests/routes/workspace/members`).
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
 * really Admin-only. **Unchanged by w14** (ADR-020 §10.6) -- copying the export's superseded
 * "Also uploads, deletes, manages members" would regress this accepted decision.
 */
export const INVITE_ROLE_SUMMARY: Record<Day1InviteRole, string> = {
  Procurement: "Asks, uploads, reviews, triages renewals",
  Admin: "Also deletes documents and manages members",
};

/** `markup.html` `kbOff` tip, verbatim. Shown while the knowledge base has no validated contract yet.
 * **Unchanged by w14** -- no task should "improve" it into an always-on banner. */
export const MEMBERS_TIP = "Tip: invite the team once the first contract is validated — there is nothing for them to ask before that.";

/** Header meta line: "tenant {id}". Pre-w14 this also carried the workspace's own name
 * (`{workspace} · tenant {id}`), read from the `sessionStorage`-cached "current workspace". w14's
 * interface contract with the phase-4 shell sibling (`E14/F03/US02/T01`) passes `MembersRoute` only
 * `workspaceId` and `role` -- no name -- so this line is honestly narrowed to what this screen
 * actually holds, rather than inventing or re-fetching a name nobody asked this task to carry. */
export function formatWorkspaceLine(tenantId: string): string {
  return `tenant ${tenantId}`;
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
 * ADR-020 §10.2's w14 warning -- the export's old blocking sentence ("Use an @{domain} address.")
 * demoted to a non-blocking, informative one. The tenant "domain" is a proxy read off whoever is
 * signed in (`index.tsx`'s own `workspaceDomainFromEmail(userLabel)` call, gap named below at
 * `validateInviteEmail`'s own doc comment) -- not a server-held rule (the server has no domain
 * restriction at all, ADR-001's w14 footer defers the real restriction to the wave that lands
 * ADR-010, and a direct API call bypasses this entirely). A typo guard, never a safeguard.
 */
export function formatDomainWarning(email: string, tenantDomain: string, role: Day1InviteRole): string {
  return `${email} is outside ${tenantDomain}. They will get full ${INVITE_ROLE_LABEL[role]} access to this workspace.`;
}

export interface InviteEmailValidation {
  /** Blocking -- empty or malformed only. A malformed address is not an address (ADR-020 §10.2). */
  error: string | null;
  /** Non-blocking -- true when the address's domain differs from `tenantDomain`. Submit proceeds
   * either way; the caller renders `formatDomainWarning` under the field when this is true. */
  crossDomain: boolean;
}

/**
 * Client-side invite validation (screens-v2.md #10: "email must match the workspace domain"). There
 * is no tenant-domain field on `WorkspaceTenant` -- the honest proxy is the signed-in Admin's own
 * email domain. Format errors stay **blocking**; the domain check is **non-blocking** as of w14
 * (ADR-020 §10.2) -- far too imprecise to *gate* an invitation (the server has none of this logic;
 * a direct API call bypasses it entirely), perfectly adequate to *inform* one.
 */
export function validateInviteEmail(email: string, tenantDomain: string | null): InviteEmailValidation {
  const trimmed = email.trim();
  if (trimmed.length === 0) return { error: "An email is required.", crossDomain: false };
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmed)) {
    return { error: "Enter a valid email address.", crossDomain: false };
  }

  const inviteDomain = workspaceDomainFromEmail(trimmed);
  const crossDomain = tenantDomain !== null && inviteDomain !== tenantDomain;
  return { error: null, crossDomain };
}

export function memberRoleLabel(role: string): string {
  if (role === "Admin") return INVITE_ROLE_LABEL.Admin;
  if (role === "Procurement") return INVITE_ROLE_LABEL.Procurement;
  return role;
}

/**
 * `memberStore.ts:14`'s binary union, moved here because that file is deleted whole by this task
 * (ADR-020 §10.3: "`MemberStatus` (`memberStore.ts:14`) a binary union -- neither can express a
 * third case ... so the union moves rather than being edited in place"). The wire's own `status`
 * field is a plain, non-enum string (ADR-026 Amendment #3 -- role and status are never OpenAPI
 * enums, per-tenant rows the generator cannot close over), so this is a hand-declared closed
 * vocabulary, not a generated one -- the same discipline `api/client.ts`'s
 * `PortfolioRiskSeverity`/`QuoteMarketPosition` already establish for the identical generator gap.
 * `Expired` is new (ADR-019 w14 clause 1) and, as of w14, forward-declared: the roster never emits
 * it today (`WorkspaceMembershipService.ComposeRoster` silently drops an expired invitation rather
 * than rendering a status for it -- backend/.../WorkspaceMembershipService.cs:508-511), but
 * `getMemberStatusTag` must already know how to render it the day that changes.
 */
export type MemberStatus = "Active" | "Invited" | "Expired";

/**
 * `app.jsx` members: Active rows `tag-neutral`, a freshly invited row `tag-accent`. `Expired` is
 * *derived*, not invented: `.tag-outline` is already this system's "needs your decision" treatment
 * (`getConfidenceTag`'s `Review · N%`, `getStatusTag`'s `needs_review`) -- never a new colour
 * (ADR-019 w14 clause 1). Takes the wire's own plain string, like `memberRoleLabel` above, rather
 * than the closed `MemberStatus` type -- an unrecognised value still renders as Active rather than
 * throwing, the same fallback the pre-w14 binary ternary already had. Text carries the meaning, the
 * variant only adds emphasis.
 */
export function getMemberStatusTag(status: string): SemanticTag {
  if (status === "Invited") return { variant: "accent", label: "Invited" };
  if (status === "Expired") return { variant: "outline", label: "Expired" };
  return { variant: "neutral", label: "Active" };
}

/** `mailDelivered: true` (ADR-026 §D5) -- the export's "Invitation sent." plus the address, so a
 * typo is catchable at the moment it is made. */
export function formatInvitationSentMessage(email: string): string {
  return `Invitation sent to ${email}.`;
}

/** `mailDelivered: false` -- deliberately true for both of its causes (no transport configured,
 * transport errored; council decision, w14 table) and never diagnoses the mailer. The word "sent"
 * must never appear in this string. */
export function formatInvitationReadyMessage(email: string): string {
  return `Invitation ready for ${email}.`;
}

/** Same fixed locale/UTC convention `documentTable.ts#formatUploadedAt` established (that file's own
 * comment has the full reasoning): locale/timezone-independent, so this and its unit tests do not
 * depend on the host/CI runner. */
const INVITATION_EXPIRY_FORMATTER = new Intl.DateTimeFormat("en-GB", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  timeZone: "UTC",
});

export function formatInvitationExpiry(expiresAt: string): string {
  return INVITATION_EXPIRY_FORMATTER.format(new Date(expiresAt));
}

/** ADR-020's second w14 footer (the re-issue correction, `INDEX.md` "Second face of the same gap"):
 * the meta line under the copyable link. "can be used once, and is not shown again" is a real
 * property of this contract -- the server returns `acceptUrl` exactly once, on the invite 201;
 * nothing re-reads or re-serves it later -- stated up front so an Admin who navigates away never
 * expects to find the link again. */
export function formatInvitationLinkMeta(expiresAt: string): string {
  return `It expires ${formatInvitationExpiry(expiresAt)}, can be used once, and is not shown again.`;
}

/** Composes the copyable link from the server's `acceptUrl`. `new URL(acceptUrl,
 * window.location.origin)` accepts both a site-relative value (w14, ADR-005 w14 footer: an absolute
 * URL would need an API env var this wave deliberately does not add) and an absolute one, so no
 * client change is needed the day a transport wave makes it absolute. */
export function buildAcceptLink(acceptUrl: string): string {
  return new URL(acceptUrl, window.location.origin).toString();
}

/** The server's own outcome for the most recently issued invite (`InvitedMemberBody`, narrowed to
 * what `InvitePane` renders) -- never inferred from the request's mere 201, only from these fields. */
export interface InviteOutcome {
  email: string;
  mailDelivered: boolean;
  acceptUrl: string;
  expiresAt: string;
}

/**
 * `Request access` (ADR-020 §10.7; a non-Admin's read-only variant of this screen): a real
 * `mailto:` to the workspace's currently-live Admins, discoverable only because this screen just
 * read the roster -- never an inert button (ADR-018 `:107-108`). Structurally typed (not
 * `WorkspaceMemberBody`) so this file stays decoupled from the API layer, the same "no React, no
 * I/O" discipline this file's own header comment states.
 */
export function buildRequestAccessMailto(
  members: readonly { email: string; role: string; status: string }[],
): string {
  const admins = members
    .filter((member) => member.status === "Active" && member.role === "Admin")
    .map((member) => member.email);
  return `mailto:${admins.join(",")}`;
}
