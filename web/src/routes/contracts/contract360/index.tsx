import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import AskShortcuts from "../../../components/ask/AskShortcuts";
import NegotiationPoints from "../../../components/ask/NegotiationPoints";
import type { ApiClient, ContractV4Body, DecisionPointBody, DecisionPointChoice } from "../../../api/client";
import { useGuide } from "../../../components/shell/guide";
import { useI18n } from "../../../i18n/I18nProvider";
import { announceContractsChanged, chfK, enNumber, tx, URGENT_NOTICE_DAYS, useTenantId } from "../contracts4";
import "../contracts4.css";

export interface Contract360RouteProps {
  apiClient: ApiClient;
  /** Kept for the route table's signature; the API names who decided. */
  userLabel?: string;
}

const CHOICES: ReadonlyArray<{ value: DecisionPointChoice; label: string; recorded: string }> = [
  { value: "negotiate", label: "We negotiate it", recorded: "we negotiate it" },
  { value: "accept", label: "We accept it", recorded: "we accept it" },
  { value: "clarify", label: "To be clarified", recorded: "to be clarified" },
];

/**
 * AskRaffa 4.0 Contratto, decisions first (AskRaffa V4.dc.html lines 186-205): supplier + contract,
 * meta, the four decisive dates, ≤3 points that require a decision (*Lo negoziamo / Lo accettiamo /
 * Va chiarito*, recorded per contract with author and date), the next action (open or start the
 * renewal case) and, collapsed, the full contract (terms, products from the contract only,
 * obligations, standard terms). Data: `GET /api/contracts/{id}/v4`.
 */
export default function Contract360Route({ apiClient }: Contract360RouteProps) {
  const { contractId = "" } = useParams();
  const { t, lang } = useI18n();
  const navigate = useNavigate();
  const tenantId = useTenantId();
  const [contract, setContract] = useState<ContractV4Body | null>(null);
  const [failed, setFailed] = useState(false);
  const [allOpen, setAllOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState(false);

  const load = useCallback(async () => {
    if (!tenantId || !contractId) return;
    const result = await apiClient.getContractV4(tenantId, contractId);
    if (result.ok && result.data) {
      setContract(result.data);
      setFailed(false);
    } else {
      setFailed(true);
    }
  }, [apiClient, tenantId, contractId]);

  useEffect(() => {
    setAllOpen(false);
    void load();
  }, [load]);

  const decide = async (point: DecisionPointBody, choice: DecisionPointChoice) => {
    if (!tenantId || !contract) return;
    const next = point.decision === choice ? null : choice;
    // Optimistic: the toggle shows at once, the server's answer replaces it.
    setContract({
      ...contract,
      decisionPoints: contract.decisionPoints.map((p) => (p.key === point.key ? { ...p, decision: next } : p)),
    });
    const result = await apiClient.setDecisionPoint(tenantId, contract.contractId, point.key, next);
    if (result.ok && result.data) {
      const points = result.data.points;
      setContract((current) => (current ? { ...current, decisionPoints: points } : current));
      announceContractsChanged();
    } else {
      void load();
    }
  };

  const next = contract?.nextAction;
  const goCase = useCallback(async () => {
    if (!tenantId || !contract || !next || busy) return;
    setActionError(false);
    if (next.hasCase && next.caseId) {
      navigate(`/renewals/${encodeURIComponent(next.caseId)}`);
      return;
    }
    setBusy(true);
    const result = await apiClient.openRenewalCase(tenantId, { contractId: contract.contractId, branch: null });
    setBusy(false);
    if (result.ok && result.data) navigate(`/renewals/${encodeURIComponent(result.data.caseId)}`);
    else setActionError(true);
  }, [apiClient, tenantId, contract, next, busy, navigate]);

  const nextText = next ? tx(t, next.text) : "";
  const cta = next?.hasCase ? t("Open the case⟦contract⟧") : t("Start the renewal");
  useGuide(
    contract
      ? {
          where: t("{supplier} · contract", { supplier: contract.supplier }),
          now: t("Decide the points above, then: {next}", { next: nextText }),
          why: t("Decisive dates and negotiable terms first; everything else on request."),
          next: t("Decisions flow into the renewal plan."),
          cta: { label: cta, onClick: () => void goCase() },
        }
      : null,
  );

  if (!contract) {
    return (
      <div className="c4-page" data-screen-label="Contratto">
        <button type="button" className="btn btn-ghost c4-back" onClick={() => navigate("/contracts")}>
          ← {t("Contracts")}
        </button>
        {failed ? (
          <div className="c4-error" role="alert">
            <span>{t("The contract could not be loaded.")}</span>
            <button type="button" className="btn btn-secondary" onClick={() => void load()}>
              {t("Try again")}
            </button>
          </div>
        ) : null}
      </div>
    );
  }

  const d = contract.dates;
  const meta = t("{type} · version {version} · validated · {value} / year", {
    type: contract.type,
    version: contract.version,
    value: chfK(contract.annualValue),
  });
  // A term that rolled over by itself is shown on its effective dates, labelled as such; a passed
  // deadline never shows a negative count.
  const rolled = d.termStatus === "rolledOver";
  const renewedLabel = d.renewedOn ? t(d.renewedOnPassed ? "Renewed by itself on {date}" : "Renews by itself on {date}", { date: d.renewedOn }) : "";
  const a = contract.assessment;
  const dates = [
    {
      label: rolled ? t("Next notice by") : t("Notice by"),
      value: d.noticeDeadline,
      sub: d.noticeDays >= 0 ? t("last day · in {n} days", { n: d.noticeDays }) : t("passed"),
      cls: d.noticeDays >= 0 && d.noticeDays <= URGENT_NOTICE_DAYS ? "c4-urgent" : "",
    },
    {
      label: t("Contract end"),
      value: d.end,
      sub: rolled
        ? renewedLabel
        : d.autoRenew
          ? d.renewalMonths != null
            ? t("then auto-renewal {n} months", { n: d.renewalMonths })
            : t("then auto-renewal · term to confirm")
          : t("no auto-renewal"),
      cls: "",
    },
    {
      label: t("Uplift at renewal"),
      value: d.uplift,
      sub: t("if nothing is negotiated"),
      cls: d.uplift !== "0%" ? "c4-accent-text" : "",
    },
    { label: t("Start date"), value: d.start, sub: rolled ? t("current term, renewed by itself") : t("current term"), cls: "" },
  ];

  return (
    <div className="c4-page" data-screen-label="Contratto">
      <button type="button" className="btn btn-ghost c4-back" onClick={() => navigate("/contracts")}>
        ← {t("Contracts")}
      </button>
      <h6 className="c4-supplier">{contract.supplier}</h6>
      <h2>{contract.name}</h2>
      <div className="c4-meta">{meta}</div>

      <div className="c4-dates">
        {dates.map((date) => (
          <div key={date.label} className="c4-date">
            <div className="c4-label">{date.label}</div>
            <div className={date.cls ? `c4-date-value ${date.cls}` : "c4-date-value"}>{date.value}</div>
            <div className="c4-date-sub">{date.sub}</div>
          </div>
        ))}
      </div>

      {a ? (
        <div className={`c4-situation is-${a.urgency}`} data-screen-label="Situazione">
          <div className="c4-label">{t("Situation")}</div>
          <div className="c4-situation-head">{lang === "it" ? a.headline.it : a.headline.en}</div>
          <div className="c4-situation-why">{lang === "it" ? a.why.it : a.why.en}</div>
        </div>
      ) : null}

      <div className="c4-decisions">
        <div className="c4-decisions-head">
          {t("What requires a decision")}{" "}
          <span className="c4-decisions-sub">· {t("{n} points · standard terms stay below", { n: contract.decisionPoints.length })}</span>
        </div>
        {contract.decisionPoints.map((point) => (
          <DecisionCard key={point.key} point={point} onDecide={(choice) => void decide(point, choice)} />
        ))}
      </div>

      <div className="c4-next">
        <span className="c4-next-label">{t("Next action")}</span>
        <strong className="c4-next-text">{nextText}</strong>
        <button type="button" className="btn btn-primary c4-next-cta" onClick={() => void goCase()} disabled={busy}>
          {cta}
        </button>
        <button
          type="button"
          className="btn btn-ghost c4-next-ask"
          onClick={() => navigate(`/ask?scope=${encodeURIComponent(contractId)}&q=${encodeURIComponent(t("Summarize the contract {supplier}", { supplier: contract.supplier }))}`)}
        >
          {t("Ask Raffa")}
        </button>
      </div>
      <AskShortcuts
        scopeContractId={contractId}
        items={[
          { label: t("Renewal strategy"), question: t("How should we approach this renewal? Give me the strategy and the negotiation points.") },
          { label: t("Where can we save?"), question: t("Where can we save on this contract?") },
          { label: t("Negotiation email"), question: t("Draft the negotiation email to the supplier for this renewal.") },
        ]}
      />
      <NegotiationPoints apiClient={apiClient} tenantId={tenantId ?? ""} contractId={contractId} />
      {actionError ? (
        <div className="c4-error" role="alert">
          <span>{t("The renewal case could not be opened.")}</span>
          <button type="button" className="btn btn-secondary" onClick={() => void goCase()}>
            {t("Try again")}
          </button>
        </div>
      ) : null}

      <button type="button" className="btn btn-ghost c4-all-toggle" aria-expanded={allOpen} onClick={() => setAllOpen((o) => !o)}>
        {allOpen ? t("Hide the full contract ▴") : t("Full contract: terms, products, obligations, standard terms ▾")}
      </button>
      {allOpen ? (
        <div className="c4-all">
          <div>
            <div className="c4-section-label">{t("Key terms")}</div>
            <div className="c4-terms">
              {contract.terms.map((term) => (
                <div key={term.label} className="c4-term">
                  <div className="c4-label">{term.label}</div>
                  <div className="c4-term-value">{term.value}</div>
                </div>
              ))}
            </div>
          </div>
          <div>
            <div className="c4-section-label">{t("Products & prices · from the contract, no market data")}</div>
            {contract.products.map((p) => (
              <div key={p.name} className="c4-product">
                <span>
                  <strong>{p.name}</strong>
                  <span className="c4-product-unit">{p.unit}</span>
                </span>
                <span className="c4-num">{p.quantity > 1 ? enNumber(p.quantity) : "1"}</span>
                <span className="c4-num">CHF {enNumber(p.unitPrice)}</span>
                <span className="c4-num is-strong">CHF {enNumber(p.annual)}</span>
              </div>
            ))}
          </div>
          <div className="c4-obligations">
            <div>
              <div className="c4-section-label">{t("We must")}</div>
              {contract.oblYou.map((o) => (
                <div key={o.text} className="c4-obligation">
                  <div className={o.high ? "c4-obligation-text is-high" : "c4-obligation-text"}>{o.text}</div>
                  <div className="c4-obligation-when">{o.when}</div>
                </div>
              ))}
            </div>
            <div>
              <div className="c4-section-label">{t("{supplier} must", { supplier: contract.supplier })}</div>
              {contract.oblSup.map((o) => (
                <div key={o.text} className="c4-obligation">
                  <div className={o.high ? "c4-obligation-text is-high" : "c4-obligation-text"}>{o.text}</div>
                  <div className="c4-obligation-when">{o.when}</div>
                </div>
              ))}
            </div>
          </div>
          <div>
            <div className="c4-section-label">{t("Standard terms · already accepted")}</div>
            {contract.std.map((s) => (
              <div key={s.type} className="c4-std">
                <strong className="c4-std-type">{s.type}</strong>
                <span className="c4-std-text">{s.normalized}</span>
                <span className="c4-std-src">{s.src}</span>
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}

function DecisionCard({ point, onDecide }: { point: DecisionPointBody; onDecide: (choice: DecisionPointChoice) => void }) {
  const { t } = useI18n();
  const recorded = CHOICES.find((c) => c.value === point.decision);
  // Prototype: the "recorded" note is on the points to discuss; the policy note on liability.
  let source = tx(t, point.source);
  if (point.policyNote) source += ` · ${tx(t, point.policyNote)}`;
  if (point.tag === "discuss" && recorded && point.decidedBy) {
    source += ` · ${t("recorded: {decision} · {who}", { decision: t(recorded.recorded), who: point.decidedBy.split(" ")[0] ?? "" })}`;
  }
  return (
    <div className="c4-point" data-screen-label="Decisione">
      <div>
        <div className="c4-point-head">
          <span className={point.tag === "discuss" ? "tag tag-accent c4-tag" : "tag tag-neutral c4-tag"}>{tx(t, point.kind)}</span>
          <strong>{point.title}</strong>
        </div>
        <div className="c4-point-says">
          <span className="c4-label">{t("Says")}</span> {point.says}
        </div>
        <div className="c4-point-why">
          <span className="c4-label">{t("Why it matters")}</span> {tx(t, point.why)}
        </div>
        <div className="c4-point-proposal">
          <span className="c4-label">{t("Raffa proposes")}</span> {point.proposal}
        </div>
        <div className="c4-point-src">{source}</div>
      </div>
      <div className="c4-point-opts" role="group" aria-label={point.title}>
        {CHOICES.map((choice) => {
          const on = point.decision === choice.value;
          return (
            <button
              key={choice.value}
              type="button"
              className={on ? "c4-opt is-on" : "c4-opt"}
              aria-pressed={on}
              onClick={() => onDecide(choice.value)}
            >
              {t(choice.label)}
            </button>
          );
        })}
      </div>
    </div>
  );
}
