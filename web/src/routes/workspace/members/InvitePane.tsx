import { useEffect, useState, type FormEvent } from "react";
import {
  INVITE_ROLE_LABEL,
  INVITE_ROLE_ORDER,
  INVITE_ROLE_SUMMARY,
  composeAcceptLink,
  inviteEmailPlaceholder,
  inviteLinkExpiryMeta,
  type Day1InviteRole,
  type InviteOutcome,
} from "./memberViewModel";

export interface InvitePaneProps {
  email: string;
  role: Day1InviteRole;
  /** The signed-in Admin's own email domain -- drives the placeholder; `null` when it cannot be derived. */
  tenantDomain: string | null;
  /** A blocking failure: a format error caught before the request, or the server's own 400/409. */
  error: string | null;
  /** Non-blocking cross-domain notice (AC-5) -- submit stays enabled either way. */
  domainWarning: string | null;
  /** The server's own answer to the last successful invite, or `null` before one exists / after the
   * form is edited again. Never inferred from the 201 alone (N3b-1). */
  outcome: InviteOutcome | null;
  submitting: boolean;
  onEmailChange: (email: string) => void;
  onRoleChange: (role: Day1InviteRole) => void;
  onSubmit: () => void;
}

/**
 * The V2 "Invite a colleague" pane (screens-v2.md #10; `raffa-v2/markup.html` "WORKSPACE &
 * MEMBERS" block, right column): h4 -> "Work email" field -> two stacked radios (Procurement first,
 * each with a bold label and a muted one-line summary, `.radio` + `.dot` from the ADR-019 catalogue,
 * the native control stays in the accessibility tree) -> block "Send invitation" -> the outcome.
 *
 * Task E15/F02/US01/T01 (wave w14, N3b-1): the outcome is the server's own `mailDelivered` fact, in
 * exactly two strings (see the JSX below for the exact copy) -- one confirms delivery, the other
 * gives a copyable single-use link, its expiry, and a Copy link button, and deliberately never
 * describes what happened to the mail: that copy is written to stay true for both of that boolean's
 * `false` causes (no transport configured, transport errored) without ever diagnosing the mailer.
 */
export default function InvitePane({ email, role, tenantDomain, error, domainWarning, outcome, submitting, onEmailChange, onRoleChange, onSubmit }: InvitePaneProps) {
  const [linkCopied, setLinkCopied] = useState(false);

  useEffect(() => {
    setLinkCopied(false);
  }, [outcome]);

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSubmit();
  };

  const copyLink = (link: string) => {
    const clipboard = typeof navigator === "undefined" ? undefined : navigator.clipboard;
    if (!clipboard) return;
    void clipboard.writeText(link).then(() => setLinkCopied(true));
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
          {domainWarning !== null && <p className="micro-meta members-domain-warning">{domainWarning}</p>}
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

        {error === null && outcome !== null && outcome.mailDelivered && (
          <p className="members-invite-outcome" role="status">
            Invitation sent to {outcome.email}.
          </p>
        )}

        {error === null && outcome !== null && !outcome.mailDelivered && (
          <div className="members-invite-link" role="status">
            <p className="members-invite-outcome">Invitation ready for {outcome.email}.</p>
            <div className="members-invite-link-row">
              <input
                className="input members-invite-link-field"
                type="text"
                readOnly
                aria-label="Invitation link"
                value={composeAcceptLink(outcome.acceptUrl, window.location.origin)}
                onFocus={(event) => event.currentTarget.select()}
              />
              <button type="button" className="btn btn-secondary" onClick={() => copyLink(composeAcceptLink(outcome.acceptUrl, window.location.origin))}>
                {linkCopied ? "Copied" : "Copy link"}
              </button>
            </div>
            <p className="micro-meta">{inviteLinkExpiryMeta(outcome.expiresAt)}</p>
          </div>
        )}
      </form>
    </aside>
  );
}
