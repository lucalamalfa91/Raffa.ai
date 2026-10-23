/**
 * Local store for renewal cases, standing in for the backend until it owns `Case`.
 *
 * It holds one `Case` per contract, persists them in the browser, and routes every change through
 * the pure engine (`act`). The two side effects the engine asks for -- putting an email on the
 * clipboard and ticking the simulated extraction pipeline -- are performed here, never in the engine.
 *
 * Swapping this for the real thing means implementing the same four methods against the API; the
 * chat and the Renewals screens only talk to `CaseStore`.
 */

import { act, create, type CreateOptions } from "./engine";
import type { ActPayload, ActionType, Case, CaseContext } from "./types";

export interface CaseStoreState {
  readonly cases: Readonly<Record<string, Case>>;
}

export interface StoreEffects {
  copyToClipboard(text: string): void;
  later(run: () => void, ms: number): void;
}

export interface CaseStore {
  getState(): CaseStoreState;
  subscribe(listener: () => void): () => void;
  /**
   * Opens a case: creates it when the contract has none, otherwise records a visit (which feeds the
   * "since your last visit" summary). `branch: "notice"` opens straight onto the non-renewal notice.
   */
  open(cid: string, ctx: CaseContext, opts: CreateOptions & { branch?: "notice" | null }): void;
  /** Applies one action. Unknown contracts are ignored. */
  act(cid: string, type: ActionType, payload: ActPayload | undefined, ctx: CaseContext): void;
  /** Back to the seeded demo data. */
  reset(): void;
}

export interface CaseStoreOptions {
  /** Where cases persist; `null` keeps them in memory. */
  storage?: Pick<Storage, "getItem" | "setItem" | "removeItem"> | null;
  storageKey?: string;
  effects?: StoreEffects;
  seed?: () => Record<string, Case>;
}

export const CASE_STORE_KEY = "raffa.renewalCases.v1";
const TICK_MS = 900;

const browserEffects: StoreEffects = {
  copyToClipboard(text) {
    try {
      void navigator.clipboard?.writeText(text);
    } catch {
      // Copying is a convenience: the email stays on screen to select by hand.
    }
  },
  later: (run, ms) => {
    setTimeout(run, ms);
  },
};

function isCase(value: unknown): value is Case {
  const c = value as Partial<Case> | null;
  return !!c && typeof c === "object" && typeof c.cid === "string" && Array.isArray(c.feed) && Array.isArray(c.emails) && typeof c.phase === "string";
}

function load(storage: CaseStoreOptions["storage"], key: string): Record<string, Case> | null {
  if (!storage) return null;
  try {
    const raw = storage.getItem(key);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { cases?: Record<string, unknown> } | null;
    const entries = Object.entries(parsed?.cases ?? {});
    if (!entries.length || !entries.every(([, value]) => isCase(value))) return null;
    return Object.fromEntries(entries) as Record<string, Case>;
  } catch {
    return null;
  }
}

export function createCaseStore(options: CaseStoreOptions = {}): CaseStore {
  const storage = options.storage === undefined ? browserStorage() : options.storage;
  const key = options.storageKey ?? CASE_STORE_KEY;
  const effects = options.effects ?? browserEffects;
  const seed = options.seed ?? (() => ({}));
  const listeners = new Set<() => void>();
  let state: CaseStoreState = { cases: load(storage, key) ?? seed() };

  const commit = (cases: Record<string, Case>): void => {
    state = { cases };
    try {
      storage?.setItem(key, JSON.stringify({ cases }));
    } catch {
      // Private mode or a full quota: the session still works, it just does not persist.
    }
    listeners.forEach((listener) => listener());
  };

  const store: CaseStore = {
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    open(cid, ctx, { branch = null, ...create_ }) {
      const existing = state.cases[cid];
      let next: Case;
      if (!existing) {
        next = create(cid, ctx, branch, create_);
      } else {
        next = existing;
        if (branch === "notice" && !existing.notice) next = act(next, "startNotice", {}, ctx);
        next = act(next, "visit", {}, ctx);
      }
      commit({ ...state.cases, [cid]: next });
    },
    act(cid, type, payload, ctx) {
      const current = state.cases[cid];
      if (!current) return;
      const result = act(current, type, payload, ctx);
      const { clip, tick, ...next } = result;
      commit({ ...state.cases, [cid]: next as Case });
      if (clip) effects.copyToClipboard(clip);
      if (tick) effects.later(() => store.act(cid, "tick", {}, ctx), TICK_MS);
    },
    reset() {
      try {
        storage?.removeItem(key);
      } catch {
        // nothing persisted to clear
      }
      commit(seed());
    },
  };
  return store;
}

function browserStorage(): CaseStoreOptions["storage"] {
  try {
    return typeof window === "undefined" ? null : window.localStorage;
  } catch {
    return null;
  }
}
