/**
 * Renewal plan F2/F3/F3b (docs/architecture/renewal-in-app-mail-plan.md): the caller's company mailbox, a
 * case's mail (its address, the emails that reached it) and notifications.
 */

export interface MailboxInfo {
  /** microsoft | google */
  provider: string;
  address: string;
  displayName: string | null;
  connectedAt: string;
  lastUsedAt: string | null;
  /** The grant expired or was revoked: connect again before sending. */
  needsReconnect: boolean;
}

/** `GET /api/mailbox`. */
export interface MailboxStatus {
  mailbox: MailboxInfo | null;
  /** The providers this server offers (microsoft, google). */
  providers: string[];
  available: boolean;
}

export interface InboundItem {
  id: string;
  from: string;
  subject: string;
  receivedAt: string;
  preview: string;
  attachments: string[];
  /** pending | accepted | dismissed */
  state: string;
  decidedBy: string | null;
  decidedAt: string | null;
}

/** `GET /api/renewals/{caseId}/mail`. */
export interface CaseMailStatus {
  /** `r-<token>@<inbound domain>`, in CC and Reply-To of what Raffa sends; null when inbound mail is off. */
  caseAddress: string | null;
  mailbox: MailboxInfo | null;
  mailboxAvailable: boolean;
  inbound: InboundItem[];
}

export interface NotificationItem {
  id: string;
  kind: string;
  title: string;
  body: string;
  link: string | null;
  caseId: string | null;
  createdAt: string;
  read: boolean;
  /** The values in the title and body (supplier, contract), to show them in the reader's language. */
  vars?: Record<string, string> | null;
}

/** `GET /api/notifications`. */
export interface NotificationFeed {
  items: NotificationItem[];
  unread: number;
}

export type NotificationChannelKind = "webpush" | "email" | "teams" | "slack";

export interface NotificationChannel {
  id: string;
  kind: NotificationChannelKind;
  label: string;
  enabled: boolean;
  createdAt: string;
  lastDeliveredAt: string | null;
  lastError: string | null;
}

/** `GET /api/notifications/settings`. */
export interface NotificationSettings {
  channels: NotificationChannel[];
  available: NotificationChannelKind[];
  vapidPublicKey: string | null;
}
