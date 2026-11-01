import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import type { TodayFeed, TodayItem } from "../../api/renewalCases";
import { useGuide, useNavBadge } from "../../components/shell/guide";
import { useTenantId } from "../../routes/contracts/contracts4";
import { useI18n } from "../../i18n/I18nProvider";
import { resolveText, type Text } from "../../i18n/translate";
import { CONTRACTS_CHANGED, loadToday, notifyTodayChanged } from "./loadToday";
import { useCaseNavigation } from "./useCaseNavigation";
import "./today4.css";

export interface TodayRouteProps {
  apiClient: ApiClient;
}

const IT_MONTHS: Record<string, string> = { Jan: "Gen", May: "Mag", Jun: "Giu", Jul: "Lug", Aug: "Ago", Sep: "Set", Oct: "Ott", Dec: "Dic" };

/** "08 Sep 2026" → "08 Set 2026" in Italian (the role line; contract dates stay as written). */
export function localDate(date: string, lang: string): string {
  return lang === "it" ? date.replace(/\b(Jan|May|Jun|Jul|Aug|Sep|Oct|Dec)\b/, (m) => IT_MONTHS[m] ?? m) : date;
}

/** The kinds where the CPO can hand the step to the buyer instead of doing it ("Delega a Marta"). */
const DELEGABLE = ["approve", "policy", "sigok", "nout", "alt"];

/** The item's explanation: the situation assessment's (written in both languages) when there is one. */
export function itemWhy(item: TodayItem, lang: string, tr: (x: Text | null | undefined) => string): string {
  return item.assessment ? (lang === "it" ? item.assessment.why.it : item.assessment.why.en) : tr(item.why);
}

/** Days left, never negative: a passed deadline shows no count (the item says what happened instead). */
export function dueDaysShown(item: TodayItem): number | null {
  return item.kind === "confirmTerm" || item.dueDays < 0 ? null : item.dueDays;
}

/**
 * Oggi (AskRaffa V4.dc.html lines 49-98): one next action, then what follows. Never a dashboard.
 * Header (role line, title, subtitle); "Punto zero" on an empty workspace; progress dots + the Focus
 * card (max 3 buttons) with "Poi, per te" and "In attesa di altri"; the calm state; "Rimandate".
 */
export default function TodayRoute({ apiClient }: TodayRouteProps) {
  const tenantId = useTenantId() ?? undefined;
  const { t, lang } = useI18n();
  const tr = useCallback((x: Text | null | undefined) => resolveText(x, lang), [lang]);
  const navigate = useNavigate();
  const setBadge = useNavBadge();
  const [feed, setFeed] = useState<TodayFeed | null>(null);
  const [error, setError] = useState<string | null>(null);
  const { openItem } = useCaseNavigation(apiClient, tenantId);

  const load = useCallback(async () => {
    if (!tenantId) return;
    const r = await loadToday(apiClient, tenantId);
    if (r.ok) {
      setFeed(r.body);
      setError(null);
    } else {
      setError(r.error);
    }
  }, [apiClient, tenantId]);

  useEffect(() => {
    void load();
    const reload = () => void load();
    window.addEventListener(CONTRACTS_CHANGED, reload);
    return () => window.removeEventListener(CONTRACTS_CHANGED, reload);
  }, [load]);

  useEffect(() => {
    if (feed) setBadge(feed.mine.length || null);
  }, [feed, setBadge]);

  const snooze = useCallback(
    async (key: string, on: boolean) => {
      if (!tenantId || !apiClient.snoozeTodayItem) return;
      const r = await apiClient.snoozeTodayItem(tenantId, key, on);
      if (r.ok) {
        await load();
        notifyTodayChanged();
      }
    },
    [apiClient, tenantId, load],
  );

  const startSample = useCallback(async () => {
    if (!tenantId) return;
    const doc = feed?.documents[0];
    if (doc) {
      navigate(`/contracts/review/${doc.documentId}`);
      return;
    }
    const r = await apiClient.uploadSampleDocument(tenantId);
    const documentId = r.ok ? (r.data as { documentId?: string } | null)?.documentId : undefined;
    if (documentId) navigate(`/contracts/review/${documentId}`);
    else setError(r.error ?? t("The sample contract could not be uploaded."));
  }, [apiClient, tenantId, feed, navigate, t]);

  const delegate = useCallback(
    async (item: TodayItem) => {
      if (!tenantId || !item.caseId || !feed?.assignee || !apiClient.postRenewalCaseAction) return;
      await apiClient.postRenewalCaseAction(tenantId, item.caseId, "delegate", { who: feed.assignee.name, on: true }, null);
      await load();
    },
    [apiClient, tenantId, feed, load],
  );

  const viewer = feed?.viewer;
  const mine = feed?.mine ?? [];
  const team = feed?.waiting ?? [];
  const f = mine[0] ?? null;
  const startOn = !!feed?.emptyWorkspace;
  const hasDoc = (feed?.documents.length ?? 0) > 0;
  const stP = hasDoc ? 2 : 0;
  const assigneeFirst = feed?.assignee?.name.split(" ")[0] ?? "";

  const open = useCallback(
    (item: TodayItem) => {
      if (feed) void openItem(item, feed.viewer, feed.assignee);
    },
    [feed, openItem],
  );

  const title = startOn
    ? t("Start here.")
    : f
      ? mine.length === 1
        ? t("Today you have 1 thing to do.")
        : t("Today you have {n} things to do. One at a time.", { n: mine.length })
      : t("Nothing needs your action today.");
  const sub = startOn
    ? t(hasDoc ? "The reading is done. I only ask about the fields I am not sure of." : "No contract yet. Raffa works only on contracts that are read and verified: you upload the first, check the doubtful fields, and the renewal starts from there.")
    : f
      ? t("I start from the nearest deadline. Each card has one main action; everything else stays saved.")
      : team.length === 1
        ? t("1 step waits on a colleague or the supplier — see it in Renewals.")
        : team.length
          ? t("{n} steps wait on colleagues or suppliers — see them in Renewals.", { n: team.length })
          : t("No open renewal.");

  // "Start & assign to …" only with a real colleague to assign to; otherwise just start it.
  const canAssign = !!viewer?.isApprover && !!assigneeFirst;
  const focusCta = f
    ? f.kind === "unassigned"
      ? canAssign
        ? t("Start & assign to {name}", { name: assigneeFirst })
        : t("Start the renewal")
      : f.kind === "confirmTerm"
        ? t("Confirm the renewal term")
        : f.kind === "quote"
          ? t("Open the check")
          : t("Do it now — open the step")
    : "";
  const hasAlt = !!f && !!viewer?.isApprover && ((f.kind === "unassigned" && canAssign) || (DELEGABLE.includes(f.kind) && !!feed?.assignee));
  const fDays = f ? dueDaysShown(f) : null;
  const info = feed?.info ?? [];
  const altLabel = f?.kind === "unassigned" ? t("I’ll handle it") : t("Delegate to {name}", { name: assigneeFirst });

  const steps: Array<[string, string]> = [
    ["Upload the contract", "A PDF: dates, prices and notice terms are read page by page."],
    ["Raffa reads it", "A few seconds in the demo."],
    ["You verify the doubtful fields", "One at a time, with the quote from the document."],
    ["The renewal starts", "The notice deadline shows up here, in Today."],
  ];
  const startCta = hasDoc ? t("Verify the fields") : t("Upload the first contract");

  useGuide(
    !feed
      ? null
      : startOn
        ? {
            where: t("Today · start"),
            now: hasDoc ? t("Verify the two doubtful fields.") : t("Upload the first contract."),
            why: t("Raffa works only on read and verified contracts: a doubtful notice date cannot drive a decision."),
            next: t("Contract validated → the notice deadline appears in Today → you run the renewal through to signature."),
            cta: { label: startCta, onClick: () => void startSample() },
          }
        : {
            where: f ? t("Today · {n} things for you", { n: mine.length }) : t("Today · nothing pending"),
            now: f ? `${tr(f.title)} — ${f.supplier}` : t("Look at the renewals, or rest."),
            why: f ? itemWhy(f, lang, tr) : t("When a colleague closes a step that waits for you, it shows up here."),
            next: mine[1] ? t("Then {supplier}: {title}", { supplier: mine[1].supplier, title: tr(mine[1].title) }) : t("Then you are done."),
            cta: f ? { label: t("Do now"), onClick: () => open(f) } : { label: t("See the renewals"), onClick: () => navigate("/renewals") },
          },
  );

  const roleLine = viewer ? `${viewer.name} · ${viewer.title ? t(viewer.title) + " · " : ""}${localDate(viewer.date, lang)}` : "";

  if (!feed) {
    return (
      <div className="o4-page" data-screen-label="Oggi">
        {error ? <div className="o4-sub">{error}</div> : null}
      </div>
    );
  }

  return (
    <div className="o4-page" data-screen-label="Oggi">
      <h6 className="o4-role">{roleLine}</h6>
      <h2 className="o4-title">{title}</h2>
      <div className="o4-sub">{sub}</div>

      {startOn ? (
        <div className="o4-zero" data-screen-label="Punto zero">
          <div className="o4-zero-head">{t("Upload a contract. Everything else starts from there.")}</div>
          <div className="o4-zero-steps">
            {steps.map(([st, hint], i) => (
              <div key={st} className={`o4-zero-step${i === stP ? " is-now" : i < stP ? " is-done" : ""}`}>
                <div className="o4-zero-n">
                  {t("Step")} {i + 1}
                </div>
                <div className="o4-zero-t">{t(st)}</div>
                <div className="o4-zero-h">{t(hint)}</div>
              </div>
            ))}
          </div>
          <div className="o4-zero-cta">
            <button type="button" className="btn btn-primary" onClick={() => void startSample()}>
              {startCta}
            </button>
            {!hasDoc ? <span className="o4-note">{t("Demo: upload “Salesforce_MSA_2024.pdf”, the sample contract.")}</span> : null}
          </div>
        </div>
      ) : null}

      {f ? (
        <>
          <div className="o4-dots">
            {mine.map((x, i) => (
              <span key={x.key} className={i === 0 ? "is-now" : undefined} />
            ))}
          </div>
          <div className="o4-grid">
            <div className="o4-focus" data-screen-label="Focus">
              <div className="o4-kicker">
                <span className="o4-kicker-n">{t("1 of {n}", { n: mine.length })}</span>
                <span>{t("Your action")}</span>
                <span>{f.stage >= 0 ? t("Phase {n}/6 · {label}", { n: f.stage + 1, label: tr(f.stageLabel) }) : tr(f.stageLabel)}</span>
              </div>
              <div className="o4-supplier">
                {f.supplier} <span>{f.contract}</span>
              </div>
              <div className="o4-ftitle">{tr(f.title)}</div>
              <div className="o4-why">{itemWhy(f, lang, tr)}</div>
              <div className="o4-due">
                <span>{f.dueLabel ? tr(f.dueLabel) : t("Notice deadline")}</span>{" "}
                <strong className={fDays !== null && fDays <= 30 ? "is-urgent" : undefined}>
                  {fDays !== null ? (
                    <>
                      {f.due} · {t("{n} days", { n: fDays })}
                    </>
                  ) : (
                    f.due
                  )}
                </strong>
              </div>
              <div className="o4-actions">
                <button type="button" className="btn btn-primary o4-btn-main" onClick={() => open(f)}>
                  {focusCta}
                </button>
                {hasAlt ? (
                  <button
                    type="button"
                    className="btn btn-secondary o4-btn-alt"
                    onClick={() => {
                      if (f.kind === "unassigned" && f.contractId && viewer) {
                        void openItem({ ...f }, { ...viewer, isApprover: false }, null);
                      } else {
                        void delegate(f);
                      }
                    }}
                  >
                    {altLabel}
                  </button>
                ) : null}
                <button type="button" className="btn btn-ghost o4-btn-later" onClick={() => void snooze(f.key, true)}>
                  {t("Later · 3 days")}
                </button>
              </div>
            </div>
            <div className="o4-side">
              <div>
                <div className="o4-side-head">{t("Then, for you · {n}", { n: mine.length - 1 })}</div>
                {mine.slice(1).map((x) => (
                  <div key={x.key} className="r-row o4-row" onClick={() => open(x)}>
                    <span className="o4-row-sup">{x.supplier}</span>
                    <span className="o4-row-title">{tr(x.title)}</span>
                    <span className={(dueDaysShown(x) ?? 0) <= 30 ? "o4-row-days is-urgent" : "o4-row-days"}>
                      {dueDaysShown(x) !== null ? t("{n} d", { n: dueDaysShown(x) }) : t("to verify")}
                    </span>
                  </div>
                ))}
                {mine.length <= 1 ? <div className="o4-empty">{t("Nothing else: this is the only thing for today.")}</div> : null}
              </div>
              <div>
                <div className="o4-side-head">{t("Waiting on others · {n}", { n: team.length })}</div>
                {team.slice(0, 6).map((x) => (
                  <div
                    key={x.key}
                    className="r-row o4-row is-team"
                    onClick={() => {
                      if (x.caseId) navigate(`/renewals/${x.caseId}`);
                      else if (x.quoteId) navigate(`/quotes/${x.quoteId}`);
                    }}
                  >
                    <span className="o4-row-sup">{x.supplier}</span>
                    <span className="o4-row-title">{tr(x.title)}</span>
                    <span className="o4-row-who">{x.who === "Supplier" || x.who === "Fornitore" ? t("supplier") : x.who.split(" ")[0]}</span>
                  </div>
                ))}
                {team.length === 0 ? <div className="o4-empty">{t("No step waiting on colleagues or suppliers.")}</div> : null}
              </div>
            </div>
          </div>
        </>
      ) : null}

      {!f && !startOn ? (
        <div className="o4-calm">
          <div className="o4-calm-head">{t("You are done for today.")}</div>
          <div className="o4-calm-line">
            {feed.nextDeadline
              ? t("Next notice deadline: {supplier} on {date} ({n} days).", { supplier: feed.nextDeadline.supplier, date: feed.nextDeadline.date, n: feed.nextDeadline.days })
              : t("Upload the next contract when you have it.")}
          </div>
          <div className="o4-calm-actions">
            <button type="button" className="btn btn-secondary" onClick={() => navigate("/renewals")}>
              {t("See the renewals")}
            </button>
          </div>
        </div>
      ) : null}

      {info.length > 0 ? (
        <div className="o4-info" data-screen-label="Per tua informazione">
          <div className="o4-snoozed-head">{t("For your information")}</div>
          {info.map((x) => (
            <div key={x.key} className="o4-snoozed-row o4-info-row">
              <strong>{x.supplier}</strong>
              <span className="o4-info-text">
                <span className="o4-info-title">{x.assessment ? (lang === "it" ? x.assessment.headline.it : x.assessment.headline.en) : tr(x.title)}</span>
                <span className="o4-info-why">{itemWhy(x, lang, tr)}</span>
              </span>
              <button type="button" className="btn btn-ghost" onClick={() => void snooze(x.key, true)}>
                {t("Got it")}
              </button>
            </div>
          ))}
        </div>
      ) : null}

      {feed.snoozed.length > 0 ? (
        <div className="o4-snoozed">
          <div className="o4-snoozed-head">{t("Postponed")}</div>
          {feed.snoozed.map((z) => (
            <div key={z.key} className="o4-snoozed-row">
              <strong>{z.supplier}</strong>
              <span>{tr(z.title)}</span>
              <button type="button" className="btn btn-ghost" onClick={() => void snooze(z.key, false)}>
                {t("Resume now")}
              </button>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}
