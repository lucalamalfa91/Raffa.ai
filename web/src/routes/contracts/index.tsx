import { useCallback, useEffect, useState, type KeyboardEvent } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiClient, ContractListRowV4Body, ContractListV4Body } from "../../api/client";
import { useGuide } from "../../components/shell/guide";
import { useShellContext } from "../../components/shell/shellContext";
import { notifyTodayChanged } from "../today/loadToday";
import { useI18n } from "../../i18n/I18nProvider";
import { announceContractsChanged, chfK, CONTRACTS_CHANGED_EVENT, URGENT_NOTICE_DAYS, useTenantId } from "./contracts4";
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
  const canDelete = useShellContext()?.role === "admin";
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
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

  // Re-read after uploads (one per file of a bulk upload) and validations.
  useEffect(() => {
    const reload = () => void load();
    window.addEventListener(CONTRACTS_CHANGED_EVENT, reload);
    return () => window.removeEventListener(CONTRACTS_CHANGED_EVENT, reload);
  }, [load]);

  const reading = list?.document?.status === "processing" || (list?.document?.reading ?? 0) > 0;
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
  /** Delete a contract for good (Admin): it, its documents, its renewal case and chats. */
  const removeContract = async (contractId: string) => {
    if (!tenantId || !apiClient.deleteContract) return;
    setDeleteError(null);
    const r = await apiClient.deleteContract(tenantId, contractId);
    if (r.ok) {
      setConfirmDelete(null);
      void load();
      announceContractsChanged();
      notifyTodayChanged();
    } else {
      setDeleteError(r.error || t("The contract could not be deleted."));
    }
  };

  const onKey = (event: KeyboardEvent, go: () => void) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      go();
    }
  };

  return (
    <div className="c4-page is-list" data-screen-label="Contratti">
      <div className="ph c4-head">
        <h2>{t("Contracts")}</h2>
        <div className="ph-actions">
          <button type="button" className="btn btn-secondary c4-upload" onClick={upload.start} disabled={upload.busy}>
            {t("Upload a contract")}
          </button>
        </div>
        <div className="ph-sub c4-head-sub">{list ? t("{n} validated contracts · sorted by notice deadline", { n: rows.length }) : "\u00a0"}</div>
      </div>

      {upload.bulk ? (
        <div className="c4-banner c4-bulk" role="status" aria-live="polite">
          <span>
            {upload.bulk.finished ? (
              <strong>{t("{n} files uploaded", { n: upload.bulk.uploaded })}</strong>
            ) : (
              <strong>{t("Uploading {done} of {total}", { done: upload.bulk.done, total: upload.bulk.total })}</strong>
            )}
            {upload.bulk.duplicates > 0 ? <> · {t("{n} already uploaded", { n: upload.bulk.duplicates })}</> : null}
            {upload.bulk.failed.length > 0 ? (
              <> · <span className="c4-bulk-failed" title={upload.bulk.failed.join("\n")}>{t("{n} did not go through", { n: upload.bulk.failed.length })}</span></>
            ) : null}
            {upload.bulk.finished ? <> · {t("Raffa reads them one after the other; you verify only the doubtful fields.")}</> : null}
          </span>
          {upload.bulk.finished ? (
            <button type="button" className="btn btn-ghost c4-banner-cta" onClick={upload.dismissBulk}>
              {t("Close")}
            </button>
          ) : (
            <span className="c4-bulk-bar" aria-hidden="true">
              <span style={{ width: `${Math.round((upload.bulk.done / Math.max(upload.bulk.total, 1)) * 100)}%` }} />
            </span>
          )}
        </div>
      ) : null}

      {doc ? (
        <div
          className="r-row c4-banner"
          role="link"
          tabIndex={0}
          onClick={() => navigate(reviewPath(doc.documentId))}
          onKeyDown={(e) => onKey(e, () => navigate(reviewPath(doc.documentId)))}
        >
          <span>
            {(doc.reading ?? 0) + (doc.toReview ?? 0) > 1 ? (
              <>
                <strong>{t("{n} documents being read", { n: doc.reading ?? 0 })}</strong> ·{" "}
                {t("{n} to verify", { n: doc.toReview ?? 0 })}
                {doc.status === "review" ? <> · {t("next: {file}", { file: doc.file })}</> : null}
              </>
            ) : (
              <>
                <strong>{doc.file}</strong> ·{" "}
                {doc.status === "processing"
                  ? t("being read…")
                  : t(doc.flagged === 1 ? "1 field to verify before use" : "{n} fields to verify before use", { n: doc.flagged })}
              </>
            )}
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

      {deleteError ? (
        <div className="c4-error" role="alert">
          <span>{deleteError}</span>
        </div>
      ) : null}

      <div className={canDelete ? "dt c4-rows has-actions" : "dt c4-rows"}>
        {list && rows.length === 0 ? (
          <div className="c4-empty">
            {t("No validated contract. Upload the first: after the reading and the check of doubtful fields it shows up here and in Today.")}
          </div>
        ) : (
          <div className="dt-row dt-head" aria-hidden="true">
            <span className="dt-th">{t("Supplier · document")}</span>
            <span className="dt-th">{t("Notice by")}</span>
            <span className="dt-th dt-num">{t("Days")}</span>
            <span className="dt-th">{t("Contract end")}</span>
            <span className="dt-th dt-num">{t("Annual value")}</span>
            <span className="dt-th">{t("Status")}</span>
            {canDelete ? <span className="dt-end" /> : null}
          </div>
        )}
        {rows.map((row) => {
          const stated = row.noticeDeadline.trim() !== "";
          const urgent = stated && row.noticeDays >= 0 && row.noticeDays <= URGENT_NOTICE_DAYS;
          const noticeLabel =
            row.termStatus === "rolledOver" ? t("Next notice by") : stated && row.noticeDays < 0 ? t("Notice deadline passed on") : t("Notice by");
          return (
            <div
              key={row.contractId}
              className="r-row dt-row c4-row"
              role="link"
              tabIndex={0}
              onClick={() => open(row)}
              onKeyDown={(e) => onKey(e, () => open(row))}
            >
              <div className="dt-cell c4-row-main">
                <div className="dt-title c4-row-supplier">{row.supplier}</div>
                <div className="dt-sub c4-row-name" title={row.name}>
                  {row.name}
                </div>
              </div>
              <div className="dt-cell c4-row-notice">
                <span className="dt-label">{noticeLabel}</span>
                <div className={urgent ? "dt-urgent c4-row-notice-value" : stated ? "c4-row-notice-value" : "dt-muted c4-row-notice-value"}>
                  {stated ? row.noticeDeadline : t("Not stated")}
                </div>
                {row.termStatus === "rolledOver" && row.renewedOn ? (
                  <div className="dt-note c4-row-renewed">
                    {t(row.renewedOnPassed ? "Renewed by itself on {date}" : "Renews by itself on {date}", { date: row.renewedOn })}
                  </div>
                ) : null}
              </div>
              <div className={urgent ? "dt-cell dt-num dt-urgent" : "dt-cell dt-num"}>
                <span className="dt-label">{t("Days")}</span>
                {!stated ? <span className="dt-muted">—</span> : row.noticeDays < 0 ? <span className="dt-muted">{t("passed")}</span> : row.noticeDays}
              </div>
              <div className="dt-cell c4-row-end">
                <span className="dt-label">{t("Contract end")}</span>
                <div className="c4-row-end-value">{row.end}</div>
              </div>
              <div className="dt-cell dt-num dt-strong c4-row-spend">
                <span className="dt-label">{t("Annual value")}</span>
                {chfK(row.annualValue)}
              </div>
              <div className="dt-cell">
                <span className="dt-label">{t("Status")}</span>
                <span className={row.status === "validated" ? "tag tag-neutral c4-tag" : "tag tag-accent c4-tag"}>{t(STATUS_LABEL[row.status])}</span>
              </div>
              {canDelete ? (
                <div className="dt-cell dt-end c4-del" data-parity-hide onClick={(e) => e.stopPropagation()} onKeyDown={(e) => e.stopPropagation()}>
                  <button
                    type="button"
                    className="btn btn-ghost c4-del-btn"
                    aria-expanded={confirmDelete === row.contractId}
                    onClick={() => setConfirmDelete(confirmDelete === row.contractId ? null : row.contractId)}
                  >
                    {t("Delete")}
                  </button>
                </div>
              ) : null}
              {canDelete && confirmDelete === row.contractId ? (
                <div className="dt-span c4-del-confirm" data-parity-hide onClick={(e) => e.stopPropagation()} onKeyDown={(e) => e.stopPropagation()}>
                  <span className="c4-del-ask">{t("Delete this contract and its documents?")}</span>
                  <button type="button" className="btn btn-secondary c4-del-btn" onClick={() => void removeContract(row.contractId)}>
                    {t("Delete")}
                  </button>
                  <button type="button" className="btn btn-ghost c4-del-btn" onClick={() => setConfirmDelete(null)}>
                    {t("Cancel")}
                  </button>
                </div>
              ) : null}
            </div>
          );
        })}
      </div>
    </div>
  );
}
