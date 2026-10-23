import { useSyncExternalStore } from "react";
import { createCaseStore, type CaseStore, type CaseStoreState } from "./caseStore";
import { seedDemoCases } from "./demoCases";

let shared: CaseStore | null = null;

/** The app-wide store (demo-seeded until the backend owns renewal cases). */
export function getCaseStore(): CaseStore {
  shared ??= createCaseStore({ seed: seedDemoCases });
  return shared;
}

export function useCaseStore(store: CaseStore = getCaseStore()): CaseStoreState {
  return useSyncExternalStore(store.subscribe, store.getState, store.getState);
}
