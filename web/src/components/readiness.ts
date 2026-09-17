/**
 * Ready vs still-to-review split for Portfolio and Renewals. Presentation-only over the
 * already-loaded page (same shape as Documents' attention chips and Portfolio's in-header
 * column filters): nothing here re-fetches or writes to storage.
 *
 * Default is already-OK so the list is usable the moment contracts are validated; the
 * still-to-review bucket stays one click away and is never hidden forever.
 *
 * Named `readiness.ts` (not `readinessFilter.ts`) so Windows' case-insensitive resolver
 * cannot collide with `ReadinessFilter.tsx`.
 */

export type ReadinessFilterValue = "ok" | "review" | "all";

export const DEFAULT_READINESS_FILTER: ReadinessFilterValue = "ok";

export interface ReadinessCounts {
  ok: number;
  review: number;
  all: number;
}

export function countReadiness(okCount: number, reviewCount: number): ReadinessCounts {
  return { ok: okCount, review: reviewCount, all: okCount + reviewCount };
}

export function filterByReadiness<T>(
  items: readonly T[],
  filter: ReadinessFilterValue,
  isReady: (item: T) => boolean,
): readonly T[] {
  switch (filter) {
    case "ok":
      return items.filter(isReady);
    case "review":
      return items.filter((item) => !isReady(item));
    case "all":
      return items;
  }
}

export function getReadinessFilterHint(filter: ReadinessFilterValue): string {
  switch (filter) {
    case "ok":
      return "Contracts still in review are hidden — they are not ready to use yet.";
    case "review":
      return "Only contracts still being analyzed or waiting for review.";
    case "all":
      return "Ready contracts and those still to review.";
  }
}

export function getReadinessEmptyCopy(filter: ReadinessFilterValue): string {
  switch (filter) {
    case "ok":
      return "No ready contracts in this list. Switch to To review to see uploads still being analyzed.";
    case "review":
      return "Nothing is waiting for review. Switch to Ready to see contracts already OK to use.";
    case "all":
      return "No contracts match this view.";
  }
}
