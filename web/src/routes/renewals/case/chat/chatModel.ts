/**
 * Pure model of the renewal chat: which cards are the *current step* and which are history.
 *
 * The chat shows only what the user must act on now. Everything earlier is grouped by wizard step
 * into collapsible rows ("Steps so far") that open a read-only drawer. The rules come from the design
 * prototype and are kept here, away from React, so they can be tested on their own.
 */

import type { Card, CaseView } from "../types";

export interface HistoryRow {
  id: string;
  at: string;
  label: string;
  note: string;
}

export interface HistoryGroup {
  /** 1-based wizard step number. */
  n: number;
  label: string;
  rows: HistoryRow[];
}

export interface ChatSplit {
  /** The current step: from the first card to act on (and the Raffa message just before it) on. */
  current: Card[];
  history: HistoryGroup[];
  /** Every card the history rows and the drawer can show, by id. */
  byId: ReadonlyMap<string, Card>;
}

const MAX_SUMMARY = 90;

function clip(text: string): string {
  return text.length > MAX_SUMMARY ? `${text.slice(0, MAX_SUMMARY - 2)}…` : text;
}

/** One line for a card in the history list. */
export function summaryOf(card: Card): string {
  if (card.plain) return `${card.by.split(" ")[0]}: ${clip(card.text)}`;
  return `${card.kicker} · ${card.title}`;
}

/** The short status shown at the right of a history row (the email's state, else the first line). */
export function noteOf(card: Card): string {
  return card.email ? card.email.state : (card.lines[0] ?? "");
}

export function splitFeed(cards: readonly Card[], steps: CaseView["steps7"]): ChatSplit {
  // The "what you can expect" table lives in its own tab, not in the chat.
  const visible = cards.filter((card) => card.k !== "outlook");
  const firstCurrent = visible.findIndex((card) => card.current);
  let start = firstCurrent >= 0 ? firstCurrent : visible.length - 1;
  const before = visible[start - 1];
  if (start > 0 && before?.plain && before.by === "Raffa.ai") start -= 1;
  start = Math.max(0, start);

  const current = visible.slice(start);
  // History rows are the step cards, plus what the user wrote -- except an answer that the question
  // card above it already echoes ("Answer: …"), and Raffa's own chatter.
  const rows = visible.slice(0, start).filter((card, i, all) => {
    if (!card.plain) return true;
    return card.k === "user" && all[i - 1]?.k !== "q";
  });

  const history = steps
    .map((step, index): HistoryGroup => ({
      n: step.n,
      label: step.label,
      rows: rows
        .filter((card) => card.st === index)
        .map((card): HistoryRow => ({ id: card.id, at: card.at, label: summaryOf(card), note: noteOf(card) })),
    }))
    .filter((group) => group.rows.length > 0);

  return { current, history, byId: new Map(visible.map((card) => [card.id, card])) };
}
