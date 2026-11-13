import { useEffect, useState } from "react";
import type { ApiClient } from "../../../api/client";
import type { CaseEnvelope, CaseFile } from "../../../api/renewalCases";
import { useI18n } from "../../../i18n/I18nProvider";

interface CaseFilesProps {
  apiClient: ApiClient;
  tenantId: string;
  caseId: string;
  /** The case version: the list is read again whenever the case changes. */
  version: number;
  /** Whether the caller may use a file in the case (the case is open and they can work on it). */
  canUse: boolean;
  /** A file was used: the updated case (or the current one returned with an error). */
  onCase: (next: CaseEnvelope | null, ok: boolean) => void;
}

const KIND_LABELS: Record<CaseFile["kind"], string> = {
  draft: "Contract draft",
  signed: "Signed contract",
  offer: "Offer",
  email: "Supplier email",
  attachment: "Attachment",
};

function size(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * The files the case received (renewal plan F1): drafts, the signed contract, offers, supplier emails
 * uploaded as .eml/.msg and their attachments — downloadable, and an attachment can be used as the
 * contract document without downloading and uploading it again.
 */
export function CaseFiles({ apiClient, tenantId, caseId, version, canUse, onCase }: CaseFilesProps) {
  const { t, lang } = useI18n();
  const [files, setFiles] = useState<CaseFile[]>([]);
  const [busy, setBusy] = useState<string | null>(null);
  const [failed, setFailed] = useState<string | null>(null);

  useEffect(() => {
    if (!apiClient.listRenewalCaseFiles) return;
    let alive = true;
    void apiClient.listRenewalCaseFiles(tenantId, caseId).then((r) => {
      if (alive && r.ok) setFiles(r.body);
    });
    return () => {
      alive = false;
    };
  }, [apiClient, tenantId, caseId, version]);

  if (files.length === 0) return null;

  const download = async (f: CaseFile) => {
    if (!apiClient.downloadRenewalCaseFile) return;
    setFailed(null);
    const blob = await apiClient.downloadRenewalCaseFile(tenantId, caseId, f.id);
    if (!blob) {
      setFailed(t("The file could not be downloaded."));
      return;
    }
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = f.fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  };

  const use = async (f: CaseFile) => {
    if (!apiClient.applyRenewalCaseFile) return;
    setBusy(f.id);
    setFailed(null);
    const r = await apiClient.applyRenewalCaseFile(tenantId, caseId, f.id, { type: "addDoc", version, lang });
    setBusy(null);
    onCase(r.ok ? r.body : r.current, r.ok);
    if (!r.ok) setFailed(t("Raffa could not use this file in the case."));
  };

  return (
    <div className="p4-files" data-screen-label="File della pratica">
      <div className="p4-col-head">{t("Case files · {n}", { n: files.length })}</div>
      {files.map((f) => (
        <div key={f.id} className={f.kind === "attachment" ? "p4-file is-attachment" : "p4-file"}>
          <span className="p4-file-kind">{t(KIND_LABELS[f.kind])}</span>
          <span className="p4-file-name p4-wrap">{f.fileName}</span>
          <span className="p4-muted p4-nowrap">
            {size(f.sizeBytes)} · {new Date(f.createdAt).toLocaleDateString(lang === "en" ? "en-GB" : "it-IT")} · {f.uploadedBy}
          </span>
          {f.documentId ? <span className="p4-file-note">{t("Filed in Documents")}</span> : null}
          <span className="p4-file-actions">
            <button type="button" className="btn btn-ghost" onClick={() => void download(f)}>
              {t("Download")}
            </button>
            {f.kind === "attachment" && canUse ? (
              <button type="button" className="btn btn-secondary" disabled={busy !== null} onClick={() => void use(f)}>
                {busy === f.id ? t("Raffa is reading the file…") : t("Use as contract document")}
              </button>
            ) : null}
          </span>
        </div>
      ))}
      {failed ? (
        <div className="p4-warn" role="alert">
          {failed}
        </div>
      ) : null}
    </div>
  );
}
