import type { RenewalActionStatusValue } from "../../api/client";

/**
 * Client-side, session-scoped mirror of "renewals this browser has acted on this session" (ADR-020
 * screen 8; story us-01-renewal-pipeline AC-3; council decision carried into that story: "Action
 * creates an opportunity visible on Home; insight is a card, not a raw JSON dump").
 *
 * Why this exists even though the write itself is real and durable: `POST /api/renewals/{id}/action`
 * (`Contigo.Renewals.Application.RenewalActionService.SetActionAsync`, wrapped by
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
 * This also doubles as the honest stand-in for "Action creates an opportunity visible on Home":
 * `Contigo.Savings.Application.SavingsOpportunityService.CreateAsync` ("identify" a new
 * `SavingsOpportunity`) exists but, per its own doc comment, is "not yet wired to an HTTP route ...
 * wiring a real caller is deliberately out of this task's own scope" -- so this screen cannot
 * durably create a real `SavingsOpportunity` row via HTTP (`POST /api/savings` does not exist;
 * `GET`/`PATCH /api/savings/{id}` both need an id this screen has no way to obtain). Recording the
 * acted renewal here, and linking to Home (`/`) from the confirmation panel
 * (`InsightCard.tsx`), is the honest interim until a future task wires `CreateAsync` to a route.
 * Whichever task builds epic-08/feature-02-savings-ui's Home screen (task E08/F02/US01/T01, which
 * `reports/plan/wave-spec.execution.yaml` already lists as `depends_on: [web-app-shell,
 * web-renewal-ui]` -- i.e. after this task) can import `loadTrackedRenewalActions()` from here to
 * merge these into its own opportunities table alongside whatever the real (likely still-empty)
 * `GET /api/savings` returns -- the same "breadcrumb for a future task" convention
 * `documentStore.ts` already leaves.
 */

const TRACKED_RENEWAL_ACTIONS_KEY = "contigo.renewals.actions";

export interface TrackedRenewalAction {
  contractId: string;
  /** `RenewalPipelineItemBody.supplierId` at the time of the action -- carried along so a future Home screen has it without a second fetch; never re-read from the server afterward. */
  supplierId: string | null;
  /** `RenewalPipelineItemBody.annualSpend` at the time of the action -- same reason as `supplierId` above. */
  annualSpend: number | null;
  /** Free-text -- who acted. This screen always sends the signed-in user's own `userLabel` (see `../../components/shell/WorkspaceShellApp.tsx`); there is no separate assignee picker in V1. */
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
    // over -- treat it the same as "nothing tracked yet" (documentStore.ts/workspaceStore.ts's own
    // readers follow the same convention).
    return [];
  }
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
 * contractId) semantics on the real backend row -- see this module's own header comment) and
 * persists the result. The updated/inserted row moves to the front, mirroring
 * `documentStore.ts#rememberDocument`.
 */
export function rememberRenewalAction(
  tracked: TrackedRenewalAction,
  storage: Storage = window.sessionStorage,
): TrackedRenewalAction[] {
  const existing = readTrackedRenewalActions(storage);
  const withoutThisOne = existing.filter((known) => known.contractId !== tracked.contractId);
  const next = [tracked, ...withoutThisOne];
  storage.setItem(TRACKED_RENEWAL_ACTIONS_KEY, JSON.stringify(next));
  return next;
}
