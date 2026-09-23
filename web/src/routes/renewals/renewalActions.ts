import { ASK_PROMPTS } from "../../components/ask-bar/askLaunch";
import { buildPortfolioSelectionHref } from "../contracts/portfolioViewModel";
import { buildSavingsContractHref } from "../savings/savingsFilters";
import { getRenewalWorkflowState, type RenewalTableRow } from "./renewalPipelineViewModel";

/**
 * The Renewals action registry: every thing a user can launch from a renewal -- from the selected
 * row's pane or from a multi-row selection -- declared once, here, as data. The screen renders
 * whatever this file lists (`RenewalActionLauncher.tsx`, `RenewalBulkBar.tsx`), so a new capability
 * (a reminder, an approval request, an e-signature hand-off...) is switched on by adding one entry
 * and, if it needs one, one target kind -- never by editing the pane.
 *
 * Three target kinds cover everything today:
 * - `link`: another screen with this contract in context (Contract 360, Portfolio, Savings, Quote check);
 * - `ask`: a new Ask Raffa chat asking a prepared question, bound to the contract (`askLaunch.ts`);
 * - `write`: a real write on the renewal itself (the bulk "Assign to me", through the same
 *   `POST /api/renewals/{id}/action` the pane's buttons make).
 *
 * `status: "soon"` marks an operation Raffa.ai cannot perform yet. It is still offered, and still
 * does something real: it opens Ask with the request phrased as that operation, and Ask answers the
 * Claude.ai way (ADR-030) -- says in one sentence it cannot do it yet, offers the nearest thing that
 * works today, and can file the request for the team. Flipping an entry to `"available"` (with a
 * `link` or `write` target) is how it ships.
 */

export type RenewalActionTarget =
  | { kind: "link"; to: string; state?: Record<string, unknown> }
  | { kind: "ask"; question: string; scopeContractId: string | null }
  | { kind: "write"; write: "assign" };

export type RenewalActionGroupKey = "ask" | "open" | "soon";

export interface RenewalActionGroup {
  key: RenewalActionGroupKey;
  title: string;
  /** One line under the group title, when the group needs explaining. */
  note: string | null;
}

export const RENEWAL_ACTION_GROUPS: readonly RenewalActionGroup[] = [
  { key: "ask", title: "Ask Raffa", note: null },
  { key: "open", title: "Open with this contract", note: null },
  {
    key: "soon",
    title: "Coming to Raffa.ai",
    note: "Not built yet. Ask Raffa tells you what works today and can pass the request to the team.",
  },
];

/** What an action is computed from: the selected row, plus the supplier's name when the wire has one. */
export interface RenewalActionContext {
  row: RenewalTableRow;
  /** The supplier's real name, or `null` -- a question then says "this contract", never an id. */
  supplier: string | null;
}

export function buildRenewalActionContext(row: RenewalTableRow): RenewalActionContext {
  const name = row.item.supplierName?.trim() ?? "";
  return { row, supplier: name === "" ? null : name };
}

export interface RenewalActionDefinition {
  id: string;
  group: RenewalActionGroupKey;
  label: string;
  /** What happens, in one short line -- the pane shows it under the label. */
  hint: string;
  status: "available" | "soon";
  target: (context: RenewalActionContext) => RenewalActionTarget;
  /** Hide the action where it cannot apply (e.g. no notice date to remind about). Absent = always shown. */
  appliesTo?: (context: RenewalActionContext) => boolean;
}

const hasNoticeAhead = ({ row }: RenewalActionContext) =>
  row.item.daysUntilCancellationDeadline !== null && row.item.daysUntilCancellationDeadline >= 0;

export const RENEWAL_ACTIONS: readonly RenewalActionDefinition[] = [
  {
    id: "plan",
    group: "ask",
    label: "Plan the negotiation",
    hint: "Where you have leverage and what to ask for, from this contract's facts",
    status: "available",
    target: ({ row, supplier }) => ({ kind: "ask", question: ASK_PROMPTS.renewalApproach(supplier), scopeContractId: row.item.contractId }),
  },
  {
    id: "draft-email",
    group: "ask",
    label: "Draft the negotiation email",
    hint: "A ready-to-edit email to the supplier, written from the contract",
    status: "available",
    target: ({ row, supplier }) => ({ kind: "ask", question: ASK_PROMPTS.negotiationEmail(supplier), scopeContractId: row.item.contractId }),
    appliesTo: ({ row }) => getRenewalWorkflowState(row.tracked) !== "closed",
  },
  {
    id: "market",
    group: "ask",
    label: "Check the price against the market",
    hint: "How this contract's prices compare with what others pay",
    status: "available",
    target: ({ row, supplier }) => ({ kind: "ask", question: ASK_PROMPTS.marketCheck(supplier), scopeContractId: row.item.contractId }),
  },
  {
    id: "why-here",
    group: "ask",
    label: "Why is it ranked here?",
    hint: "The priority score, explained",
    status: "available",
    target: ({ row, supplier }) => ({ kind: "ask", question: ASK_PROMPTS.renewalWhyTop(supplier), scopeContractId: row.item.contractId }),
  },
  {
    id: "contract-360",
    group: "open",
    label: "Contract 360",
    hint: "Facts, clauses, obligations and the tracker",
    status: "available",
    target: ({ row }) => ({
      kind: "link",
      to: `/contracts/${row.item.contractId}`,
      state: { from: "renewals", returnTo: `/renewals?select=${encodeURIComponent(row.item.contractId)}` },
    }),
  },
  {
    id: "savings",
    group: "open",
    label: "Savings",
    hint: "The savings identified on this contract",
    status: "available",
    target: ({ row }) => ({ kind: "link", to: buildSavingsContractHref(row.item.contractId) }),
  },
  {
    id: "portfolio",
    group: "open",
    label: "Portfolio",
    hint: "This contract among the rest",
    status: "available",
    target: ({ row }) => ({ kind: "link", to: buildPortfolioSelectionHref([row.item.contractId], "renewals") }),
  },
  {
    id: "quote-check",
    group: "open",
    label: "Quote check",
    hint: "Got the renewal quote? Compare it with the market",
    status: "available",
    target: () => ({ kind: "link", to: "/quotes" }),
    appliesTo: ({ row }) => getRenewalWorkflowState(row.tracked) !== "closed",
  },
  {
    id: "reminder",
    group: "soon",
    label: "Remind me before the notice date",
    hint: "An email reminder ahead of the deadline",
    status: "soon",
    target: ({ row, supplier }) => ({ kind: "ask", question: ASK_PROMPTS.reminder(supplier), scopeContractId: row.item.contractId }),
    appliesTo: hasNoticeAhead,
  },
  {
    id: "calendar",
    group: "soon",
    label: "Add the notice date to my calendar",
    hint: "A calendar entry on the deadline",
    status: "soon",
    target: ({ row, supplier }) => ({ kind: "ask", question: ASK_PROMPTS.calendar(supplier), scopeContractId: row.item.contractId }),
    appliesTo: hasNoticeAhead,
  },
  {
    id: "send-notice",
    group: "soon",
    label: "Send the cancellation notice",
    hint: "The notice letter, sent from Raffa.ai",
    status: "soon",
    target: ({ row, supplier }) => ({ kind: "ask", question: ASK_PROMPTS.sendNotice(supplier), scopeContractId: row.item.contractId }),
    appliesTo: ({ row }) => getRenewalWorkflowState(row.tracked) !== "closed",
  },
  {
    id: "export",
    group: "soon",
    label: "Export the renewal brief",
    hint: "The facts and the plan as an Excel file",
    status: "soon",
    target: ({ row, supplier }) => ({ kind: "ask", question: ASK_PROMPTS.exportBrief(supplier), scopeContractId: row.item.contractId }),
  },
];

export interface RenewalActionView {
  id: string;
  label: string;
  hint: string;
  status: "available" | "soon";
  target: RenewalActionTarget;
}

export interface RenewalActionGroupView extends RenewalActionGroup {
  actions: readonly RenewalActionView[];
}

/** The pane's launcher: the registry resolved for one row, grouped in `RENEWAL_ACTION_GROUPS` order, empty groups dropped. */
export function buildRenewalActionGroups(
  context: RenewalActionContext,
  actions: readonly RenewalActionDefinition[] = RENEWAL_ACTIONS,
): readonly RenewalActionGroupView[] {
  return RENEWAL_ACTION_GROUPS.map((group) => ({
    ...group,
    actions: actions
      .filter((action) => action.group === group.key && (action.appliesTo?.(context) ?? true))
      .map((action) => ({ id: action.id, label: action.label, hint: action.hint, status: action.status, target: action.target(context) })),
  })).filter((group) => group.actions.length > 0);
}

// ---------------------------------------------------------------------------------------------
// Bulk: actions over the rows ticked in the list
// ---------------------------------------------------------------------------------------------

export interface RenewalBulkActionDefinition {
  id: string;
  /** Label for `count` selected rows (only the rows the action applies to are counted). */
  label: (count: number) => string;
  target: (rows: readonly RenewalTableRow[]) => RenewalActionTarget;
  /** The subset of the selection the action applies to; the action is hidden when it is empty. */
  appliesTo: (rows: readonly RenewalTableRow[]) => readonly RenewalTableRow[];
}

function namedSuppliers(rows: readonly RenewalTableRow[]): string[] {
  return Array.from(new Set(rows.map((row) => row.item.supplierName?.trim() ?? "").filter((name) => name !== "")));
}

export const RENEWAL_BULK_ACTIONS: readonly RenewalBulkActionDefinition[] = [
  {
    id: "assign",
    label: (count) => `Assign ${count} to me`,
    target: () => ({ kind: "write", write: "assign" }),
    // Only rows nobody has acted on: an "Assign" never overwrites a negotiation already under way.
    appliesTo: (rows) => rows.filter((row) => getRenewalWorkflowState(row.tracked) === "open"),
  },
  {
    id: "portfolio",
    label: (count) => `Show ${count} in Portfolio`,
    target: (rows) => ({ kind: "link", to: buildPortfolioSelectionHref(rows.map((row) => row.item.contractId), "renewals") }),
    appliesTo: (rows) => rows,
  },
  {
    id: "ask-first",
    label: () => "Ask Raffa which to start first",
    target: (rows) => ({ kind: "ask", question: ASK_PROMPTS.startFirstAmong(namedSuppliers(rows)), scopeContractId: null }),
    appliesTo: (rows) => (rows.length > 1 ? rows : []),
  },
];

export interface RenewalBulkActionView {
  id: string;
  label: string;
  target: RenewalActionTarget;
  /** The rows the action runs on (for a `write`). */
  rows: readonly RenewalTableRow[];
}

export function buildRenewalBulkActions(
  selected: readonly RenewalTableRow[],
  actions: readonly RenewalBulkActionDefinition[] = RENEWAL_BULK_ACTIONS,
): readonly RenewalBulkActionView[] {
  return actions.flatMap((action) => {
    const rows = action.appliesTo(selected);
    if (rows.length === 0) return [];
    return [{ id: action.id, label: action.label(rows.length), target: action.target(rows), rows }];
  });
}
