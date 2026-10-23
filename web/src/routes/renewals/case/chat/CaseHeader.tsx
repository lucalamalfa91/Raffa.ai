import { useId, useState } from "react";
import type { ActPayload, ActionType, CasePerson, CaseView } from "../types";
import type { Translate } from "../../../../i18n/translate";

export interface CaseHeaderProps {
  view: CaseView;
  t: Translate;
  onAct: (type: ActionType, payload?: ActPayload) => void;
  onHistory: () => void;
  onNewChat: () => void;
}

/**
 * The case header: what this renewal is, where it stands, who answers for it, and the seven wizard
 * steps. The old "Timeline & history" button is gone -- history is one link to the Renewals case.
 */
export default function CaseHeader({ view, t, onAct, onHistory, onNewChat }: CaseHeaderProps) {
  const { hdr } = view;
  const [teamOpen, setTeamOpen] = useState(false);
  const teamId = useId();

  return (
    <header className="rc-header">
      <div className="rc-title-row">
        <span className="rc-marker" aria-hidden="true" />
        <h2 className="rc-case-title">{hdr.title}</h2>
      </div>

      <div className="rc-facts">
        <strong className="rc-status">{hdr.status}</strong>
        <span>
          <span className="rc-label">{t("Next")}</span> {hdr.next}
        </span>
        <span>
          <span className="rc-label">{t("Notice by")}</span>{" "}
          <strong>
            {hdr.noticeBy} · {hdr.noticeDays} {t("days")}
          </strong>
        </span>
        <span>
          <span className="rc-label">{t("Ends")}</span> {hdr.end}
        </span>
        <span>
          <span className="rc-label">{t("Owner")}</span> {hdr.owner}
        </span>
        <span>
          <span className="rc-label">{t("Approver")}</span> {hdr.approver} · {t("you")}: {hdr.youRole}
        </span>
      </div>

      <ol className="rc-stepper" aria-label={t("Renewal steps")}>
        {view.steps7.map((step) => (
          <li key={step.n} className={step.now ? "is-now" : step.done ? "is-done" : undefined} aria-current={step.now ? "step" : undefined}>
            <span className="rc-step-n">{step.n}</span>
            <span className="rc-step-label">{step.label}</span>
          </li>
        ))}
      </ol>

      {hdr.tracks.length > 0 && (
        <div className="rc-tracks">
          {hdr.tracks.map((track) => (
            <span key={track}>{track}</span>
          ))}
        </div>
      )}

      {hdr.warn && (
        <div className="rc-banner rc-warn" role="alert">
          {hdr.warn}
        </div>
      )}

      <div className="rc-links">
        <button type="button" className="btn btn-ghost" onClick={onHistory}>
          {t("History →")}
        </button>
        <button type="button" className="btn btn-ghost" aria-expanded={teamOpen} aria-controls={teamId} onClick={() => setTeamOpen((o) => !o)}>
          {t("Team & delegations")} {teamOpen ? "▴" : "▾"}
        </button>
        <button type="button" className="btn btn-ghost rc-links-end" onClick={onNewChat}>
          {t("+ New chat")}
        </button>
      </div>

      {teamOpen && (
        <div className="rc-team" id={teamId}>
          {hdr.people.map((person) => (
            <Person key={person.name} person={person} manage={view.permissions.manageTeam} t={t} onAct={onAct} />
          ))}
          <p className="rc-note">{t("Delegates are people already allowed in this workspace. The chat never widens access to documents.")}</p>
        </div>
      )}
    </header>
  );
}

function Person({ person, manage, t, onAct }: { person: CasePerson; manage: boolean; t: Translate; onAct: CaseHeaderProps["onAct"] }) {
  const canManage = manage && person.responsibility !== "approver";
  return (
    <div className="rc-person">
      <strong>{person.name}</strong>
      <span className="rc-person-role">{person.role}</span>
      {canManage && (
        <button type="button" className="btn btn-ghost" onClick={() => onAct("delegate", { who: person.name, on: !person.isDelegate })}>
          {person.isDelegate ? t("Remove delegation") : t("Make delegate")}
        </button>
      )}
      {canManage && person.isDelegate && (
        <button type="button" className="btn btn-ghost" onClick={() => onAct("delegateClose", { who: person.name })}>
          {person.canClose ? t("Remove closing right") : t("Allow closing")}
        </button>
      )}
      {canManage && person.responsibility !== "owner" && (
        <button type="button" className="btn btn-ghost" onClick={() => onAct("transfer", { who: person.name })}>
          {t("Reassign ownership")}
        </button>
      )}
    </div>
  );
}
