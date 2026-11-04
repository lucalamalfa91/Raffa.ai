import { useCallback, useEffect, useMemo, useState, type KeyboardEvent } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import AskShortcuts from "../../components/ask/AskShortcuts";
import type {
  ApiClient,
  SavingsGoalBody,
  SavingsGoalDraftBody,
  SavingsGoalProgressBody,
} from "../../api/client";
import type { SpendReport } from "../../api/spend";
import { useGuide } from "../../components/shell/guide";
import { useShellContext } from "../../components/shell/shellContext";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { useI18n } from "../../i18n/I18nProvider";
import { buildGoalView, GOAL_EXAMPLES, tm, type ApiMsg } from "./resultsViewModel";
import { ExcludedNote, SpendBudget, SpendFiltersBar, SpendLedger, SpendOverview, SpendPivot, SpendSectionNav } from "./SpendSections";
import { readFilters, writeFilters, type SpendFilters } from "./spendViewModel";
import "./results4.css";
import "./spend4.css";

export interface SavingsRouteProps {
  apiClient: ApiClient;
}

type Load<T> = { phase: "loading" } | { phase: "ready"; data: T } | { phase: "error"; message: string };

/**
 * Spesa e risparmi (formerly Risultati): what the contracts cost in the periods they run, what is
 * committed, what renews by itself, what is still to decide -- and, on the same periods, what the
 * signed results save (contractual, proven by invoices, quantity reductions apart, the cases'
 * objectives). One page, one set of filters (in the URL), every figure from
 * `GET /api/savings/spend`; the goal bar and "Verso il tuo obiettivo" are the user's own target.
 * Verifiable figures only: nothing is estimated.
 */
export default function SavingsRoute({ apiClient }: SavingsRouteProps) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const shell = useShellContext();
  const tenantId = shell?.workspaceId ?? loadCurrentWorkspace()?.id ?? "";

  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => readFilters(params), [params]);
  const change = useCallback(
    (next: Partial<SpendFilters>) => setParams(writeFilters({ ...filters, ...next }), { replace: true }),
    [filters, setParams],
  );
  const [report, setReport] = useState<Load<SpendReport>>({ phase: "loading" });
  const [goal, setGoal] = useState<SavingsGoalBody | null>(null);
  const [progress, setProgress] = useState<SavingsGoalProgressBody | null>(null);
  const [goalOpen, setGoalOpen] = useState(false);
  const [goalText, setGoalText] = useState("");
  const [draft, setDraft] = useState<SavingsGoalDraftBody | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let live = true;
    void (async () => {
      if (!apiClient.getSpendReport) {
        setReport({ phase: "error", message: t("Spend and savings are not available right now.") });
        return;
      }
      const [spend, active] = await Promise.all([
        apiClient.getSpendReport(tenantId, { basis: filters.basis }),
        apiClient.getActiveSavingsGoal ? apiClient.getActiveSavingsGoal(tenantId) : Promise.resolve(null),
      ]);
      if (!live) return;
      setReport(spend.ok && spend.data ? { phase: "ready", data: spend.data } : { phase: "error", message: t("Spend and savings are not available right now.") });
      const current = active?.ok ? active.data : null;
      setGoal(current);
      if (current && apiClient.getSavingsGoalProgress) {
        const p = await apiClient.getSavingsGoalProgress(tenantId);
        if (live) setProgress(p.ok ? p.data : null);
      } else {
        setProgress(null);
      }
    })();
    return () => {
      live = false;
    };
  }, [apiClient, tenantId, reloadKey, t, filters.basis]);

  const gl = useMemo(
    () => (goal && progress ? buildGoalView(progress, t, report.phase === "ready" ? report.data.currency : "CHF") : null),
    [goal, progress, t, report],
  );

  const ask = useCallback(
    async (text: string) => {
      if (!text.trim() || !apiClient.parseSavingsGoal) {
        setDraft(null);
        return;
      }
      const result = await apiClient.parseSavingsGoal(tenantId, text);
      setDraft(result.ok ? result.data : null);
    },
    [apiClient, tenantId],
  );

  const confirm = useCallback(async () => {
    if (!draft?.ok || !apiClient.confirmSavingsGoal) return;
    const result = await apiClient.confirmSavingsGoal(tenantId, {
      text: draft.text,
      pct: draft.pct,
      annual: draft.annual,
      scope: draft.scope,
      period: draft.period,
      label: draft.label,
    });
    if (!result.ok || !result.data) {
      setActionError(t("The goal could not be saved."));
      return;
    }
    setDraft(null);
    setGoalOpen(false);
    setActionError(null);
    setReloadKey((n) => n + 1);
  }, [apiClient, tenantId, draft, t]);

  const clear = useCallback(async () => {
    if (apiClient.clearSavingsGoal) await apiClient.clearSavingsGoal(tenantId);
    setGoal(null);
    setProgress(null);
    setGoalText("");
    setDraft(null);
  }, [apiClient, tenantId]);

  /** A contract to act on: the case if there is one, else open it (one case per contract). */
  const openContract = useCallback(
    async (contractId: string, caseId: string | null) => {
      if (caseId) {
        navigate(`/renewals/${caseId}`);
        return;
      }
      const opened = await apiClient.openRenewalCase(tenantId, { contractId });
      if (opened.ok && opened.data?.caseId) {
        navigate(`/renewals/${opened.data.caseId}`);
        return;
      }
      setActionError(t("The renewal could not be started."));
    },
    [apiClient, tenantId, navigate, t],
  );

  const firstRow = gl?.rows[0];
  useGuide({
    where: t("Spend and savings⟦guide⟧"),
    now: goal
      ? t("Open the first contract under “Contracts you can act on” and set the target in the case.")
      : t("Pick the fiscal year, then read what is committed, what renews by itself and what you can still decide."),
    why: t("Raffa never estimates: spend comes from the documented terms and the renewal clause, a saving exists only with a signed contract or an invoice. The goal is yours and the page adapts to it."),
    next: goal
      ? t("Every target set in a case adds up here; it truly counts only at signature.")
      : t("With the goal set you see how much is covered and which renewals can close it."),
    cta: goal && firstRow ? { label: t("Open the first contract"), onClick: () => void openContract(firstRow.contractId, firstRow.caseId) } : null,
  });

  const open = goalOpen || !!draft;
  const toggle = () => {
    setGoalOpen(!open);
    if (open) setDraft(null);
  };
  const onKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") void ask(goalText);
  };

  return (
    <div data-screen-label="Spesa e risparmi" className="s4-page">
      <h2>{t("Spend and savings")}</h2>
      <div className="s4-subtitle">
        {t("What every contract is worth in the periods it runs, and what you saved, from the contracts you uploaded. Verifiable figures only: Raffa never estimates a saving. The goal is yours, in your own words.")}
      </div>

      <AskShortcuts
        items={[
          { label: t("Where can we save?"), question: t("Where can we save across our renewals?") },
          { label: t("Which renewals first?"), question: t("Which renewals should we start first, and why?") },
        ]}
      />
      {report.phase === "ready" ? <SpendSectionNav report={report.data} filters={filters} hasGoal={!!goal} /> : null}
      <div data-screen-label="Obiettivo" className="s4-goal">
        <div className="s4-goal-head">
          <span className="s4-goal-mark" />
          <span className="s4-goal-name">{t("Your goal")}</span>
          <span className="s4-goal-summary">
            {goal ? tm(t, goal.title as ApiMsg) : t("No goal set — write it in your own words and the page adapts")}
          </span>
          <button type="button" className="btn btn-secondary s4-goal-toggle" onClick={toggle}>
            {open ? t("Close") : goal ? t("Change⟦goal⟧") : t("Set a goal")}
          </button>
        </div>
        {open && (
          <div className="s4-goal-open">
            <div className="s4-goal-hint">{t("write it as you would tell a colleague — Raffa turns it into a target and adapts the page")}</div>
            <div className="s4-goal-input-row">
              <input
                className="input s4-goal-input"
                value={goalText}
                onChange={(e) => setGoalText(e.target.value)}
                onKeyDown={onKey}
                placeholder={t("E.g. “I want to save 20% on IT contracts next year”")}
              />
              <button type="button" className="btn btn-primary s4-goal-set" onClick={() => void ask(goalText)}>
                {t("Set⟦goal⟧")}
              </button>
              {goal && (
                <button type="button" className="btn btn-ghost s4-goal-remove" onClick={() => void clear()}>
                  {t("Remove goal")}
                </button>
              )}
            </div>
            <div className="s4-goal-examples">
              <span className="s4-goal-try">{t("Try:")}</span>
              {GOAL_EXAMPLES.map((x) => {
                const label = t(x);
                return (
                  <button
                    key={x}
                    type="button"
                    className="s4-goal-example"
                    onClick={() => {
                      setGoalText(label);
                      void ask(label);
                    }}
                  >
                    {label}
                  </button>
                );
              })}
            </div>
            {draft && (
              <div className="s4-draft">
                <div className="s4-draft-kicker">{tm(t, draft.kicker as ApiMsg)}</div>
                <div className="s4-draft-title">{tm(t, draft.title as ApiMsg)}</div>
                {draft.rows.map((r, i) => (
                  <div key={i} className="s4-draft-row">
                    <span>{tm(t, r.k as ApiMsg)}</span>
                    <span>{tm(t, r.v as ApiMsg)}</span>
                  </div>
                ))}
                <div className="s4-draft-actions">
                  {draft.ok && (
                    <button type="button" className="btn btn-primary" onClick={() => void confirm()}>
                      {t("Yes, that’s it")}
                    </button>
                  )}
                  <button type="button" className="btn btn-ghost s4-draft-cancel" onClick={() => setDraft(null)}>
                    {t("Cancel")}
                  </button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      {report.phase === "ready" ? <SpendFiltersBar report={report.data} filters={filters} onChange={change} /> : null}

      {actionError && (
        <div className="s4-error" role="alert">
          <span>{actionError}</span>
          <button type="button" className="btn btn-secondary" onClick={() => setActionError(null)}>
            {t("Close")}
          </button>
        </div>
      )}

      {report.phase === "error" && (
        <div className="s4-error" role="alert">
          <span>{report.message}</span>
          <button type="button" className="btn btn-secondary" onClick={() => { setReport({ phase: "loading" }); setReloadKey((n) => n + 1); }}>
            {t("Retry")}
          </button>
        </div>
      )}

      {report.phase === "ready" && (
        <>
          <ExcludedNote report={report.data} />
          <SpendOverview report={report.data} filters={filters} onChange={change} />
      {gl && (
        <div id="goal" data-screen-label="Verso l’obiettivo" className="s4-gl">
          <div className="s4-gl-title">
            {gl.title} <span>· {gl.scope}</span>
          </div>
          <div className="s4-gl-kpis">
            {gl.kpis.map((n) => (
              <div key={n.k} className="s4-gl-kpi">
                <div className="s4-k">{n.k}</div>
                <div className="s4-gl-v" style={{ color: n.fg }}>{n.v}</div>
                <div className="s4-sub">{n.sub}</div>
              </div>
            ))}
          </div>
          <div className="s4-gl-bar-wrap">
            <div className="s4-gl-bar">
              <span style={{ width: gl.wSigned, background: "var(--color-accent)" }} />
              <span style={{ width: gl.wObj, background: "var(--color-accent-200)" }} />
            </div>
            <div className="s4-gl-legend">
              <span>
                <span className="s4-sw" style={{ background: "var(--color-accent)" }} />
                {t("signed {amount}", { amount: gl.signedTxt })}
              </span>
              <span>
                <span className="s4-sw" style={{ background: "var(--color-accent-200)" }} />
                {t("objectives set in the cases {amount}", { amount: gl.objTxt })}
              </span>
              <span className="s4-gl-gap">{gl.gapTxt}</span>
            </div>
          </div>
          <div className="s4-gl-verdict">{gl.verdict}</div>
          <div className="s4-gl-rows">
            <div className="s4-section-k">{t("Contracts you can act on to get there · {n}", { n: gl.n })}</div>
            {gl.rows.map((c) => (
              <div key={c.key} className="r-row s4-gl-row" onClick={() => void openContract(c.contractId, c.caseId)}>
                <strong>{c.supplier}</strong>
                <span className="s4-gl-spend">{t("{amount} / year", { amount: c.spend })}</span>
                <span className="s4-gl-line">{c.line}</span>
                <span className="s4-gl-hit">
                  <span>{t("If you reach {pct}", { pct: gl.pct })}</span>
                  <div>{c.ifHit}</div>
                </span>
                <span className="s4-gl-days" style={{ color: c.fg }}>{c.days === "" ? t("No notice date") : c.days === "—" ? t("notice passed") : t("{n} d to notice", { n: c.days })}</span>
                <button type="button" className={`btn ${c.cls} s4-gl-cta`}>{c.cta}</button>
              </div>
            ))}
            {gl.none && (
              <div className="s4-gl-none">{t("No validated contract in this scope: the goal stays a figure until you upload one.")}</div>
            )}
          </div>
        </div>
      )}

          <SpendPivot report={report.data} filters={filters} onChange={change} onOpenCase={(id, caseId) => void openContract(id, caseId)} />
          <SpendBudget report={report.data} filters={filters} onChange={change} onOpenCase={(id, caseId) => void openContract(id, caseId)} />
          <SpendLedger report={report.data} filters={filters} />
        </>
      )}
    </div>
  );
}
