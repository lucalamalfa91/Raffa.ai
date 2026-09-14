import { useCallback, useEffect, useRef, useState } from "react";

/** R-DOC-09: "polled every 2 s until terminal." The one cadence every "not ready yet" surface
 * re-reads on (ADR-012 w15 §4, ADR-020 w15 §8.3). */
export const POLL_INTERVAL_MS = 2000;

/**
 * ADR-012 w15 §17 (assumption in force): five minutes with no change in what the server last said,
 * after which the interval stops. ADR-027 §C6 withdrew the guarantee that every row reaches a
 * terminal status, so a poll that only stops on "terminal" can run for as long as a tab stays open
 * -- ~1,800 requests an hour -- under a "not ready yet" state that is true and never resolves.
 */
export const POLL_NO_CHANGE_BUDGET_MS = 5 * 60_000;

export interface PollBudgetOptions {
  /** Poll only while this is true -- the surface's own predicate, read from the server (a non-terminal
   * row on Documents, an in-flight count on Ask/Portfolio, `readiness.state === "processing"` on
   * Contract 360). The budget gates the *interval*, never the meaning of what is on screen. */
  active: boolean;
  /** Changes whenever the server's last answer changes; the budget counts from the last change. */
  fingerprint: string;
  /** The re-read. Called every `intervalMs` while active and not paused, and once on `resume`. */
  onTick: () => void;
  intervalMs?: number;
  budgetMs?: number;
}

export interface PollBudgetState {
  /** True once the budget elapsed with no change: rows stay exactly as last reported, and the surface
   * renders ADR-020 w15 §8's one notice with its one resume control. */
  paused: boolean;
  /** "Check again": restarts the budget and re-reads immediately. A reload restarts it too. */
  resume: () => void;
}

/**
 * The stopped-poll rule, stated once so no surface rediscovers it (ADR-018 w15 round-3 clause 6):
 * a "not ready yet" state that depends on a repeating re-read stops on the same budget and offers
 * the same resume. Three things it deliberately never does (ADR-012 w15 §17): re-label a row, feed
 * a progress bar from elapsed time, or become a second definition of terminal.
 */
export function usePollBudget({
  active,
  fingerprint,
  onTick,
  intervalMs = POLL_INTERVAL_MS,
  budgetMs = POLL_NO_CHANGE_BUDGET_MS,
}: PollBudgetOptions): PollBudgetState {
  const [paused, setPaused] = useState(false);
  const lastChangeRef = useRef(Date.now());
  const tickRef = useRef(onTick);
  tickRef.current = onTick;

  useEffect(() => {
    lastChangeRef.current = Date.now();
  }, [fingerprint]);

  useEffect(() => {
    if (!active || paused) return;
    const interval = setInterval(() => {
      if (Date.now() - lastChangeRef.current >= budgetMs) {
        setPaused(true);
        return;
      }
      tickRef.current();
    }, intervalMs);
    return () => clearInterval(interval);
  }, [active, paused, intervalMs, budgetMs]);

  // The state resolved (nothing left to wait for): a pause from the previous wait must not leak
  // into the next one.
  useEffect(() => {
    if (!active) setPaused(false);
  }, [active]);

  const resume = useCallback(() => {
    lastChangeRef.current = Date.now();
    setPaused(false);
    tickRef.current();
  }, []);

  return { paused, resume };
}

/** The notice and its control, verbatim on every surface that waits (ADR-020 w15 §8.2): one fact,
 * one wording. The actor is *the page* -- never Raffa.ai, which has not failed and may not even be
 * working on the file. */
export const UPDATES_PAUSED_NOTICE = "Nothing has changed for five minutes, so this page stopped checking for updates.";
export const CHECK_AGAIN_LABEL = "Check again";
