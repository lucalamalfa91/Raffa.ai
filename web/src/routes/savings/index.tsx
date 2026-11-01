import { useCallback, useEffect, useMemo, useState, type KeyboardEvent } from "react";
import { useNavigate } from "react-router-dom";
import type {
  ApiClient,
  SavingsGoalBody,
  SavingsGoalDraftBody,
  SavingsGoalProgressBody,
  SavingsResultsBody,
} from "../../api/client";
import { useGuide } from "../../components/shell/guide";
import { useShellContext } from "../../components/shell/shellContext";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { useI18n } from "../../i18n/I18nProvider";
import { buildGoalView, buildResultsView, GOAL_EXAMPLES, tm, type ApiMsg } from "./resultsViewModel";
import "./results4.css";

export interface SavingsRouteProps {
  apiClient: ApiClient;
}

type Load<T> = { phase: "loading" } | { phase: "ready"; data: T } | { phase: "error"; message: string };

/**
 * Risultati (AskRaffa V4.dc.html lines 206-255): verifiable figures only — contractual results
 * (signed), realized (invoices), quantity reductions apart, open objectives (the user's targets).
 * The goal bar is collapsed by default; with a goal the page adds "Verso il tuo obiettivo".
 */
export default function SavingsRoute({ apiClient }: SavingsRouteProps) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const shell = useShellContext();
  const tenantId = shell?.workspaceId ?? loadCurrentWorkspace()?.id ?? "";

  const [report, setReport] = useState<Load<SavingsResultsBody>>({ phase: "loading" });
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
      if (!apiClient.getSavingsResults) {
        setReport({ phase: "error", message: t("Results are not available right now.") });
        return;
      }
      const [results, active] = await Promise.all([
        apiClient.getSavingsResults(tenantId, "fy"),
        apiClient.getActiveSavingsGoal ? apiClient.getActiveSavingsGoal(tenantId) : Promise.resolve(null),
      ]);
      if (!live) return;
      setReport(results.ok && results.data ? { phase: "ready", data: results.data } : { phase: "error", message: t("Results are not available right now.") });
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
  }, [apiClient, tenantId, reloadKey, t]);

  const rs = useMemo(() => (report.phase === "ready" ? buildResultsView(report.data, t) : null), [report, t]);
  const gl = useMemo(() => (goal && progress ? buildGoalView(progress, t) : null), [goal, progress, t]);

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
    where: t("Results⟦guide⟧"),
    now: goal
      ? t("Open the first contract under “Contracts you can act on” and set the target in the case.")
      : t("Write your goal in your own words, then read what is signed and what is missing."),
    why: t("Raffa never estimates: a figure exists only with a signed contract or an invoice. The goal is yours and the page adapts to it."),
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
    <div data-screen-label="Risultati" className="s4-page">
      <h2>{t("Results⟦page⟧")}</h2>
      <div className="s4-subtitle">{t("Verifiable figures only. Raffa never estimates a saving. The goal is yours, in your own words.")}</div>

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

      {actionError && (
        <div className="s4-error" role="alert">
          <span>{actionError}</span>
          <button type="button" className="btn btn-secondary" onClick={() => setActionError(null)}>
            {t("Close")}
          </button>
        </div>
      )}

      {gl && (
        <div data-screen-label="Verso l’obiettivo" className="s4-gl">
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
                <span className="s4-gl-days" style={{ color: c.fg }}>{t("{n} d to notice", { n: c.days })}</span>
                <button type="button" className={`btn ${c.cls} s4-gl-cta`}>{c.cta}</button>
              </div>
            ))}
            {gl.none && (
              <div className="s4-gl-none">{t("No validated contract in this scope: the goal stays a figure until you upload one.")}</div>
            )}
          </div>
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

      {rs && (
        <>
          <div className="s4-nums">
            {rs.nums.map((n) => (
              <div key={n.k} className="s4-num">
                <div className="s4-k">{n.k}</div>
                <div className="s4-num-v" style={{ color: n.fg }}>{n.v}</div>
                <div className="s4-sub">{n.sub}</div>
              </div>
            ))}
          </div>
          <div data-screen-label="Risultati · KPI" className="s4-kpis">
            {rs.kpis.map((n) => (
              <div key={n.k} className="s4-kpi">
                <div className="s4-kpi-k">{n.k}</div>
                <div className="s4-kpi-v" style={{ color: n.fg }}>{n.v}</div>
                <div className="s4-kpi-sub">{n.sub}</div>
              </div>
            ))}
          </div>
          <div data-screen-label="Risultati · grafici" className="s4-charts">
            <div className="s4-chart">
              <div className="s4-section-k">{t("When they were signed · CHF / year")}</div>
              <div className="s4-months">
                {rs.months.map((m, i) => (
                  <div key={i} className="s4-month" title={m.title}>
                    <span className="s4-month-txt">{m.txt}</span>
                    <span className="s4-month-bar" style={{ height: m.h, background: m.bg }} />
                  </div>
                ))}
              </div>
              <div className="s4-month-labels">
                {rs.months.map((m, i) => (
                  <span key={i}>{m.label}</span>
                ))}
              </div>
            </div>
            <div className="s4-chart">
              <div className="s4-section-k">{t("By supplier · signed, proven, objective")}</div>
              {rs.by.map((b) => (
                <div key={b.label} className="s4-by">
                  <strong>{b.label}</strong>
                  <span className="s4-stack">
                    <span style={{ width: b.wReal, background: "var(--color-text)" }} />
                    <span style={{ width: b.wSig, background: "var(--color-accent)" }} />
                    <span style={{ width: b.wObj, background: "var(--color-accent-100)" }} />
                  </span>
                  <span className="s4-by-txt">{b.txt}</span>
                </div>
              ))}
              <div className="s4-legend">
                <span>
                  <span className="s4-sw" style={{ background: "var(--color-text)" }} />
                  {t("proven by invoices")}
                </span>
                <span>
                  <span className="s4-sw" style={{ background: "var(--color-accent)" }} />
                  {t("signed, not yet proven")}
                </span>
                <span>
                  <span className="s4-sw" style={{ background: "var(--color-accent-100)", border: "1px solid var(--color-accent-300)", boxSizing: "border-box" }} />
                  {t("your objective")}
                </span>
              </div>
            </div>
            <div className="s4-chart">
              <div className="s4-section-k">{t("By lever · signed results")}</div>
              {rs.levers.map((l) => (
                <div key={l.label} className="s4-lever">
                  <span>
                    {l.label}
                    <span className="s4-lever-n">{l.n}</span>
                  </span>
                  <span className="s4-bar">
                    <span style={{ width: l.w, background: "var(--color-accent)" }} />
                  </span>
                  <span className="s4-lever-txt">{l.txt}</span>
                </div>
              ))}
              <div className="s4-chart-note">{t("Quantity reductions are not included: they are shown apart.")}</div>
            </div>
            <div className="s4-chart">
              <div className="s4-section-k">{t("Proven by invoices · per signed result")}</div>
              {rs.proof.map((p, i) => (
                <div key={i} className="s4-proof">
                  <strong>{p.label}</strong>
                  <span className="s4-bar">
                    <span style={{ width: p.w, background: "var(--color-text)" }} />
                  </span>
                  <span className="s4-proof-txt">{p.txt}</span>
                </div>
              ))}
            </div>
          </div>
          <div className="s4-lists">
            <div className="s4-list">
              <div className="s4-list-title">
                {t("Open objectives")} <span>{t("· yours, from the renewal chat")}</span>
              </div>
              {rs.open.map((o) => (
                <div
                  key={o.key}
                  className="r-row s4-open"
                  onClick={() => navigate(o.caseId ? `/renewals/${o.caseId}` : `/contracts/${o.contractId}`)}
                >
                  <strong>{o.supplier}</strong>
                  <span className="s4-open-est">{o.est}</span>
                  <span className={`tag ${o.tag}`}>{o.status}</span>
                  <span className="s4-open-days" style={{ color: o.fg }}>{o.days}</span>
                </div>
              ))}
            </div>
            <div className="s4-list">
              <div className="s4-list-title">
                {t("Contractual results · FY {year}", { year: rs.fiscalYear })} <span>{t("· baseline · period · method · evidence")}</span>
              </div>
              {rs.recent.map((r) => (
                <div key={r.key} className="s4-recent">
                  <span className="s4-recent-when">{r.when}</span>
                  <span>
                    <strong>{r.supplier}</strong> · {r.note}
                    <span className="s4-recent-meta">
                      {r.baseline} · {r.period} · {r.method} · {r.evidence}
                    </span>
                    <span className="s4-recent-real" style={{ color: r.realFg }}>{r.realized}</span>
                  </span>
                  <span className="s4-recent-amount">{r.amount}</span>
                </div>
              ))}
              {rs.volume.map((r) => (
                <div key={r.key} className="s4-recent s4-volume">
                  <span className="s4-recent-when">{r.when}</span>
                  <span>
                    <strong>{r.supplier}</strong> · {r.note}
                    <span className="s4-recent-meta">{t("Quantity reduction — not a negotiation result")}</span>
                  </span>
                  <span className="s4-recent-amount">{r.amount}</span>
                </div>
              ))}
            </div>
          </div>
        </>
      )}
    </div>
  );
}
