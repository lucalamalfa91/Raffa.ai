import type { Translate } from "../../i18n/translate";
import type { GuideContent } from "./guide";

/**
 * Guida Raffa's screen-level copy for when the screen has not stated its own (`useGuide`): the
 * static parts of the prototype's `const G = …` (AskRaffa V4.dc.html), per route. Message ids are
 * the prototype's English (Italian in `i18n/it/shell.ts`). Contextual copy (a case step, a contract
 * name) is the screens' job.
 */
interface Fallback {
  where: string;
  now: string;
  why: string;
  next: string;
  cta?: { label: string; to: string };
}

const TODAY: Fallback = {
  where: "Today · start",
  now: "Upload the first contract.",
  why: "Raffa works only on read and verified contracts: a doubtful notice date cannot drive a decision.",
  next: "Contract validated → the notice deadline appears in Today → you run the renewal through to signature.",
  cta: { label: "Upload the first contract", to: "/contracts/review/new" },
};
const RENEWALS: Fallback = {
  where: "Renewals⟦guide⟧",
  now: "No step waits for you.",
  why: "Sorted by notice deadline. The work happens in the case, one step at a time.",
  next: "To start: within 120 days of the notice deadline.",
};
const CASE: Fallback = {
  where: "Renewals⟦guide⟧",
  now: "",
  why: "One step at a time: everything else stays saved in the details.",
  next: "After the questions I draft the plan; you confirm it.",
};
const CONTRACTS: Fallback = {
  where: "Validated contracts",
  now: "Open a contract or upload the next one.",
  why: "Only validated contracts feed Today and Renewals.",
  next: "A new file goes reading → verification → validation.",
  cta: { label: "Upload a contract", to: "/contracts/review/new" },
};
const CONTRACT: Fallback = {
  where: "Contracts⟦guide⟧",
  now: "",
  why: "Decisive dates and negotiable terms first; everything else on request.",
  next: "Decisions flow into the renewal plan.",
};
const REVIEW: Fallback = {
  where: "Document⟦guide⟧",
  now: "",
  why: "I ask you only about uncertain or conflicting fields; a high score never replaces your check.",
  next: "Once validated, the contract enters Today, Renewals and Contracts.",
};
const RESULTS: Fallback = {
  where: "Results⟦guide⟧",
  now: "Write your goal in your own words, then read what is signed and what is missing.",
  why: "Raffa never estimates: a figure exists only with a signed contract or an invoice. The goal is yours and the page adapts to it.",
  next: "With the goal set you see how much is covered and which renewals can close it.",
};
const QUOTE: Fallback = {
  where: "Quote⟦guide⟧",
  now: "",
  why: "",
  next: "After the decision, emails and signature follow the case.",
};
const ASK: Fallback = {
  where: "Ask Raffa",
  now: "Write a question or pick a task.",
  why: "I answer only from validated contracts, with the source — or I say I cannot. Conversations stay on the left: recent, cases and archived.",
  next: "A task becomes a guided case; an archived conversation reopens when you write in it.",
};

export function fallbackFor(pathname: string): Fallback {
  const path = pathname.replace(/\/+$/, "") || "/";
  if (path === "/" || path.startsWith("/today")) return TODAY;
  if (path === "/renewals") return RENEWALS;
  if (path.startsWith("/renewals/")) return CASE;
  if (path.startsWith("/contracts/review/") || /^\/contracts\/[^/]+\/review$/.test(path) || path.startsWith("/documents")) return REVIEW;
  if (path === "/contracts") return CONTRACTS;
  if (path.startsWith("/contracts/")) return CONTRACT;
  if (path.startsWith("/savings")) return RESULTS;
  if (path.startsWith("/quotes")) return QUOTE;
  return ASK;
}

/** The fallback for `pathname`, translated; the CTA navigates with `go`. */
export function fallbackGuide(pathname: string, t: Translate, go: (to: string) => void): GuideContent {
  const f = fallbackFor(pathname);
  const text = (id: string) => (id ? t(id) : "");
  return {
    where: text(f.where),
    now: text(f.now),
    why: text(f.why),
    next: text(f.next),
    cta: f.cta ? { label: t(f.cta.label), onClick: () => go(f.cta!.to) } : null,
  };
}
