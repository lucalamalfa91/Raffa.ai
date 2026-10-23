import { useMemo } from "react";
import { useLocation } from "react-router-dom";
import { useI18n } from "../../../i18n/I18nProvider";
import { rememberedViewer } from "./demoSession";
import { DEMO_APPROVER, DEMO_PEOPLE, demoContext } from "./fixtures";
import { buildQueue, buildSummaries, snoozedItems, visibleQueue } from "./portfolio";
import { getCaseStore, useCaseStore } from "./useCaseStore";
import { resolveViewer } from "./viewer";

/**
 * The person looking, the language, and what is waiting -- derived from the cases and the person's
 * own snoozed / finished items. The light half of `useRenewalsWorkspace`: the rail uses it for the
 * Today count without pulling in navigation and actions.
 */
export function useQueue(userLabel: string) {
  const { lang, t } = useI18n();
  const location = useLocation();
  const store = getCaseStore();
  const state = useCaseStore(store);
  const viewer = resolveViewer({ userLabel, as: rememberedViewer(location.search), members: DEMO_PEOPLE, approver: DEMO_APPROVER });
  const today = demoContext("1", viewer, lang).today;

  const summaries = useMemo(() => buildSummaries(state.cases, viewer, lang), [state.cases, viewer, lang]);
  const queueAll = useMemo(() => buildQueue(state.cases, viewer, lang), [state.cases, viewer, lang]);
  const queue = useMemo(() => visibleQueue(queueAll, state.queue, today), [queueAll, state.queue, today]);
  const snoozed = useMemo(() => snoozedItems(queueAll, state.queue, today), [queueAll, state.queue, today]);

  return { store, cases: state.cases, viewer, lang, t, today, summaries, queueAll, queue, snoozed };
}
