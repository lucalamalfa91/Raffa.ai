/**
 * Contigo — two-tier rail navigation model (ADR-024 V2 IA amendment to ADR-018/ADR-020; task
 * E13/F09/US01/T01, gap G-IA-V2).
 *
 * V2 replaces the flat, eight-item Day-1 rail (Home, Portfolio, Renewals, Ask, Quote check,
 * Documents, Review queue, Workspace & members -- one `getVisibleNavItems(role)` list) with two
 * tiers, quoted from the unpacked V2 prototype (`inputs/design/prototypes/contigo-v2/`):
 *
 *   - **Primary** (`app.jsx` `primaryNav`): Ask Contigo (badge `⌘K`, the last 5 conversations
 *     nested under it, "+ New chat") and Documents (badge `N to review` / `N docs`).
 *   - **Secondary, "From your contracts"** (`app.jsx` `kbNav`, `markup.html` "From your
 *     contracts"): Portfolio, Renewals, Quote check -- the whole row's foreground dims to
 *     `var(--color-neutral-500)` until the first validated contract (`kbReady`); Quote check's own
 *     badge is the constant `optional`, never a count.
 *
 * "Home" and "Review queue" (the old flat list's first and seventh items) do not exist in V2 at all
 * (`ia-v2.md` "No Home item"; Savings moved to its own `/savings` route, reached from actions, not
 * the rail; Review is now a state of Documents, `/documents?review=:id`, not a rail destination) --
 * see WorkspaceShellApp.tsx for the matching route changes (`/` -> `/ask`, `/review` -> redirect).
 * "Workspace & members" moves from a flat-list row to a footer link (RailNav.tsx), still gated the
 * same way.
 *
 * This module stays pure -- no React, no I/O, unit-testable without rendering (`navItems.test.ts`).
 * The badge/greyed *values* below are computed from data the caller (RailNav.tsx) already has (a
 * `useValidatedContractCount` result, a `loadTrackedDocuments()` read) -- this file only decides the
 * resulting label/tone/greyed flag, it never fetches anything itself.
 */

export type WorkspaceRole = "admin" | "procurement";

/** Display labels, matching the backend enum names. */
export const WORKSPACE_ROLE_LABEL: Record<WorkspaceRole, string> = {
  admin: "Workspace Admin",
  procurement: "Procurement",
};

/**
 * "Workspace & members" is the one role-gated rail surface (ADR-018 "Roles (Day-1)": "Procurement --
 * all routes except member management"). Kept as its own tiny named predicate -- not an inline
 * `role === "admin"` at the footer call site -- so the role gate stays one grep-able, unit-tested
 * place, the same reason the old flat-list `getVisibleNavItems` existed.
 */
export function canManageMembers(role: WorkspaceRole): boolean {
  return role === "admin";
}

/** `"attention"` is the one accent-700 case (Documents' "N to review"); every other badge in both
 * tiers is the constant muted neutral-600 (`app.jsx`'s own `badgeFg`/kbNav badge colour, which never
 * varies by tone for Portfolio/Renewals/Quote check). */
export type BadgeTone = "muted" | "attention";

export interface NavBadge {
  text: string;
  tone: BadgeTone;
}

export interface PrimaryNavItem {
  id: "ask" | "documents";
  label: string;
  path: string;
  badge: NavBadge | null;
  /** Only Ask Contigo carries the nested conversations/"+ New chat" slot (RailNav.tsx). */
  hasConversationSlot: boolean;
}

export interface SecondaryNavItem {
  id: "portfolio" | "renewals" | "quote-check";
  label: string;
  path: string;
  badge: NavBadge | null;
  /** True until the first validated contract (`kbReady`) -- the whole row's foreground dims
   * (`app.jsx` kbNav's own `fg: s.screen===k?accent:kbReady?'inherit':'var(--color-neutral-500)'` --
   * greyed only when the row is not also the active screen; RailNav.tsx's `is-active` modifier wins
   * over `is-greyed` for exactly that reason). */
  greyed: boolean;
}

/** `app.jsx` primaryNav's own always-muted `⌘K` badge -- never changes with data. */
const ASK_BADGE: NavBadge = { text: "⌘K", tone: "muted" };

/** `app.jsx` kbNav's own literal, constant third badge -- never a count, never greyed away. */
const QUOTE_CHECK_BADGE: NavBadge = { text: "optional", tone: "muted" };

export interface DocumentCounts {
  /** Every document this browser knows about this session
   * (`../../routes/documents/documentStore.ts#loadTrackedDocuments`) -- the same session-scoped
   * interim source that module's own doc comment names, since there is still no
   * `GET /api/documents` collection endpoint (gap G-DOC-API, F09/T03). */
  total: number;
  /** `processingStatus === "NeedsReview"` count within `total`. */
  needsReview: number;
}

/**
 * Documents badge (`app.jsx`: `needReview?needReview+' to review':(docs.length?docs.length+'
 * docs':'')`). `null` (no badge at all) when this browser has not tracked any document yet -- an
 * honest absence, never a fabricated "0 docs".
 */
export function getDocumentsBadge(counts: DocumentCounts): NavBadge | null {
  if (counts.needsReview > 0) {
    return { text: `${counts.needsReview} to review`, tone: "attention" };
  }
  if (counts.total > 0) {
    return { text: `${counts.total} docs`, tone: "muted" };
  }
  return null;
}

export function buildPrimaryNavItems(documentsBadge: NavBadge | null): readonly PrimaryNavItem[] {
  return [
    { id: "ask", label: "Ask Contigo", path: "/ask", badge: ASK_BADGE, hasConversationSlot: true },
    { id: "documents", label: "Documents", path: "/documents", badge: documentsBadge, hasConversationSlot: false },
  ];
}

export interface SecondaryNavInput {
  kbReady: boolean;
  /** Count of validated contracts (`useValidatedContractCount`) -- ignored while `!kbReady` (the
   * badge is empty, never a fabricated "0", until the first validated contract exists). */
  validatedContractCount: number;
}

/**
 * Secondary tier ("From your contracts"). Portfolio/Renewals share one badge (`app.jsx`:
 * `kbReady?completedCids.length:''` for both); Quote check's is the constant `optional`. Every item
 * greys together (`app.jsx` kbNav's own `fg` rule applies uniformly across all three rows).
 */
export function buildSecondaryNavItems(input: SecondaryNavInput): readonly SecondaryNavItem[] {
  const countBadge: NavBadge | null = input.kbReady
    ? { text: String(input.validatedContractCount), tone: "muted" }
    : null;
  const greyed = !input.kbReady;

  return [
    { id: "portfolio", label: "Portfolio", path: "/contracts", badge: countBadge, greyed },
    { id: "renewals", label: "Renewals", path: "/renewals", badge: countBadge, greyed },
    { id: "quote-check", label: "Quote check", path: "/quotes", badge: QUOTE_CHECK_BADGE, greyed },
  ];
}
