import { useEffect, useMemo, useState } from "react";
import { Navigate, useNavigate, useParams } from "react-router-dom";
import { askOfferApi, type ApiClient, type QuoteStatus4, type QuoteView4 } from "../../api/client";
import { useGuide } from "../../components/shell/guide";
import { useShellContext } from "../../components/shell/shellContext";
import { useI18n } from "../../i18n/I18nProvider";
import "./quote4v.css";

export interface QuoteCheckRouteProps {
  apiClient: ApiClient;
}

type Step = 1 | 2 | 3;

const STEPS: ReadonlyArray<{ n: Step; label: string }> = [
  { n: 1, label: "Verify⟦quote⟧" },
  { n: 2, label: "Compare⟦quote⟧" },
  { n: 3, label: "Decide⟦quote⟧" },
];
const KICKER = ["Step 1 · Verify", "Step 2 · Compare", "Step 3 · Decide"] as const;
const HEAD = ["Check what Raffa read", "Compared with your contract, your target and the policy", "Verdict"] as const;
const SUB = [
  "What the proposal does not say is marked as missing — I don't make it up.",
  "No market benchmark in this release.",
  "The decision is about this proposal, not the contract nor an email.",
] as const;

const VERDICT: Record<QuoteView4["assessment"]["verdict"], string> = {
  nocompare: "No price comparison available.",
  misses_target: "It misses your target.",
  meets_target: "It meets your target.",
  below_current: "Below your current price.",
  above_current: "Above your current price.",
  same: "Same as your current price.",
};

const STATUS: Record<QuoteStatus4, string> = {
  checked: "Awaiting decision",
  negotiating:
    "In negotiation — the counter-proposal follows the case email cycle (draft · to approve · approved · copied · send confirmed)",
  signing: "Towards signature — outside Raffa; then the signed contract enters the standard pipeline",
  signed: "Signed⟦quote⟧",
  declined: "Declined — the current contract is not closed by this",
};

const TERM_LABEL: Record<string, string> = {
  payment: "Payments",
  uplift: "Annual uplift",
  renewal: "Renewal⟦quote term⟧",
  liability: "Liability",
  sla: "SLA",
};

interface Choice {
  label: string;
  primary: boolean;
  status: QuoteStatus4;
  counterProposal?: boolean;
}

function choicesFor(view: QuoteView4): Choice[] {
  if (view.status !== "checked") return [{ label: "Reopen the decision", primary: false, status: "checked" }];
  const v = view.assessment.verdict;
  if (v === "nocompare") {
    return [
      { label: "Ask the supplier for the missing data", primary: true, status: "negotiating", counterProposal: true },
      { label: "Proceed towards signature", primary: false, status: "signing" },
      { label: "Decline and assess alternatives", primary: false, status: "declined" },
    ];
  }
  if (v === "misses_target" || v === "above_current") {
    return [
      { label: "Prepare the counter-proposal", primary: true, status: "negotiating", counterProposal: true },
      { label: "Sign anyway", primary: false, status: "signing" },
      { label: "Decline and assess alternatives", primary: false, status: "declined" },
    ];
  }
  return [
    { label: "Proceed to signature", primary: true, status: "signing" },
    { label: "Negotiate the open clauses first", primary: false, status: "negotiating" },
    { label: "Decline", primary: false, status: "declined" },
  ];
}

/**
 * Offerta (AskRaffa 4.0, prototype `data-screen-label="Offerta"`): one proposal in three steps —
 * Verifica (what Raffa read, what it omits) → Confronta (only with the customer's own contract,
 * target, other offers and policy; never a market benchmark) → Decidi (one sentence, three choices).
 */
export default function QuoteCheckRoute({ apiClient }: QuoteCheckRouteProps) {
  const { quoteId } = useParams();
  if (!quoteId) return <Navigate to="/ask" replace />;
  return <Offer apiClient={apiClient} quoteId={quoteId} />;
}

function Offer({ apiClient, quoteId }: { apiClient: ApiClient; quoteId: string }) {
  const api = useMemo(() => askOfferApi(apiClient), [apiClient]);
  const tenantId = useShellContext()?.workspaceId ?? "";
  const { t, lang } = useI18n();
  const navigate = useNavigate();
  const [view, setView] = useState<QuoteView4 | null>(null);
  const [missing, setMissing] = useState(false);
  const [step, setStep] = useState<Step | null>(null);

  useEffect(() => {
    let live = true;
    void api.getQuote(tenantId, quoteId, lang).then((r) => {
      if (!live) return;
      setView(r.data);
      setMissing(!r.ok && r.status === 404);
      if (r.data) setStep((s) => s ?? (r.data!.status !== "checked" ? 3 : 1));
    });
    return () => {
      live = false;
    };
  }, [api, tenantId, quoteId, lang]);

  const s = step ?? 1;
  useGuide(
    view
      ? {
          where: `${t("Quote⟦guide⟧")} · ${view.supplier}`,
          now: t(HEAD[s - 1]),
          why: t(SUB[s - 1]),
          next: t("After the decision, emails and signature follow the case."),
        }
      : null,
  );

  if (missing) return <Navigate to="/ask" replace />;
  if (!view) return <div data-screen-label="Offerta" className="q6-offer" />;

  const a = view.assessment;
  const confirm = async () => {
    const r = await api.confirmQuoteExtraction(tenantId, view.id, lang);
    if (r.data) setView(r.data);
    setStep(2);
  };

  const openCase = async () => {
    if (view.caseId) navigate(`/renewals/${encodeURIComponent(view.caseId)}`);
    else if (view.contractId) {
      const r = await api.openRenewalCase(tenantId, view.contractId);
      navigate(r.data?.caseId ? `/renewals/${encodeURIComponent(r.data.caseId)}` : "/renewals");
    }
  };

  const decide = async (choice: Choice) => {
    const r = await api.decideQuote(tenantId, view.id, choice.status, lang);
    if (r.data) setView(r.data);
    const linked = view.caseId !== null || view.contractId !== null;
    if (!linked) return;
    if (choice.status === "negotiating" && choice.counterProposal) await api.counterProposal(tenantId, view.id);
    if (choice.status === "negotiating" || choice.status === "declined") await openCase();
  };

  const stated = view.stated.map((x) => ({
    key: x.key,
    label: TERM_LABEL[x.key] ? t(TERM_LABEL[x.key]!) : x.key,
    value: x.value ?? t("not stated"),
    missing: x.value === null,
  }));

  const cells = [
    { k: t("This proposal"), v: a.totalLabel, sub: t("a year⟦quote⟧"), alert: false },
    {
      k: t("Your current contract"),
      v: a.curTotalLabel ?? "—",
      sub: a.curTotalLabel ? t("same lines") : t("no current price for these lines"),
      alert: false,
    },
    {
      k: t("Difference"),
      v: a.ownLabel ?? "—",
      sub: a.target ? t("target {label}", { label: a.target.label ?? a.target.kind }) : t("no target"),
      alert: (a.own ?? 0) > 0,
    },
  ];

  const verdictSub =
    a.basis === "none"
      ? t("Worth {total} a year. You can check lines, term, validity and missing clauses.", { total: a.totalLabel })
      : t("Worth {total} a year against {current} of your contract.", { total: a.totalLabel, current: a.curTotalLabel ?? "—" });

  return (
    <div data-screen-label="Offerta" className="q6-offer">
      <button type="button" className="btn btn-ghost q6-back" onClick={() => navigate("/ask")}>
        {t("← Ask")}
      </button>
      <h6 className="q6-meta">
        {`${t("Quote⟦guide⟧")} · `}
        {`${view.fileName} · ${view.term ?? ""} · ${t("valid until")} ${view.valid ?? "—"}`}
      </h6>
      <h2 className="q6-title">{view.supplier}</h2>
      <div className="q6-steps">
        {STEPS.map((st) => (
          <div key={st.n} className={st.n === s ? "q6-step is-current" : st.n < s ? "q6-step is-done" : "q6-step"}>
            {st.n} · {t(st.label)}
          </div>
        ))}
      </div>
      <div className="q6-card">
        <div>
          <div className="q6-kicker">{t(KICKER[s - 1])}</div>
          <div className="q6-head">{t(HEAD[s - 1])}</div>
          <div className="q6-sub">{t(SUB[s - 1])}</div>
        </div>
        {s === 1 ? (
          <>
            {view.lines.map((l, i) => (
              <div key={`l${i}`} className="q6-line">
                <strong>{l.name}</strong>
                <span>{l.qtyLabel}</span>
                <span>{l.priceLabel}</span>
                <span>
                  {`${l.annualLabel} / ${t("year⟦quote⟧")}`}
                </span>
              </div>
            ))}
            {stated.map((x) => (
              <div key={x.key} className="q6-stated">
                <span className="q6-stated-k">{x.label}</span>
                <span className={x.missing ? "q6-stated-v is-missing" : "q6-stated-v"}>{x.value}</span>
              </div>
            ))}
            <div>
              <button type="button" className="btn btn-primary q6-next" onClick={() => void confirm()}>
                {t("Confirm the extracted data")}
              </button>
            </div>
          </>
        ) : null}
        {s === 2 ? (
          <>
            <div className="q6-cells">
              {cells.map((c) => (
                <div key={c.k} className="q6-cell">
                  <div className="q6-cell-k">{c.k}</div>
                  <div className={c.alert ? "q6-cell-v is-alert" : "q6-cell-v"}>{c.v}</div>
                  <div className="q6-cell-sub">{c.sub}</div>
                </div>
              ))}
            </div>
            {a.points.slice(0, 3).map((p, i) => (
              <div key={`p${i}`} className="q6-point">
                <span className={p.tone === "alert" ? "q6-dot is-alert" : "q6-dot"} />
                <span className="q6-point-text">{p.text}</span>
                <span className="q6-point-src">{p.src}</span>
              </div>
            ))}
            {a.missing.length > 0 ? (
              <div className="q6-missing">
                {`${t("Missing for a full comparison:")} `}
                {a.missing.join(" · ")}
              </div>
            ) : null}
            <div>
              <button type="button" className="btn btn-primary q6-next" onClick={() => setStep(3)}>
                {t("Go to the decision")}
              </button>
            </div>
          </>
        ) : null}
        {s === 3 ? (
          <>
            <div className="q6-verdict">
              <strong>{t(VERDICT[a.verdict])}</strong> {verdictSub}
            </div>
            <div className="q6-choices">
              {choicesFor(view).map((c) => (
                <button
                  key={c.label}
                  type="button"
                  className={c.primary ? "btn btn-primary q6-choice" : "btn btn-secondary q6-choice"}
                  onClick={() => void decide(c)}
                >
                  {t(c.label)}
                </button>
              ))}
            </div>
            <div className="q6-status">{t(STATUS[view.status])}</div>
          </>
        ) : null}
      </div>
    </div>
  );
}
