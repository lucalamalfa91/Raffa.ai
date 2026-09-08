import type { FormEvent } from "react";
import type { Day1InviteRole } from "./memberViewModel";
import { INVITE_ROLE_LABEL, INVITE_ROLE_SUMMARY } from "./memberViewModel";

export interface InvitePaneProps {
  email: string;
  role: Day1InviteRole;
  error: string | null;
  submitting: boolean;
  onEmailChange: (email: string) => void;
  onRoleChange: (role: Day1InviteRole) => void;
  onSubmit: () => void;
}

const ROLES: readonly Day1InviteRole[] = ["Admin", "Procurement"];

/**
 * AC-2 invite pane: email + role radio (Admin vs Procurement with permission summaries) + Send.
 * Radios use the ADR-019 `.radio + .dot` pair; the native control stays in the accessibility tree.
 */
export default function InvitePane({
  email,
  role,
  error,
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
    <aside className="detail-pane members-invite-pane">
      <h3>Invite a member</h3>
      <p className="micro-meta">Send an invitation with a Day-1 role. They appear in the table as Invited until they sign in.</p>

      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="invite-email">Email</label>
          <input
            id="invite-email"
            className="input"
            type="email"
            name="email"
            autoComplete="off"
            value={email}
            onChange={(event) => onEmailChange(event.target.value)}
            disabled={submitting}
          />
        </div>

        <fieldset className="invite-role-fieldset">
          <legend>Role</legend>
          {ROLES.map((option) => (
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
                <span className="invite-role-label">{INVITE_ROLE_LABEL[option]}</span>
                <span className="micro-meta">{INVITE_ROLE_SUMMARY[option]}</span>
              </span>
            </label>
          ))}
        </fieldset>

        {error !== null && (
          <p className="hint" role="alert">
            {error}
          </p>
        )}

        <button type="submit" className="btn btn-primary btn-block" disabled={submitting}>
          {submitting ? "Sending…" : "Send invitation"}
        </button>
      </form>
    </aside>
  );
}
