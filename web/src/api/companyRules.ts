/**
 * Company rules (Impostazioni) -- the response shapes of
 * `backend/src/Raffa.Api/CompanyRulesEndpointExtensions.cs` (camelCase). The rules are free text
 * Raffa reads, plus the alerting lead time taken from them.
 */

export interface CompanyRules {
  text: string;
  /** Days before a notice deadline from which a renewal shows on Today. */
  startWindowDays: number;
  /** The policy document the rules were taken from, when one was uploaded. */
  sourceFile: string | null;
  updatedAt: string | null;
  updatedBy: string | null;
  /** Nothing saved yet: the defaults apply. */
  isDefault: boolean;
}

export interface CompanyRulesBody {
  rules: CompanyRules;
  /** Only a workspace Admin (the Head of Procurement) changes the rules. */
  canEdit: boolean;
}

export interface RuleTurn {
  role: "raffa" | "user";
  text: string;
}

/** The next question, or -- when Raffa has enough -- the whole rules text to keep or edit. */
export interface RuleInterviewReply {
  message: string;
  proposal: string | null;
  startWindowDays: number | null;
  done: boolean;
}

/** What Raffa took from an uploaded policy; nothing is saved until the user saves. */
export interface PolicyProposal {
  fileName: string;
  text: string;
  startWindowDays: number | null;
  summary: string;
}

export const DEFAULT_START_WINDOW_DAYS = 120;
export const MIN_START_WINDOW_DAYS = 14;
export const MAX_START_WINDOW_DAYS = 365;
export const MAX_RULES_LENGTH = 12000;

/** The lead time as the API keeps it: a whole number of days within bounds. */
export function clampWindow(days: number): number {
  const n = Number.isFinite(days) ? Math.round(days) : DEFAULT_START_WINDOW_DAYS;
  return Math.min(MAX_START_WINDOW_DAYS, Math.max(MIN_START_WINDOW_DAYS, n));
}
