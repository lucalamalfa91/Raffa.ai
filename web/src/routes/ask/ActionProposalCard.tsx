import type { ActionProposal } from "../../api/client";
import { useI18n } from "../../i18n/I18nProvider";

export type ProposalState = { status: "running" } | { status: "done"; savings?: boolean } | { status: "declined" } | { status: "error"; message: string };

/**
 * Raffa asks before it acts: the proposal's confirm / cancel buttons (typing "yes" works too) and,
 * once answered, what happened. Nothing runs until the user confirms.
 */
export default function ActionProposalCard({
  proposal,
  state,
  active,
  onConfirm,
  onDecline,
  onOpenSavings,
}: {
  proposal: ActionProposal;
  state: ProposalState | undefined;
  /** True only for the proposal on the conversation's last message. */
  active: boolean;
  onConfirm: () => void;
  onDecline: () => void;
  onOpenSavings: () => void;
}) {
  const { t } = useI18n();
  const renewal = proposal.kind === "start_renewal";
  if (state?.status === "done") {
    return (
      <div className="k6-proposal is-done" data-parity-hide role="status">
        <span>{state.savings ? t("Savings goal set.") : t("Renewal started.")}</span>
        {state.savings ? (
          <button type="button" className="btn btn-secondary k6-qa-cta" onClick={onOpenSavings}>
            {t("Open Spend and savings")}
          </button>
        ) : null}
      </div>
    );
  }
  if (state?.status === "declined") {
    return <div className="k6-proposal is-declined" data-parity-hide>{t("Okay, nothing changed.")}</div>;
  }
  if (!active && !state) return null;
  const running = state?.status === "running";
  return (
    <div className="k6-proposal" data-parity-hide>
      <div className="k6-proposal-acts">
        <button type="button" className="btn btn-primary k6-qa-cta" disabled={running} onClick={onConfirm}>
          {running ? t("Raffa is working…") : renewal ? t("Yes, start the renewal") : t("Yes, set the goal")}
        </button>
        <button type="button" className="btn btn-ghost k6-qa-cta" disabled={running} onClick={onDecline}>
          {t("No, leave it")}
        </button>
      </div>
      {state?.status === "error" ? (
        <div className="k6-conv-error" role="alert">
          {state.message}
        </div>
      ) : null}
    </div>
  );
}
