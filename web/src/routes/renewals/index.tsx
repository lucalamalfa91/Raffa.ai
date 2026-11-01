import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import type { RenewalsBoard, RenewalsRow } from "../../api/renewalCases";
import { useGuide } from "../../components/shell/guide";
import { useShellContext } from "../../components/shell/shellContext";
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
  const tenantId = useShellContext()?.workspaceId;
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

  const next = (x: RenewalsRow): string =>
    x.hasCase ? tr(x.next) : x.days <= 120 ? t("Start & assign") : t("To start by {date}", { date: x.startBy });
  const cta = (x: RenewalsRow): string => (x.hasCase ? (x.myTurn ? t("Do now") : t("Open")) : board?.viewer.isApprover ? t("Start & assign") : t("Start"));

  return (
    <div className="r4-page" data-screen-label="Pratiche">
      <h2 className="r4-title">{t("Renewals")}</h2>
      <div className="r4-head">
        {board ? t("{open} open cases · {start} to start · sorted by notice deadline", { open: board.openCases, start: board.toStart }) : (error ?? "")}
      </div>
      {board && all.length === 0 ? (
        <div className="r4-empty">{t("No renewal to manage: there is no validated contract yet. Upload the first in Contracts.")}</div>
      ) : null}
      {board?.groups.map((g) => (
        <div key={g.key} className="r4-group">
          <div className="r4-group-head">
            {tr(g.label)} <span>· {g.items.length}</span>
          </div>
          {g.items.map((x) => (
            <div key={x.contractId} className="r-row r4-row" onClick={() => open(x)}>
              <div className="r4-main">
                <div className="r4-sup">
                  {x.supplier} <span>{x.contract}</span>
                </div>
                <div className="r4-phase">
                  {x.hasCase ? t("Phase {n}/6 · {label}", { n: x.stage + 1, label: tr(x.stageLabel) }) : t("Not started")} · <strong>{next(x)}</strong>
                </div>
              </div>
              <div className="r4-notice">
                <div className="r4-label">{t("Notice by")}</div>
                <div className={x.days <= 30 ? "r4-days is-urgent" : "r4-days"}>
                  {x.notice} · {t("{n} d", { n: x.days })}
                </div>
              </div>
              <div className="r4-owner">{x.owner ? `${t("Owner")} ${x.owner.split(" ")[0]}` : t("No owner")}</div>
              <button type="button" className={`btn ${x.myTurn || !x.hasCase ? "btn-primary" : "btn-secondary"} r4-cta`}>
                {cta(x)}
              </button>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}
