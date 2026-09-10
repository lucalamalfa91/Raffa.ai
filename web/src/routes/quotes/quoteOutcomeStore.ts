import type { NegotiationOutcomeBody } from "../../api/client";

/**
 * Client-side record of "negotiation outcomes this browser has recorded this session" (task
 * E08/F03/US01/T01, us-01-quote-check AC-4: "...recorded outcome ... -> Home Savings Realized
 * updates"). Same interim shape `../signin/workspaceStore.ts`/`../documents/documentStore.ts`
 * already establish for this codebase, for the identical reason:
 *
 * There is no backend query this screen -- or Home's own, not-yet-built Savings screen
 * (epic-08/feature-02-savings-ui) -- could read the running list back from today:
 *   - `backend/src/Raffa.Api/NegotiationsEndpointExtensions.cs` maps only
 *     `POST /api/negotiations/outcomes` -- no `GET` (list or single) anywhere.
 *   - `GET/PATCH /api/savings` (`SavingsEndpointExtensions`) is the real, generic surface a future
 *     Home task would extend `../../api/client.ts` to call for the KPI row itself; this task's own
 *     file scope (`src/routes/quotes/`) does not touch it, and `captureNegotiationOutcome`'s request
 *     never supplies a `savingsOpportunityId` here (there is no Savings UI yet to pick one from), so
 *     `NegotiationOutcomePropagationService`'s own cross-module write never runs for an outcome this
 *     screen records -- an honest, named consequence, not a bug.
 *
 * Until a backend task adds a list query (or the Home task wires a real `GET /api/savings` KPI
 * read), this module is the interim: every entry here is the *real*, server-computed response body
 * this browser actually received from a real `POST /api/negotiations/outcomes` call
 * (`../../api/client.ts`'s `captureNegotiationOutcome`) -- never fabricated. The known limitation is
 * discovery, not truth: this browser cannot learn about an outcome recorded on another
 * device/browser, or one recorded before this session started.
 *
 * Session-scoped (`sessionStorage`), not workspace-keyed -- same scope/reasoning as
 * `documentStore.ts`'s own `TRACKED_DOCUMENTS_KEY`.
 */

const TRACKED_OUTCOMES_KEY = "raffa.quotes.negotiationOutcomes";

export type TrackedNegotiationOutcome = NegotiationOutcomeBody;

function readTrackedOutcomes(storage: Storage): TrackedNegotiationOutcome[] {
  const raw = storage.getItem(TRACKED_OUTCOMES_KEY);
  if (!raw) return [];
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as TrackedNegotiationOutcome[]) : [];
  } catch {
    // Malformed/foreign sessionStorage content under this key is not this screen's problem to throw
    // over -- treat it the same as "nothing recorded yet" (workspaceStore.ts's own
    // readWorkspaceArray follows the same convention).
    return [];
  }
}

/** Every negotiation outcome this browser has recorded this session, most-recently-captured first. */
export function loadNegotiationOutcomes(storage: Storage = window.sessionStorage): TrackedNegotiationOutcome[] {
  return readTrackedOutcomes(storage);
}

/** Inserts or updates `outcome` by id (re-recording an outcome for the same quote in the same
 * session updates that row instead of duplicating it) and persists the result. New/updated entries
 * move to the front -- an activity list, not an archive, the same convention
 * `documentStore.ts#rememberDocument` already uses. */
export function rememberNegotiationOutcome(
  outcome: TrackedNegotiationOutcome,
  storage: Storage = window.sessionStorage,
): TrackedNegotiationOutcome[] {
  const existing = readTrackedOutcomes(storage);
  const withoutThisOne = existing.filter((known) => known.id !== outcome.id);
  const next = [outcome, ...withoutThisOne];
  storage.setItem(TRACKED_OUTCOMES_KEY, JSON.stringify(next));
  return next;
}

/** Sum of every recorded outcome's own server-computed `realizedSaving` this session -- the same
 * real, never-fabricated figure a future Home task's own "Savings realized" KPI would need, kept
 * here (not re-derived per-caller) so it can only ever be computed one way. */
export function sumRealizedSavings(outcomes: readonly TrackedNegotiationOutcome[]): number {
  return outcomes.reduce((total, outcome) => total + outcome.realizedSaving, 0);
}
