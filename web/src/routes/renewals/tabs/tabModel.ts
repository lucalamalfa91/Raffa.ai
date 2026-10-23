/**
 * What the case tabs show, derived from the engine's `CaseView` (pure): the count on each tab, the
 * step guide of "Do now", and the lines under it.
 */

import type { Translate } from "../../../i18n/translate";
import type { CaseView, ChecklistItem } from "../case/types";
import type { TabKey } from "../caseTabs";

/** The small count after each tab's name; empty when there is nothing to count yet. */
export function tabBadges(v: CaseView): Record<TabKey, string> {
  const current = v.steps7.findIndex((s) => s.now);
  return {
    now: current >= 0 ? `${current + 1}/7` : "✓",
    terms: v.summary.terms.length ? String(v.summary.terms.length) : "",
    timeline: String(v.upcoming.length + v.timeline.length),
    mail: String(v.emailsList.length + v.docsList.length),
    clauses: String(v.outlook.rows.length),
    team: String(v.hdr.people.length),
  };
}

export interface WizardRow {
  /** "✓" when done, else the 1-based position. */
  mark: string;
  label: string;
  done: boolean;
  /** The one thing to do now. */
  now: boolean;
  /** "Done", "Who: Marta", or "To do". */
  who: string;
}

/** The step's checklist as the guide: what is done first, then what is next, then the rest. */
export function wizardRows(check: readonly ChecklistItem[], viewer: string, t: Translate): WizardRow[] {
  const ordered = [...check].sort((a, b) => Number(b.done) - Number(a.done));
  const nowAt = ordered.findIndex((item) => !item.done);
  return ordered.map((item, i) => {
    const name = item.who ? (item.who === viewer ? t("you") : (item.who.split(" ")[0] ?? item.who)) : "";
    return {
      mark: item.done ? "✓" : String(i + 1),
      label: item.label,
      done: item.done,
      now: i === nowAt,
      who: item.done ? t("Done") : name ? t("Who: {name}", { name }) : t("To do"),
    };
  });
}

/** "Step 3 / 7 · Negotiation", or "Closed" once the case has ended. */
export function stepLine(v: CaseView, t: Translate): string {
  const now = v.steps7.find((s) => s.now);
  return now ? `${t("Step {n} / 7", { n: now.n })} · ${now.label}` : t("Closed");
}

export function stepTitle(v: CaseView): string {
  return v.steps7.find((s) => s.now)?.label ?? "";
}

/** "Next step: Agreement", or that this is the last one. */
export function nextStepLine(v: CaseView, t: Translate): string {
  const at = v.steps7.findIndex((s) => s.now);
  const next = at >= 0 ? v.steps7[at + 1] : undefined;
  return next ? t("Next step: {step}", { step: next.label }) : t("Last step of the renewal");
}
