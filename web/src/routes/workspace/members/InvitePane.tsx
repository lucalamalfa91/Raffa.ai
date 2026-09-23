import { useState, type FormEvent } from "react";
import {
  IDENTITY_ONE_TIME_CODE_LINE,
  INVITE_ROLE_LABEL,
  INVITE_ROLE_ORDER,
  INVITE_ROLE_SUMMARY,
  NO_INVITATION_CREATED_META,
  composeAcceptLink,
  inviteEmailPlaceholder,
  inviteFailureCopy,
  inviteLinkExpiryMeta,
  inviteOutcomeSentence,
  type Day1InviteRole,
  type InviteOutcome,
} from "./memberViewModel";

/** The 502's fact (task E17/F02/US01/T01): the directory would not provision the guest, and NO
 * invitation was created. `failureReason` is the server's closed-set code, mapped to copy by
 * `inviteFailureCopy` -- never rendered raw. */
export interface InviteFailure {
  failureReason: string;
  email: string;
}

export interface InvitePaneProps {
  email: string;
  role: Day1InviteRole;
  /** The signed-in Admin's own email domain -- drives the placeholder; `null` when it cannot be derived. */
  tenantDomain: string | null;
  /** A blocking failure: a format error caught before the request, or the server's own 400/409. */
  error: string | null;
  /** A blocking 502 -- the directory refused the guest; rendered in the same pre-creation error slot
   * as `error`, with its designed copy and the "No invitation was created." meta. */
  failure: InviteFailure | null;
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
 * Task E17/F02/US01/T01 (wave w15, NW-69/NW-68/NW-67; ADR-020 w15 §3): the outcome is the server's
 * own `deliveryOutcome`, in exactly three states -- "Invitation sent to {email}.", "Invitation
 * created, but the email could not be sent.", "Invitation ready for {email}." -- each with the
 * copyable accept link (demo has no ACS transport; even `sent` is only "ACS accepted", not inbox
 * delivery) and the one-time-code sentence while the 201's `identityProvisioned` is true. A 502
 * renders its designed copy in the pre-creation error slot with "No invitation was created."
 * beneath it. There is deliberately no resend affordance on this pane: the server cannot re-send
 * the original link (the token is stored only as a hash), so any retry is a re-issue that kills
 * the link the Admin is looking at -- the link block IS the remedy, and the roster row's
 * "Send a new invitation" remains the only re-issue path (ADR-020 w15 §3.4).
 */
export default function InvitePane({
  email,
  role,
  tenantDomain,
  error,
  failure,
  domainWarning,
  outcome,
  submitting,
  onEmailChange,
  onRoleChange,
  onSubmit,
}: InvitePaneProps) {
  // "Copied" belongs to the outcome whose link was copied: a new invite (or none) reads "Copy link"
  // again by construction. Derived rather than reset in an effect -- a passive effect for a fresh
  // outcome could run after a quick click and wipe the confirmation it had just shown.
  const [copiedOutcome, setCopiedOutcome] = useState<InviteOutcome | null>(null);
  const linkCopied = outcome !== null && copiedOutcome === outcome;

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSubmit();
  };

  const copyLink = (link: string) => {
    const clipboard = typeof navigator === "undefined" ? undefined : navigator.clipboard;
    if (!clipboard) return;
    const copiedFor = outcome;
    void clipboard.writeText(link).then(() => setCopiedOutcome(copiedFor));
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

        {error === null && failure !== null && (
          <div className="members-invite-error" role="alert">
            <p className="members-invite-failure">{inviteFailureCopy(failure.failureReason, failure.email)}</p>
            <p className="micro-meta">{NO_INVITATION_CREATED_META}</p>
          </div>
        )}

        {failure === null && outcome !== null && (
          <div className="members-invite-link" role="status">
            <p className="members-invite-outcome">{inviteOutcomeSentence(outcome)}</p>
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
            {outcome.identityProvisioned && <p className="micro-meta">{IDENTITY_ONE_TIME_CODE_LINE}</p>}
          </div>
        )}
      </form>
    </aside>
  );
}
