import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import type { NotificationFeed, NotificationItem } from "../../api/outreach";
import { useI18n } from "../../i18n/I18nProvider";

interface NotificationBellProps {
  apiClient: ApiClient;
  tenantId: string;
}

const POLL_MS = 60_000;

/** The known kinds, written in the reader's language from their values; anything else as the server wrote it. */
const TEMPLATES: Record<string, { title: string; body: string }> = {
  supplier_message: { title: "New email from {supplier}", body: "{contract}: confirm it belongs to the negotiation to add it to the case." },
  approval_requested: { title: "An email to {supplier} waits for your approval", body: "{contract}: review the exact version and approve it." },
  test: { title: "Raffa — test notification", body: "Notifications reach you here." },
};

/**
 * The bell in the top nav (renewal plan F3b): the caller's notifications — a supplier answered, an email
 * waits for approval — with the unread count. Opening one marks it read and goes to its case.
 */
export default function NotificationBell({ apiClient, tenantId }: NotificationBellProps) {
  const { t, lang } = useI18n();
  const navigate = useNavigate();
  const [feed, setFeed] = useState<NotificationFeed | null>(null);
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement | null>(null);

  const load = useCallback(async () => {
    if (!apiClient.getNotifications) return;
    const r = await apiClient.getNotifications(tenantId, 20);
    if (r.ok) setFeed(r.body);
  }, [apiClient, tenantId]);

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") void load();
    }, POLL_MS);
    return () => window.clearInterval(timer);
  }, [load]);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => {
      if (box.current && !box.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  if (!apiClient.getNotifications) return null;

  const openItem = async (item: NotificationItem) => {
    setOpen(false);
    if (!item.read) {
      await apiClient.markNotificationsRead?.(tenantId, item.id);
      void load();
    }
    if (item.link) navigate(item.link);
  };

  const readAll = async () => {
    await apiClient.markNotificationsRead?.(tenantId, null);
    void load();
  };

  const unread = feed?.unread ?? 0;
  return (
    <div className="v4-bell" ref={box}>
      <button
        type="button"
        className={unread > 0 ? "v4-bell-button has-unread" : "v4-bell-button"}
        aria-haspopup="true"
        aria-expanded={open}
        aria-label={unread > 0 ? t("Notifications · {n} unread", { n: unread }) : t("Notifications")}
        onClick={() => {
          setOpen((o) => !o);
          if (!open) void load();
        }}
      >
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
          <path d="M12 3a6 6 0 0 0-6 6v4l-2 3h16l-2-3V9a6 6 0 0 0-6-6zm0 18a2.5 2.5 0 0 0 2.4-2h-4.8A2.5 2.5 0 0 0 12 21z" fill="currentColor" />
        </svg>
        {unread > 0 ? <span className="v4-bell-count">{unread > 99 ? "99+" : unread}</span> : null}
      </button>
      {open ? (
        <div className="v4-bell-panel" role="menu">
          <div className="v4-bell-head">
            <strong>{t("Notifications")}</strong>
            {unread > 0 ? (
              <button type="button" className="btn btn-ghost" onClick={() => void readAll()}>
                {t("Mark all read")}
              </button>
            ) : null}
          </div>
          {(feed?.items ?? []).length === 0 ? <div className="v4-bell-empty">{t("No notifications yet.")}</div> : null}
          {(feed?.items ?? []).map((item) => (
            <button key={item.id} type="button" role="menuitem" className={item.read ? "v4-bell-item" : "v4-bell-item is-unread"} onClick={() => void openItem(item)}>
              <span className="v4-bell-title">{TEMPLATES[item.kind] ? t(TEMPLATES[item.kind]!.title, item.vars ?? {}) : item.title}</span>
              <span className="v4-bell-body">{TEMPLATES[item.kind] ? t(TEMPLATES[item.kind]!.body, item.vars ?? {}) : item.body}</span>
              <span className="v4-bell-when">{new Date(item.createdAt).toLocaleString(lang === "en" ? "en-GB" : "it-IT", { dateStyle: "medium", timeStyle: "short" })}</span>
            </button>
          ))}
          <button type="button" className="v4-bell-settings" onClick={() => { setOpen(false); navigate("/settings/notifications"); }}>
            {t("Email and notifications settings")}
          </button>
        </div>
      ) : null}
    </div>
  );
}
