import { useEffect } from "react";
import { askOfferApi, type ApiClient } from "../../api/client";
import type { CaseCallResult, TodayFeed } from "../../api/renewalCases";
import { useNavBadge } from "../../components/shell/guide";
import { withQuotes } from "./quoteItems";

/** The contracts slice fires this after a validation or a decision (Oggi may gain or lose items). */
export const CONTRACTS_CHANGED = "raffa4:contracts-changed";
/** Fired by Oggi, Rinnovi and the case after anything that can change the caller's items. */
export const TODAY_CHANGED = "raffa4:today-changed";

export function notifyTodayChanged(): void {
  try {
    window.dispatchEvent(new Event(TODAY_CHANGED));
  } catch {
    // no window (tests): nothing listens anyway
  }
}

/** `GET /api/today` plus the checked quotes waiting for a decision (Quotes slice). */
export async function loadToday(apiClient: ApiClient, tenantId: string): Promise<CaseCallResult<TodayFeed>> {
  if (!apiClient.getToday) {
    return { ok: false, statusCode: null, body: null, error: "Today is not available.", code: null, current: null };
  }

  const [r, quotes] = await Promise.all([
    apiClient.getToday(tenantId),
    askOfferApi(apiClient)
      .listQuotes(tenantId, ["checked"])
      .catch(() => null),
  ]);
  if (!r.ok) return r;
  return { ...r, body: quotes?.ok && quotes.data ? withQuotes(r.body, quotes.data) : r.body };
}

/**
 * The Oggi tab badge (README "Top nav": the number of items for the current user) on every screen:
 * read once per screen change and again whenever something changes the caller's items.
 */
export function useTodayBadge(apiClient: ApiClient, tenantId: string | undefined, refreshKey: string): void {
  const setBadge = useNavBadge();
  useEffect(() => {
    if (!tenantId) return undefined;
    let alive = true;
    const run = () => {
      void loadToday(apiClient, tenantId).then((r) => {
        if (alive && r.ok) setBadge(r.body.mine.length || null);
      });
    };
    run();
    window.addEventListener(CONTRACTS_CHANGED, run);
    window.addEventListener(TODAY_CHANGED, run);
    return () => {
      alive = false;
      window.removeEventListener(CONTRACTS_CHANGED, run);
      window.removeEventListener(TODAY_CHANGED, run);
    };
  }, [apiClient, tenantId, refreshKey, setBadge]);
}
