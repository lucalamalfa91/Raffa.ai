import { useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import type { MailboxStatus, NotificationChannelKind, NotificationSettings } from "../../api/outreach";
import { useTenantId } from "../../routes/contracts/contracts4";
import { useI18n } from "../../i18n/I18nProvider";
import { pushSupported, subscribeToPush } from "../../notifications/webPush";
import "./settings.css";

export interface NotificationsSettingsProps {
  apiClient: ApiClient;
}

const PROVIDER_LABELS: Record<string, string> = { microsoft: "Microsoft 365 / Outlook", google: "Google Workspace / Gmail" };

const KIND_LABELS: Record<NotificationChannelKind, string> = {
  webpush: "Browser",
  email: "Email",
  teams: "Microsoft Teams",
  slack: "Slack",
};

const MAILBOX_RESULTS: Record<string, string> = {
  connected: "Mailbox connected. Raffa can now send the approved emails from your address.",
  denied: "The mailbox was not connected: the permission was not granted.",
  expired: "The connection took too long. Try again.",
  failed: "The provider did not confirm the connection. Try again.",
  unavailable: "This mailbox provider is not available.",
};

/**
 * Impostazioni → Email e notifiche (renewal plan F2/F3b): the person's company mailbox (Raffa sends the
 * approved negotiation emails from it, never reads it) and where they want to hear that a supplier answered:
 * in the app always, plus this browser, email, a Teams or a Slack channel.
 */
export default function NotificationsSettings({ apiClient }: NotificationsSettingsProps) {
  const tenantId = useTenantId() ?? undefined;
  const { t } = useI18n();
  const [search] = useSearchParams();
  const [mailbox, setMailbox] = useState<MailboxStatus | null>(null);
  const [settings, setSettings] = useState<NotificationSettings | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [webhook, setWebhook] = useState<{ teams: string; slack: string; email: string }>({ teams: "", slack: "", email: "" });
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (!tenantId) return;
    const [m, s] = await Promise.all([apiClient.getMailbox?.(tenantId), apiClient.getNotificationSettings?.(tenantId)]);
    if (m?.ok) setMailbox(m.body);
    if (s?.ok) setSettings(s.body);
  }, [apiClient, tenantId]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    const result = search.get("mailbox");
    if (result && MAILBOX_RESULTS[result]) {
      if (result === "connected") setMessage(t(MAILBOX_RESULTS[result]));
      else setError(t(MAILBOX_RESULTS[result]));
    }
  }, [search, t]);

  const run = async (work: () => Promise<string | null>) => {
    setBusy(true);
    setError(null);
    setMessage(null);
    const failure = await work();
    if (failure) setError(failure);
    setBusy(false);
    await load();
  };

  const connect = (provider: string) =>
    run(async () => {
      if (!tenantId || !apiClient.connectMailbox) return null;
      const r = await apiClient.connectMailbox(tenantId, provider);
      if (!r.ok) return t("This mailbox provider is not available.");
      window.location.assign(r.body.url);
      return null;
    });

  const disconnect = () =>
    run(async () => {
      if (!tenantId || !apiClient.disconnectMailbox) return null;
      await apiClient.disconnectMailbox(tenantId);
      setMessage(t("Mailbox disconnected. Raffa no longer sends from it."));
      return null;
    });

  const add = (kind: NotificationChannelKind, target: string, label?: string) =>
    run(async () => {
      if (!tenantId || !apiClient.addNotificationChannel) return null;
      const r = await apiClient.addNotificationChannel(tenantId, { kind, target, label });
      if (!r.ok) return r.error;
      setWebhook((w) => ({ ...w, [kind]: "" }));
      setMessage(t("Channel added. Send a test to check it."));
      return null;
    });

  const enablePush = () =>
    run(async () => {
      if (!settings?.vapidPublicKey) return null;
      const outcome = await subscribeToPush(settings.vapidPublicKey);
      if (!outcome.ok) {
        return outcome.reason === "denied"
          ? t("The browser blocked notifications for Raffa. Allow them in the site settings, then try again.")
          : t("This browser cannot receive notifications.");
      }
      if (!tenantId || !apiClient.addNotificationChannel) return null;
      const r = await apiClient.addNotificationChannel(tenantId, { kind: "webpush", target: outcome.subscription, label: outcome.label });
      if (!r.ok) return r.error;
      setMessage(t("This browser will show Raffa's notifications."));
      return null;
    });

  const available = new Set(settings?.available ?? []);
  const mb = mailbox?.mailbox ?? null;

  return (
    <div className="st-page" data-screen-label="Email e notifiche">
      <h6 className="st-role">{t("Settings")}</h6>
      <h2 className="st-title">{t("Email and notifications")}</h2>
      <p className="st-sub">
        {t("Connect your company mailbox so Raffa sends the approved negotiation emails from your address, and choose where Raffa tells you that a supplier answered.")}{" "}
        <Link to="/settings">{t("Company rules")}</Link>
      </p>
      {message ? <div className="st-note" role="status">{message}</div> : null}
      {error ? <div className="st-note st-error" role="alert">{error}</div> : null}

      <div className="st-grid">
        <section className="st-card" aria-labelledby="st-mailbox">
          <h4 id="st-mailbox">{t("Company mailbox")}</h4>
          {mb ? (
            <>
              <div>
                <strong>{mb.address}</strong> · {PROVIDER_LABELS[mb.provider] ?? mb.provider}
              </div>
              {mb.needsReconnect ? <div className="st-note st-error">{t("The connection has expired: connect the mailbox again to keep sending from Raffa.")}</div> : null}
              <p className="st-sub">{t("Raffa only has permission to send: it never reads your mailbox. The supplier's replies reach the case through the case address Raffa puts in copy.")}</p>
              <div className="st-actions">
                {mb.needsReconnect ? (
                  <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void connect(mb.provider)}>
                    {t("Connect again")}
                  </button>
                ) : null}
                <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => void disconnect()}>
                  {t("Disconnect")}
                </button>
              </div>
            </>
          ) : mailbox?.available ? (
            <>
              <p className="st-sub">{t("Raffa only asks for permission to send: it never reads your mailbox.")}</p>
              <div className="st-actions">
                {mailbox.providers.map((p) => (
                  <button key={p} type="button" className="btn btn-primary" disabled={busy} onClick={() => void connect(p)}>
                    {t("Connect {provider}", { provider: PROVIDER_LABELS[p] ?? p })}
                  </button>
                ))}
              </div>
            </>
          ) : (
            <p className="st-sub">{t("Sending from your mailbox is not set up on this server yet. You can still copy the email and confirm you sent it.")}</p>
          )}
        </section>

        <section className="st-card" aria-labelledby="st-channels">
          <h4 id="st-channels">{t("Where to notify you")}</h4>
          <p className="st-sub">{t("Notifications always appear in Raffa (the bell). Outside Raffa they only say who wrote and on which case — never prices, terms or the text of the email.")}</p>
          {(settings?.channels ?? []).map((c) => (
            <div key={c.id} className="st-channel">
              <span className="st-channel-kind">{t(KIND_LABELS[c.kind])}</span>
              <span className="st-channel-label">{c.label}</span>
              {c.lastError ? <span className="st-channel-error">{c.lastError}</span> : null}
              <label className="st-channel-toggle">
                <input
                  type="checkbox"
                  checked={c.enabled}
                  onChange={(e) => void run(async () => {
                    if (tenantId) await apiClient.updateNotificationChannel?.(tenantId, c.id, e.target.checked);
                    return null;
                  })}
                />
                {t("On")}
              </label>
              <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => void run(async () => {
                if (tenantId) await apiClient.removeNotificationChannel?.(tenantId, c.id);
                return null;
              })}>
                {t("Remove")}
              </button>
            </div>
          ))}

          {available.has("webpush") && pushSupported() ? (
            <div className="st-add">
              <button type="button" className="btn btn-secondary" disabled={busy} onClick={() => void enablePush()}>
                {t("Notify me in this browser")}
              </button>
            </div>
          ) : null}

          {available.has("email") ? (
            <div className="st-add">
              <label>
                {t("Email address")}
                <input className="input" type="email" value={webhook.email} onChange={(e) => setWebhook((w) => ({ ...w, email: e.target.value }))} />
              </label>
              <button type="button" className="btn btn-secondary" disabled={busy || !webhook.email.trim()} onClick={() => void add("email", webhook.email)}>
                {t("Add email")}
              </button>
            </div>
          ) : null}

          {available.has("teams") ? (
            <div className="st-add">
              <label>
                {t("Microsoft Teams — Workflows webhook URL")}
                <input className="input" value={webhook.teams} placeholder="https://…logic.azure.com/…" onChange={(e) => setWebhook((w) => ({ ...w, teams: e.target.value }))} />
              </label>
              <span className="st-help">{t("In Teams: the channel or chat → … → Workflows → “Post to a channel when a webhook request is received”, then copy the URL here.")}</span>
              <button type="button" className="btn btn-secondary" disabled={busy || !webhook.teams.trim()} onClick={() => void add("teams", webhook.teams)}>
                {t("Add Teams")}
              </button>
            </div>
          ) : null}

          {available.has("slack") ? (
            <div className="st-add">
              <label>
                {t("Slack — Incoming Webhook URL")}
                <input className="input" value={webhook.slack} placeholder="https://hooks.slack.com/services/…" onChange={(e) => setWebhook((w) => ({ ...w, slack: e.target.value }))} />
              </label>
              <span className="st-help">{t("In Slack: create an app with Incoming Webhooks for the channel you want, then copy the webhook URL here.")}</span>
              <button type="button" className="btn btn-secondary" disabled={busy || !webhook.slack.trim()} onClick={() => void add("slack", webhook.slack)}>
                {t("Add Slack")}
              </button>
            </div>
          ) : null}

          {available.size === 0 ? <p className="st-sub">{t("External notifications are not set up on this server yet.")}</p> : null}

          <div className="st-actions">
            <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => void run(async () => {
              if (tenantId) await apiClient.testNotifications?.(tenantId);
              setMessage(t("Test sent. It is in the bell and on every channel that is on."));
              return null;
            })}>
              {t("Send a test notification")}
            </button>
          </div>
        </section>
      </div>
    </div>
  );
}
