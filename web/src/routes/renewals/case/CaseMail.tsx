import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { ApiClient } from "../../../api/client";
import type { CaseCardEmail, CaseEnvelope } from "../../../api/renewalCases";
import type { CaseMailStatus, InboundItem } from "../../../api/outreach";
import { useI18n } from "../../../i18n/I18nProvider";

interface CaseMailProps {
  apiClient: ApiClient;
  tenantId: string;
  caseId: string;
  version: number;
  /** The caller can work on the case and it is open. */
  canWork: boolean;
  /** The email of the current step, when there is one. */
  email: CaseCardEmail | null;
  /** The case changed (sent, reply registered), or the current one returned with an error. */
  onCase: (next: CaseEnvelope | null) => void;
}

/** How often the case looks for new supplier emails while it is open. */
const POLL_MS = 60_000;

const SENDABLE = new Set(["approved", "copied"]);

const ERRORS: Record<string, string> = {
  no_mailbox: "Connect your company mailbox to send from Raffa.",
  mailbox_reconnect: "Your mailbox connection has expired. Connect it again, then send.",
  mailbox_refused: "Your mailbox refused the email. Nothing was sent; try again later.",
  no_recipient: "Add a valid recipient address before sending.",
  not_approved: "Only an approved email can be sent.",
  stale_version: "The case changed since you loaded it. Your text is kept; review the current version.",
};

/**
 * The case's mail (renewal plan F2/F3): send the approved email from the person's company mailbox — the
 * supplier sees their address, the case address goes in CC so the reply reaches Raffa — and confirm the
 * supplier emails that reached the case: nothing enters the case until the owner says it belongs here.
 */
export function CaseMail({ apiClient, tenantId, caseId, version, canWork, email, onCase }: CaseMailProps) {
  const { t, lang } = useI18n();
  const [status, setStatus] = useState<CaseMailStatus | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!apiClient.getCaseMail) return;
    const r = await apiClient.getCaseMail(tenantId, caseId);
    if (r.ok) setStatus(r.body);
  }, [apiClient, tenantId, caseId]);

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), POLL_MS);
    return () => window.clearInterval(timer);
  }, [load, version]);

  if (!status) return null;

  const send = async () => {
    if (!email || !apiClient.sendCaseEmail) return;
    setBusy("send");
    setError(null);
    const r = await apiClient.sendCaseEmail(tenantId, caseId, email.eid, version);
    setBusy(null);
    onCase(r.ok ? r.body : r.current);
    if (!r.ok) setError(t(ERRORS[r.code ?? ""] ?? "Raffa could not send the email."));
    void load();
  };

  const decide = async (item: InboundItem, accept: boolean) => {
    setBusy(item.id);
    setError(null);
    if (accept && apiClient.acceptInboundMail) {
      const r = await apiClient.acceptInboundMail(tenantId, caseId, item.id, version, lang);
      onCase(r.ok ? r.body : r.current);
      if (!r.ok && r.code !== "duplicate_reply") setError(t(ERRORS[r.code ?? ""] ?? "Raffa could not add this email to the case."));
    } else if (!accept && apiClient.dismissInboundMail) {
      await apiClient.dismissInboundMail(tenantId, caseId, item.id);
    }
    setBusy(null);
    void load();
  };

  const pending = status.inbound.filter((i) => i.state === "pending");
  const sendable = !!email && SENDABLE.has(email.stateKey) && canWork;
  const mailbox = status.mailbox;

  return (
    <div className="p4-mail" data-screen-label="Posta della pratica">
      {pending.map((item) => (
        <div key={item.id} className="p4-inbound" role="region" aria-label={t("New supplier email")}>
          <div className="p4-inbound-head">
            <span className="p4-inbound-kicker">{t("New supplier email")}</span>
            <span className="p4-muted">
              {item.from} · {new Date(item.receivedAt).toLocaleString(lang === "en" ? "en-GB" : "it-IT", { dateStyle: "medium", timeStyle: "short" })}
            </span>
          </div>
          <div className="p4-strong">{item.subject}</div>
          <div className="p4-inbound-preview">{item.preview}</div>
          {item.attachments.length > 0 ? (
            <div className="p4-muted">
              {t("Attachments")}: {item.attachments.join(", ")}
            </div>
          ) : null}
          {canWork ? (
            <div className="p4-inbound-actions">
              <button type="button" className="btn btn-primary" disabled={busy !== null} onClick={() => void decide(item, true)}>
                {busy === item.id ? t("Raffa is reading the email…") : t("It belongs to this negotiation — add it")}
              </button>
              <button type="button" className="btn btn-ghost" disabled={busy !== null} onClick={() => void decide(item, false)}>
                {t("Not for this case")}
              </button>
            </div>
          ) : null}
        </div>
      ))}

      {sendable ? (
        <div className="p4-send">
          {mailbox && !mailbox.needsReconnect ? (
            <>
              <button type="button" className="btn btn-primary" disabled={busy !== null || email!.toMissing} onClick={() => void send()}>
                {busy === "send" ? t("Sending…") : t("Send from {address}", { address: mailbox.address })}
              </button>
              <span className="p4-muted">
                {status.caseAddress
                  ? t("Raffa sends this exact version from your mailbox, with the contract attached and {address} in copy: the supplier's reply comes back to this case.", { address: status.caseAddress })
                  : t("Raffa sends this exact version from your mailbox, with the contract attached.")}
              </span>
            </>
          ) : status.mailboxAvailable ? (
            <span className="p4-muted">
              {mailbox?.needsReconnect ? t("Your mailbox connection has expired.") : t("Send it from Raffa with your company address:")}{" "}
              <Link to="/settings/notifications">{mailbox?.needsReconnect ? t("Connect it again") : t("connect your mailbox")}</Link>
            </span>
          ) : null}
        </div>
      ) : null}

      {error ? (
        <div className="p4-warn" role="alert">
          {error}
        </div>
      ) : null}
    </div>
  );
}
