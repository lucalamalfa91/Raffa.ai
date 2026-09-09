/**
 * Session-local checklist state for Contract 360's negotiation tracker (`app.jsx` `steps360`:
 * `s.steps360[cur.id]` -- an array of four booleans toggled by the reader). The steps themselves
 * are a fixed, deterministic plan (`contract360ViewModel.ts#buildNegotiationSteps`); only the
 * ticks are stored, per contract, in `sessionStorage` -- the same session-only scope
 * `../../renewals/renewalActionStore.ts` uses for the action they hang off. No backend endpoint
 * records checklist progress yet; this is the honest interim, never a fabricated server state.
 */

const STEPS_KEY_PREFIX = "contigo.contract360.steps.";
export const NEGOTIATION_STEP_COUNT = 4;

function storageKey(contractId: string): string {
  return `${STEPS_KEY_PREFIX}${contractId}`;
}

export function loadNegotiationSteps(contractId: string, storage: Storage = window.sessionStorage): boolean[] {
  const empty = Array.from({ length: NEGOTIATION_STEP_COUNT }, () => false);
  const raw = storage.getItem(storageKey(contractId));
  if (!raw) return empty;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return empty;
    return empty.map((_, index) => parsed[index] === true);
  } catch {
    return empty;
  }
}

export function saveNegotiationSteps(contractId: string, steps: readonly boolean[], storage: Storage = window.sessionStorage): boolean[] {
  const next = Array.from({ length: NEGOTIATION_STEP_COUNT }, (_, index) => steps[index] === true);
  storage.setItem(storageKey(contractId), JSON.stringify(next));
  return next;
}

export function clearNegotiationSteps(contractId: string, storage: Storage = window.sessionStorage): void {
  storage.removeItem(storageKey(contractId));
}
