import type { FormEvent } from "react";
import {
  INVITATION_SENT_MESSAGE,
  INVITE_ROLE_LABEL,
  INVITE_ROLE_ORDER,
  INVITE_ROLE_SUMMARY,
  inviteEmailPlaceholder,
  type Day1InviteRole,
} from "./memberViewModel";

export interface InvitePaneProps {
  email: string;
  role: Day1InviteRole;
  /** The signed-in Admin's own email domain -- drives the placeholder; `null` when it cannot be derived. */
  tenantDomain: string | null;
  error: string | null;
  /** `true` right after a successful invite, until the form is edited or submitted again. */
  sent: boolean;
  submitting: boolean;
  onEmailChange: (email: string) => void;
  onRoleChange: (role: Day1InviteRole) => void;
  onSubmit: () => void;
}

/**
 * The V2 "Invite a colleague" pane (screens-v2.md #10; `contigo-v2/markup.html` "WORKSPACE &
 * MEMBERS" block, right column): h4 → "Work email" field → two stacked radios (Procurement first,
 * each with a bold label and a muted one-line summary, `.radio` + `.dot` from the ADR-019 catalogue,
 * the native control stays in the accessibility tree) → block "Send invitation" → either the accent
 * error line or "Invitation sent.".
 */
export default function InvitePane({
  email,
  role,
  tenantDomain,
  error,
  sent,
  submitting,
  onEmailChange,
  onRoleChange,
  onSubmit,
}: InvitePaneProps) {
  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSubmit();
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
        {sent && error === null && (
          <p className="members-invite-sent" role="status">
            {INVITATION_SENT_MESSAGE}
          </p>
        )}
      </form>
    </aside>
  );
}
