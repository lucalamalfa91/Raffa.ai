import type { RenewalActionStatusValue } from "../../api/client";

/**
 * Client-side, session-scoped mirror of "renewals this browser has acted on this session" (ADR-020
 * screen 8; ADR-024 V2: the same record drives the Renewals list's Status column, the "Why it is
 * here" pane's acted state, Contract 360's negotiation tracker and the Savings opportunities table).
 *
 * Why this exists even though the write itself is real and durable: `POST /api/renewals/{id}/action`
 * (`Raffa.Renewals.Application.RenewalActionService.SetActionAsync`, wrapped by
 * `apiClient.postRenewalAction`, see `../../api/client.ts`) really does persist owner/status/action
 * for this renewal -- this is not a fabricated write. But there is no way to read it back over HTTP
 * yet: `RenewalActionService.GetActionAsync` exists but its own doc comment says "no HTTP route
 * calls this yet". This module is the same kind of interim `../signin/workspaceStore.ts` and
 * `../documents/documentStore.ts` already establish for their own missing-endpoint gaps: every row
 * here is a *real* action this browser really posted (never fabricated), and the known limitation is
 * discovery, not truth -- this browser cannot learn about an action taken on another device, one
 * that already existed before this session started, or one this same browser took in an earlier
 * session (`sessionStorage`, not `localStorage` -- the same session-only scope `documentStore.ts`
 * uses, for the same "one current workspace per session" reason).
 *
 * This also doubles as the honest stand-in for "Action creates an opportunity visible on Savings":
 * `Raffa.Savings.Application.SavingsOpportunityService.CreateAsync` exists but is "not yet wired
 * to an HTTP route" (its own doc comment), so no screen can durably create a real
 * `SavingsOpportunity` row via HTTP. `../savings/` merges these rows into its own table alongside
 * whatever the real `GET /api/savings` returns.
 */

const TRACKED_RENEWAL_ACTIONS_KEY = "raffa.renewals.actions";

export interface TrackedRenewalAction {
  contractId: string;
  /** `RenewalPipelineItemBody.supplierId` at the time of the action -- carried along so Savings has it without a second fetch. */
  supplierId: string | null;
  /** `RenewalPipelineItemBody.annualSpend` at the time of the action -- same reason as `supplierId` above. */
  annualSpend: number | null;
  /** Free-text -- who acted. Every screen sends the signed-in user's own `userLabel` (see `../../components/shell/WorkspaceShellApp.tsx`); there is no separate assignee picker yet. */
  owner: string;
  status: RenewalActionStatusValue;
  /** Free-text -- what was done (e.g. "In negotiation"); see `renewalPipelineViewModel.ts#getRenewalActionPlan`. */
  action: string;
  /** `RenewalActionBody.updatedAt` from the real `POST /api/renewals/{id}/action` response -- never a client-guessed timestamp. */
  updatedAt: string;
}

function readTrackedRenewalActions(storage: Storage): TrackedRenewalAction[] {
  const raw = storage.getItem(TRACKED_RENEWAL_ACTIONS_KEY);
  if (!raw) return [];
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as TrackedRenewalAction[]) : [];
  } catch {
    // Malformed/foreign sessionStorage content under this key is not this screen's problem to throw
    // over -- treat it the same as "nothing tracked yet".
    return [];
  }
}

function writeTrackedRenewalActions(storage: Storage, next: readonly TrackedRenewalAction[]): void {
  storage.setItem(TRACKED_RENEWAL_ACTIONS_KEY, JSON.stringify(next));
}

/** Every renewal this browser has acted on this session, most-recently-acted first. */
export function loadTrackedRenewalActions(storage: Storage = window.sessionStorage): TrackedRenewalAction[] {
  return readTrackedRenewalActions(storage);
}

/** This session's own recorded action for one contract, or `null` if none has been taken yet (this browser, this session). */
export function getTrackedRenewalAction(
  contractId: string,
  storage: Storage = window.sessionStorage,
): TrackedRenewalAction | null {
  return readTrackedRenewalActions(storage).find((tracked) => tracked.contractId === contractId) ?? null;
}

/**
 * Inserts or updates `tracked` by `contractId` (matches `RenewalAction`'s own upsert-by-(tenant,
 * contractId) semantics on the real backend row) and persists the result. The updated/inserted row
 * moves to the front, mirroring `documentStore.ts#rememberDocument`.
 */
export function rememberRenewalAction(
  tracked: TrackedRenewalAction,
  storage: Storage = window.sessionStorage,
): TrackedRenewalAction[] {
  const existing = readTrackedRenewalActions(storage);
  const withoutThisOne = existing.filter((known) => known.contractId !== tracked.contractId);
  const next = [tracked, ...withoutThisOne];
  writeTrackedRenewalActions(storage, next);
  return next;
}

/**
 * Contract 360's "Undo" (`app.jsx` `undo360`): drops this session's record for one contract so the
 * list, pane and tracker fall back to "Open". The caller is responsible for the matching real write
 * (re-posting the renewal's action as NotStarted / "Open") -- this only forgets the local mirror.
 */
export function forgetRenewalAction(contractId: string, storage: Storage = window.sessionStorage): TrackedRenewalAction[] {
  const next = readTrackedRenewalActions(storage).filter((known) => known.contractId !== contractId);
  writeTrackedRenewalActions(storage, next);
  return next;
}
