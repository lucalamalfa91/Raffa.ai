import { useCallback, useEffect, useState, type KeyboardEvent } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiClient, ContractListRowV4Body, ContractListV4Body } from "../../api/client";
import { useGuide } from "../../components/shell/guide";
import { useI18n } from "../../i18n/I18nProvider";
import { chfK, URGENT_NOTICE_DAYS, useTenantId } from "./contracts4";
import { reviewPath, useContractUpload } from "./useContractUpload";
import "./contracts4.css";

export interface ContractsRouteProps {
  apiClient: ApiClient;
}

const STATUS_LABEL: Record<ContractListRowV4Body["status"], string> = {
  validated: "Validated",
  caseOpen: "Case open",
  renewed: "Renewed",
  terminated: "Terminated",
  autoRenewed: "Renewed automatically",
  expired: "Expired",
};

/** While a document is being read the banner follows it. */
const POLL_MS = 1500;

/**
 * AskRaffa 4.0 Contratti (AskRaffa V4.dc.html lines 176-184): the validated contracts, nearest notice
 * deadline first, with notice deadline and contract end labelled apart, annual value and status tag
 * (*Validato / Pratica aperta / Rinnovato / Disdetto*); a banner leads to a document being read or
 * waiting for review. Data: `GET /api/contracts?view=v4`.
 */
export default function ContractsRoute({ apiClient }: ContractsRouteProps) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const tenantId = useTenantId();
  const upload = useContractUpload(apiClient);
  const [list, setList] = useState<ContractListV4Body | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!tenantId) return;
    const result = await apiClient.listContractsV4(tenantId);
    if (result.ok && result.data) {
      setList(result.data);
      setError(null);
    } else {
      setError(result.error ?? "");
    }
  }, [apiClient, tenantId]);

  useEffect(() => {
    void load();
  }, [load]);

  const reading = list?.document?.status === "processing";
  useEffect(() => {
    if (!reading) return undefined;
    const timer = window.setInterval(() => void load(), POLL_MS);
    return () => window.clearInterval(timer);
  }, [reading, load]);

  useGuide({
    where: t("Validated contracts"),
    now: t("Open a contract or upload the next one."),
    why: t("Only validated contracts feed Today and Renewals."),
    next: t("A new file goes reading → verification → validation."),
    cta: { label: t("Upload a contract"), onClick: upload.start },
  });

  const rows = list?.rows ?? [];
  const doc = list?.document ?? null;
  const open = (row: ContractListRowV4Body) => navigate(`/contracts/${encodeURIComponent(row.contractId)}`);
  const onKey = (event: KeyboardEvent, go: () => void) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      go();
    }
  };

  return (
    <div className="c4-page is-list" data-screen-label="Contratti">
      <div className="c4-head">
        <div>
          <h2>{t("Contracts")}</h2>
          <div className="c4-head-sub">{list ? t("{n} validated contracts · sorted by notice deadline", { n: rows.length }) : " "}</div>
        </div>
        <button type="button" className="btn btn-secondary c4-upload" onClick={upload.start} disabled={upload.busy}>
          {t("Upload a contract")}
        </button>
      </div>

      {doc ? (
        <div
          className="r-row c4-banner"
          role="link"
          tabIndex={0}
          onClick={() => navigate(reviewPath(doc.documentId))}
          onKeyDown={(e) => onKey(e, () => navigate(reviewPath(doc.documentId)))}
        >
          <span>
            <strong>{doc.file}</strong> ·{" "}
            {doc.status === "processing"
              ? t("being read…")
              : t(doc.flagged === 1 ? "1 field to verify before use" : "{n} fields to verify before use", { n: doc.flagged })}
          </span>
          <span className="c4-banner-cta">{t(doc.status === "processing" ? "See" : "Verify now")} →</span>
        </div>
      ) : null}

      {error !== null || upload.error ? (
        <div className="c4-error" role="alert">
          <span>{upload.error ? t("The upload did not go through.") : t("The contracts could not be loaded.")}</span>
          <button type="button" className="btn btn-secondary" onClick={upload.error ? upload.start : () => void load()}>
            {t("Try again")}
          </button>
        </div>
      ) : null}

      <div className="c4-rows">
        {list && rows.length === 0 ? (
          <div className="c4-empty">
            {t("No validated contract. Upload the first: after the reading and the check of doubtful fields it shows up here and in Today.")}
          </div>
        ) : null}
        {rows.map((row) => (
          <div
            key={row.contractId}
            className="r-row c4-row"
            role="link"
            tabIndex={0}
            onClick={() => open(row)}
            onKeyDown={(e) => onKey(e, () => open(row))}
          >
            <div className="c4-row-main">
              <div className="c4-row-supplier">{row.supplier}</div>
              <div className="c4-row-name">{row.name}</div>
            </div>
            <div className="c4-row-notice">
              <div className="c4-label">
                {row.termStatus === "rolledOver" ? t("Next notice by") : row.noticeDays < 0 ? t("Notice deadline passed on") : t("Notice by")}
              </div>
              <div className={row.noticeDays >= 0 && row.noticeDays <= URGENT_NOTICE_DAYS ? "c4-row-notice-value c4-urgent" : "c4-row-notice-value"}>
                {row.noticeDays >= 0 ? (
                  <>
                    {row.noticeDeadline} · {row.noticeDays} {t("d⟦days⟧")}
                  </>
                ) : (
                  row.noticeDeadline
                )}
              </div>
              {row.termStatus === "rolledOver" && row.renewedOn ? (
                <div className="c4-row-renewed">
                  {t(row.renewedOnPassed ? "Renewed by itself on {date}" : "Renews by itself on {date}", { date: row.renewedOn })}
                </div>
              ) : null}
            </div>
            <div className="c4-row-end">
              <div className="c4-label">{t("Contract end")}</div>
              <div className="c4-row-end-value">{row.end}</div>
            </div>
            <div className="c4-row-spend">{chfK(row.annualValue)}</div>
            <span className={row.status === "validated" ? "tag tag-neutral c4-tag" : "tag tag-accent c4-tag"}>
              {t(STATUS_LABEL[row.status])}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
