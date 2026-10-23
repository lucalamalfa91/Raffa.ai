/** The six fixed tabs of a renewal case, in order. `now` is the default. */
export const TAB_KEYS = ["now", "terms", "timeline", "mail", "clauses", "team"] as const;
export type TabKey = (typeof TAB_KEYS)[number];

export function parseTab(value: string | null): TabKey {
  return (TAB_KEYS as readonly string[]).includes(value ?? "") ? (value as TabKey) : "now";
}
