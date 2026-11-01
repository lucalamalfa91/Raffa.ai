import type { QuoteListItem4 } from "../../api/client";
import type { TodayFeed, TodayItem } from "../../api/renewalCases";
import { msg } from "../../i18n/translate";

const MONTHS: Record<string, number> = { Jan: 0, Feb: 1, Mar: 2, Apr: 3, May: 4, Jun: 5, Jul: 6, Aug: 7, Sep: 8, Oct: 9, Nov: 10, Dec: 11 };

/** "08 Sep 2026" or "2026-09-08" → UTC day number. */
export function dayNumber(date: string): number | null {
  const iso = /^(\d{4})-(\d{2})-(\d{2})/.exec(date);
  if (iso) return Date.UTC(+iso[1]!, +iso[2]! - 1, +iso[3]!) / 864e5;
  const dm = /^(\d{1,2}) (\w{3}) (\d{4})$/.exec(date.trim());
  if (dm && MONTHS[dm[2]!] !== undefined) return Date.UTC(+dm[3]!, MONTHS[dm[2]!]!, +dm[1]!) / 864e5;
  return null;
}

function display(date: string): string {
  const n = dayNumber(date);
  if (n === null) return date;
  const d = new Date(n * 864e5);
  const m = Object.keys(MONTHS)[d.getUTCMonth()];
  return `${String(d.getUTCDate()).padStart(2, "0")} ${m} ${d.getUTCFullYear()}`;
}

/**
 * Quote decisions in Oggi (prototype `todo` "quote" items): every checked quote waiting for a decision,
 * owned by whoever checked it, due on its validity date. Read from the quotes slice
 * (`GET /api/quotes?status=checked`) and merged into the server's feed by due date.
 */
export function withQuotes(feed: TodayFeed, quotes: QuoteListItem4[]): TodayFeed {
  const today = dayNumber(feed.viewer.date) ?? 0;
  const items: TodayItem[] = quotes
    .filter((q) => q.status === "checked")
    .map((q) => {
      const due = q.validUntil ?? feed.viewer.date;
      return {
        key: `q:${q.id}`,
        kind: "quote",
        caseId: q.caseId,
        contractId: q.contractId,
        quoteId: q.id,
        documentId: null,
        supplier: q.supplier,
        contract: "",
        title: msg("Decide on the proposal {supplier}", { supplier: q.supplier }),
        why: msg(
          q.contractId
            ? "Comparison with your contract is available."
            : "No price comparison available: your current price for these lines is missing. You can still verify the lines, the term and the missing clauses.",
        ),
        due: display(due),
        dueDays: (dayNumber(due) ?? today) - today,
        dueLabel: msg("Proposal valid until"),
        stage: -1,
        stageLabel: msg("Quotes"),
        who: q.checkedBy ?? feed.viewer.name,
        stalled: 0,
      };
    });
  const snoozedKeys = new Set(feed.snoozed.map((x) => x.key));
  const visible = items.filter((x) => !snoozedKeys.has(x.key));
  const byDue = (a: TodayItem, b: TodayItem) => a.dueDays - b.dueDays;
  return {
    ...feed,
    mine: [...feed.mine, ...visible.filter((x) => x.who === feed.viewer.name)].sort(byDue),
    waiting: [...feed.waiting, ...visible.filter((x) => x.who !== feed.viewer.name)].sort(byDue),
  };
}
