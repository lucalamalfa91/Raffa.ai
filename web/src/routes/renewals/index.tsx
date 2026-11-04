import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import AskShortcuts from "../../components/ask/AskShortcuts";
import type { ApiClient } from "../../api/client";
import type { RenewalsBoard, RenewalsRow } from "../../api/renewalCases";
import { useGuide } from "../../components/shell/guide";
import { useTenantId } from "../../routes/contracts/contracts4";
import { useI18n } from "../../i18n/I18nProvider";
import { resolveText, type Text } from "../../i18n/translate";
import { useCaseNavigation } from "../today/useCaseNavigation";
import "./renewals4.css";

export interface RenewalsRouteProps {
  apiClient: ApiClient;
}

/**
 * Rinnovi (AskRaffa V4.dc.html lines 100-117): one summary line, then the validated contracts by whose
 * turn it is -- Tocca a te · In corso con il team · In attesa del fornitore · Da avviare · Chiuse --
 * sorted by notice deadline. A row opens the case (the same one, never a second).
 */
export default function RenewalsRoute({ apiClient }: RenewalsRouteProps) {
  const tenantId = useTenantId() ?? undefined;
  const { t, lang } = useI18n();
  const tr = useCallback((x: Text | null | undefined) => resolveText(x, lang), [lang]);
  const navigate = useNavigate();
  const [board, setBoard] = useState<RenewalsBoard | null>(null);
  const [error, setError] = useState<string | null>(null);
  const { openContract } = useCaseNavigation(apiClient, tenantId);

  useEffect(() => {
    if (!tenantId || !apiClient.getRenewalsBoard) return;
    let alive = true;
    void apiClient.getRenewalsBoard(tenantId).then((r) => {
      if (!alive) return;
      if (r.ok) setBoard(r.body);
      else setError(r.error);
    });
    return () => {
      alive = false;
    };
  }, [apiClient, tenantId]);

  const open = useCallback(
    (x: RenewalsRow) => {
      if (x.caseId) navigate(`/renewals/${x.caseId}`);
      else if (!x.hasCase && (x.termStatus === "expired" || x.termStatus === "needsReview")) navigate(`/contracts/${x.contractId}`);
      else if (board) void openContract(x.contractId, board.viewer.isApprover ? (board.assignee?.userId ?? board.viewer.userId) : board.viewer.userId);
    },
    [board, navigate, openContract],
  );

  const all = board?.groups.flatMap((g) => g.items) ?? [];
  const first = board?.groups.find((g) => g.key === "mine")?.items[0];
  useGuide(
    board
      ? {
          where: t("Renewals"),
          now: first ? t("Open the first one under “Your turn”.") : t("No step waits for you."),
          why: t("Sorted by notice deadline. The work happens in the case, one step at a time."),
          next: t("To start: within 120 days of the notice deadline."),
          cta: first ? { label: t("Open the first"), onClick: () => open(first) } : null,
        }
      : null,
  );

  // "Start & assign" only when there is a real colleague to assign to.
  const canAssign = !!board?.viewer.isApprover && !!board?.assignee;
  const toContract = (x: RenewalsRow) => !x.hasCase && (x.termStatus === "expired" || x.termStatus === "needsReview" || x.termStatus === "unknown");
  const next = (x: RenewalsRow): string =>
    x.hasCase ? tr(x.next) : toContract(x) ? t("Open the contract") : x.days <= 120 ? (canAssign ? t("Start & assign") : t("Start the renewal")) : t("To start by {date}", { date: x.startBy });
  const cta = (x: RenewalsRow): string =>
    x.hasCase ? (x.myTurn ? t("Do now") : t("Open⟦action⟧")) : toContract(x) ? t("Open the contract") : canAssign ? t("Start & assign") : t("Start⟦action⟧");
  /** The phase line of a row without a case whose notice deadline passed (what happened by itself). */
  const situation = (x: RenewalsRow): string | null => {
    if (x.hasCase) return null;
    switch (x.termStatus) {
      case "rolledOver":
        return x.renewedOn
          ? t(x.renewedOnPassed ? "Renewed by itself on {date} · next notice by {notice}" : "Renews by itself on {date} · next notice by {notice}", { date: x.renewedOn, notice: x.notice })
          : t("Renewed by itself");
      case "ending":
        return t("Ends on {date} without renewal", { date: x.end ?? "" });
      case "expired":
        return t("Expired on {date}", { date: x.end ?? "" });
      case "needsReview":
        return t("Renewal term to confirm");
      case "unknown":
        return t("Dates to verify");
      default:
        return null;
    }
  };

  return (
    <div className="r4-page" data-screen-label="Pratiche">
      <div className="ph r4-ph">
        <h2 className="r4-title">{t("Renewals")}</h2>
        <div className="ph-sub r4-head">
          {board ? t("{open} open cases · {start} to start · sorted by notice deadline", { open: board.openCases, start: board.toStart }) : (error ?? "")}
        </div>
      </div>
      <AskShortcuts
        items={[
          { label: t("Which renewals first?"), question: t("Which renewals should we start first, and why?") },
          { label: t("Where can we save?"), question: t("Where can we save across our renewals?") },
        ]}
      />
      {board && all.length === 0 ? (
        <div className="r4-empty">{t("No renewal to manage: there is no validated contract yet. Upload the first in Contracts.")}</div>
      ) : null}
      {board && all.length > 0 ? (
        <div className="dt r4-table">
          <div className="dt-row dt-head" aria-hidden="true">
            <span className="dt-th">{t("Supplier · case")}</span>
            <span className="dt-th">{t("Notice by")}</span>
            <span className="dt-th dt-num">{t("Days")}</span>
            <span className="dt-th">{t("Owner")}</span>
            <span />
          </div>
          {board.groups.map((g) => (
            <div key={g.key} className="r4-group">
              <div className="dt-group r4-group-head">
                {tr(g.label)} <span>· {g.items.length}</span>
              </div>
              {g.items.map((x) => {
                // The board sends "—" (and days = int.MaxValue) when the contract's dates were not read.
                const stated = x.notice.trim() !== "" && x.notice !== "—" && x.termStatus !== "unknown";
                const urgent = stated && x.days >= 0 && x.days <= 30;
                return (
                  <div key={x.contractId} className="r-row dt-row r4-row" onClick={() => open(x)}>
                    <div className="dt-cell r4-main">
                      <div className="dt-title r4-sup">{x.supplier}</div>
                      <div className="dt-sub r4-file" title={x.contract}>{x.contract}</div>
                      <div className="r4-phase">
                        <svg className="r4-ico" viewBox="0 0 16 16" aria-hidden="true">
                          {x.hasCase ? <path d="M3 8a5 5 0 1 0 1.5-3.5M3 2.5V5h2.5" /> : x.termStatus === "rolledOver" ? <path d="M13 8a5 5 0 1 1-1.5-3.5M13 2.5V5h-2.5" /> : <circle cx="8" cy="8" r="5" />}
                        </svg>
                        <span className="r4-phase-txt">
                          {situation(x) ?? (x.hasCase ? t("Phase {n}/6 · {label}", { n: x.stage + 1, label: tr(x.stageLabel) }) : t("Not started"))}
                        </span>
                        {x.hasCase ? (
                          <span className="r4-steps" aria-hidden="true">
                            {[0, 1, 2, 3, 4, 5].map((i) => <i key={i} className={i <= x.stage ? "on" : ""} />)}
                          </span>
                        ) : null}
                      </div>
                      <div className="r4-next">
                        <svg className="r4-ico" viewBox="0 0 16 16" aria-hidden="true"><path d="M3 8h10M9 4l4 4-4 4" /></svg>
                        <strong>{next(x)}</strong>
                      </div>
                    </div>
                    <div className="dt-cell r4-notice">
                      <span className="dt-label">{x.termStatus === "rolledOver" ? t("Next notice by") : stated && x.days < 0 ? t("Notice deadline passed on") : t("Notice by")}</span>
                      <div className={urgent ? "dt-urgent r4-days" : stated ? "r4-days" : "dt-muted r4-days"}>{stated ? x.notice : t("Not stated")}</div>
                    </div>
                    <div className={urgent ? "dt-cell dt-num dt-urgent" : "dt-cell dt-num"}>
                      <span className="dt-label">{t("Days")}</span>
                      {!stated ? <span className="dt-muted">—</span> : x.days < 0 ? <span className="dt-muted">{t("passed")}</span> : x.days}
                    </div>
                    <div className="dt-cell r4-owner" title={x.owner ?? t("No owner")}>
                      <span className={x.owner ? "r4-avatar" : "r4-avatar is-none"} aria-hidden="true">{x.owner ? x.owner.trim().charAt(0).toUpperCase() : "–"}</span>
                      <span className="r4-owner-name">{x.owner ? x.owner.split(/[ @]/)[0] : t("No owner")}</span>
                    </div>
                    <div className="dt-cell dt-end r4-cta-cell">
                      <button type="button" className={`btn ${x.myTurn || !x.hasCase ? "btn-primary" : "btn-secondary"} r4-cta`}>
                        {cta(x)}
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}
