import type { FormEvent } from "react";
import {
  INVITE_ROLE_LABEL,
  INVITE_ROLE_ORDER,
  INVITE_ROLE_SUMMARY,
  buildAcceptLink,
  formatInvitationLinkMeta,
  formatInvitationReadyMessage,
  formatInvitationSentMessage,
  inviteEmailPlaceholder,
  type Day1InviteRole,
  type InviteOutcome,
} from "./memberViewModel";

export interface InvitePaneProps {
  email: string;
  role: Day1InviteRole;
  /** The signed-in Admin's own email domain -- drives the placeholder; `null` when it cannot be derived. */
  tenantDomain: string | null;
  /** Blocking -- a malformed/empty address. Submit does not fire while this is set. */
  error: string | null;
  /** Non-blocking -- a cross-domain address. Submit still fires; this is informative only. */
  warning: string | null;
  /** The server's own outcome for the most recent successful invite; `null` before any invite, or
   * once the form is edited or submitted again. */
  result: InviteOutcome | null;
  submitting: boolean;
  onEmailChange: (email: string) => void;
  onRoleChange: (role: Day1InviteRole) => void;
  onSubmit: () => void;
}

/**
 * The V2 "Invite a colleague" pane (screens-v2.md #10; `raffa-v2/markup.html` "WORKSPACE &
 * MEMBERS" block, right column): h4 → "Work email" field → two stacked radios (Procurement first,
 * each with a bold label and a muted one-line summary, `.radio` + `.dot` from the ADR-019 catalogue,
 * the native control stays in the accessibility tree) → block "Send invitation" → the blocking
 * error, or the non-blocking domain warning, or the server's own honest outcome (ADR-020 §10.2):
 * "Invitation sent to {email}." when `mailDelivered`, otherwise "Invitation ready for {email}." with
 * the copyable single-use link, its expiry and a **Copy link** button -- the word "sent" never
 * appears in that second case, because the client never infers delivery from a mere 201.
 */
export default function InvitePane({
  email,
  role,
  tenantDomain,
  error,
  warning,
  result,
  submitting,
  onEmailChange,
  onRoleChange,
  onSubmit,
}: InvitePaneProps) {
  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSubmit();
  };

  const handleCopyLink = (acceptUrl: string) => {
    const link = buildAcceptLink(acceptUrl);
    if (typeof navigator !== "undefined" && navigator.clipboard) {
      void navigator.clipboard.writeText(link);
    }
  };

  return (
    <aside className="members-invite-pane" aria-label="Invite a colleague">
      <h4>Invite a colleague</h4>

      <form onSubmit={handleSubmit} noValidate>
        <div className="field members-invite-field">
          <label htmlFor="invite-email">Work email</label>
          <input
            id="invite-email"
            className="input"
            type="email"
            name="email"
            autoComplete="off"
            placeholder={inviteEmailPlaceholder(tenantDomain)}
            value={email}
            onChange={(event) => onEmailChange(event.target.value)}
            disabled={submitting}
          />
        </div>

        <fieldset className="invite-role-fieldset">
          <legend className="visually-hidden">Role</legend>
          {INVITE_ROLE_ORDER.map((option) => (
            <label key={option} className="invite-role-option">
              <span className="invite-role-control">
                <input
                  type="radio"
                  className="radio"
                  name="invite-role"
                  value={option}
                  checked={role === option}
                  onChange={() => onRoleChange(option)}
                  disabled={submitting}
                />
                <span className="dot" />
              </span>
              <span className="invite-role-copy">
                <strong className="invite-role-label">{INVITE_ROLE_LABEL[option]}</strong>
                <span className="invite-role-summary">{INVITE_ROLE_SUMMARY[option]}</span>
              </span>
            </label>
          ))}
        </fieldset>

        <button type="submit" className="btn btn-primary btn-block" disabled={submitting}>
          {submitting ? "Sending…" : "Send invitation"}
        </button>

        {error !== null && (
          <p className="members-invite-error" role="alert">
            {error}
          </p>
        )}

        {error === null && warning !== null && (
          <p className="members-invite-warning" role="note">
            {warning}
          </p>
        )}

        {error === null &&
          result !== null &&
          (result.mailDelivered ? (
            <p className="members-invite-sent" role="status">
              {formatInvitationSentMessage(result.email)}
            </p>
          ) : (
            <div className="members-invite-ready" role="status">
              <p>{formatInvitationReadyMessage(result.email)}</p>
              <p className="micro-meta">{formatInvitationLinkMeta(result.expiresAt)}</p>
              <div className="members-invite-link-row">
                <input
                  type="text"
                  className="input members-invite-link-input"
                  aria-label="Invitation link"
                  readOnly
                  value={buildAcceptLink(result.acceptUrl)}
                  onFocus={(event) => event.currentTarget.select()}
                />
                <button type="button" className="btn btn-secondary" onClick={() => handleCopyLink(result.acceptUrl)}>
                  Copy link
                </button>
              </div>
            </div>
          ))}
      </form>
    </aside>
  );
}
