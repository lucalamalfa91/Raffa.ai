/**
 * Date arithmetic shared by every screen that counts down to a contract date (Portfolio's "Give
 * notice by · N d", Renewals' "Notice in", Contract 360's "in N days"). This module used to also hold
 * the Day-1 Portfolio's severity/attention-bucket model (`computeAttentionRow`, the four attention
 * buckets, the seven filter chips' state) -- the V2 Portfolio (ADR-024; screens-v2.md #6) has no
 * attention strip or filter chips, only "validated contracts sorted by notice deadline, urgent rows
 * tinted" (`./portfolioViewModel.ts`), so only the one helper every screen still needs survives here.
 */

/**
 * Whole days from `now` (UTC midnight) to a `yyyy-MM-dd` date (UTC midnight): negative once the date
 * is past, `null` when there is no date. Both sides are taken at UTC midnight -- a date-only value has
 * no time-of-day, and comparing it against a local-time `now` would shift the count by one near
 * midnight in any timezone west of UTC.
 */
export function daysUntil(dateOnly: string | null, now: Date = new Date()): number | null {
  if (dateOnly === null) return null;

  const [year, month, day] = dateOnly.split("-").map(Number);
  const deadlineUtc = Date.UTC(year, month - 1, day);
  const todayUtc = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());

  return Math.round((deadlineUtc - todayUtc) / 86_400_000);
}
