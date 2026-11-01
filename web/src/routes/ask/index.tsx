import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useLocation, useNavigate, useParams } from "react-router-dom";
import {
  askOfferApi,
  type ApiClient,
  type AskContract,
  type ConversationDetail4,
  type ConversationMessage4,
  type ConversationSummary4,
  type OpenCase4,
  type QuoteListItem4,
} from "../../api/client";
import { useGuide } from "../../components/shell/guide";
import { useShellContext } from "../../components/shell/shellContext";
import { useFixtureSession } from "../../fixtures/FixtureSessionContext";
import { useI18n } from "../../i18n/I18nProvider";
import { conversationDay, isSameDay, todayIso } from "./askDates";
import "./ask4.css";

export interface AskRouteProps {
  apiClient: ApiClient;
}

type PickKind = "renew" | "summary" | "quote" | "q";
interface Pick {
  kind: PickKind;
  /** The question waiting for its contract (kind `q`). */
  text?: string;
}

const PICK_TITLE: Record<PickKind, string> = {
  renew: "Which contract do you want to renew?",
  quote: "Which offer?",
  q: "For which contract?",
  summary: "Which contract shall I summarize?",
};

/**
 * Chiedi (AskRaffa 4.0, prototype `data-screen-label="Chiedi"`): conversations on the left (open
 * cases · recent · archived ▾), one conversation on the right with the three intents when empty,
 * questions and sourced answers ("Fonte: …"), the contract picker only when the context is missing,
 * and the composer. Both columns share one 56px header line.
 */
export default function AskRoute({ apiClient }: AskRouteProps) {
  const api = useMemo(() => askOfferApi(apiClient), [apiClient]);
  const tenantId = useShellContext()?.workspaceId ?? "";
  const fixture = useFixtureSession();
  const today = todayIso(fixture?.now);
  const { t, lang } = useI18n();
  const navigate = useNavigate();
  const { conversationId } = useParams();

  const [recent, setRecent] = useState<ConversationSummary4[]>([]);
  const [archived, setArchived] = useState<ConversationSummary4[]>([]);
  const [cases, setCases] = useState<OpenCase4[]>([]);
  const [contracts, setContracts] = useState<AskContract[]>([]);
  const [quotes, setQuotes] = useState<QuoteListItem4[]>([]);
  const [current, setCurrent] = useState<ConversationDetail4 | null>(null);
  const [pick, setPick] = useState<Pick | null>(null);
  const [q, setQ] = useState("");
  const [showArchive, setShowArchive] = useState(false);
  const [busy, setBusy] = useState(false);

  const reloadLists = useCallback(async () => {
    const [r, a] = await Promise.all([api.listConversations(tenantId, "recent"), api.listConversations(tenantId, "archived")]);
    setRecent(r.data ?? []);
    setArchived(a.data ?? []);
  }, [api, tenantId]);

  useEffect(() => {
    void reloadLists();
    void api.listOpenCases(tenantId).then((r) => setCases(r.data ?? []));
    void api.listAskContracts(tenantId).then((r) => setContracts(r.data ?? []));
    void api.listQuotes(tenantId, "any").then((r) => setQuotes(r.data ?? []));
  }, [api, tenantId, reloadLists]);

  useEffect(() => {
    if (!conversationId) {
      setCurrent(null);
      return;
    }
    if (current?.id === conversationId) return;
    let live = true;
    void api.getConversation(tenantId, conversationId).then((r) => {
      if (live) setCurrent(r.data);
    });
    return () => {
      live = false;
    };
    // `current` is read only to skip a reload of the conversation just answered.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, tenantId, conversationId]);

  const show = (detail: ConversationDetail4) => {
    setCurrent(detail);
    if (detail.id !== conversationId) navigate(`/ask/${detail.id}`);
  };

  const ask = async (question: string, contractId?: string, answerOnly = false) => {
    const text = question.trim();
    if (!text || busy) return;
    setBusy(true);
    const r = await api.ask(tenantId, {
      conversationId: current?.id ?? null,
      question: text,
      contractId: contractId ?? null,
      answerOnly,
      lang,
    });
    setBusy(false);
    if (!r.data) return;
    setQ("");
    setPick(r.data.needsContract ? { kind: "q", text } : null);
    show(r.data.conversation);
    void reloadLists();
  };

  const openCase = async (contractId: string) => {
    const r = await api.openRenewalCase(tenantId, contractId);
    navigate(r.data?.caseId ? `/renewals/${encodeURIComponent(r.data.caseId)}` : "/renewals");
  };

  const choose = (kind: PickKind, contract: AskContract) => {
    setPick(null);
    if (kind === "renew") void openCase(contract.contractId);
    else if (kind === "q") void ask(pick?.text ?? "", contract.contractId, true);
    else void ask(t("Summarize the contract {supplier}", { supplier: contract.supplier }));
  };

  const toggleArchived = async () => {
    if (!current) return;
    const r = await api.setArchived(tenantId, current.id, current.archivedAt === null);
    if (r.data) setCurrent({ ...current, archivedAt: r.data.archivedAt });
    void reloadLists();
  };

  const newConversation = () => {
    setPick(null);
    setQ("");
    setCurrent(null);
    navigate("/ask");
  };

  const when = (iso: string) => (isSameDay(iso, today) ? t("Today") : conversationDay(iso, lang));
  const countLabel = (n: number) => `${n} ${t(n === 1 ? "question" : "questions")}`;

  const convRow = (c: ConversationSummary4) => {
    const on = current?.id === c.id;
    return (
      <button
        key={c.id}
        type="button"
        className={on ? "k6-conv is-current" : "k6-conv"}
        onClick={() => {
          setPick(null);
          navigate(`/ask/${c.id}`);
        }}
      >
        <span className="k6-conv-title">{c.title}</span>
        <span className="k6-conv-meta">
          {`${when(c.updatedAt)} · ${countLabel(c.questionCount)}${c.archivedAt ? ` · ${t("archived")}` : ""}`}
        </span>
      </button>
    );
  };

  const caseRow = (c: OpenCase4) => (
    <button key={c.caseId} type="button" className="k6-conv" onClick={() => navigate(`/renewals/${encodeURIComponent(c.caseId)}`)}>
      <span className="k6-conv-title">
        {`${t("Renewal")} · ${c.supplier} · ${c.contract.split(" — ")[0]}`}
      </span>
      <span className="k6-conv-meta">
        {`${t("case")} · ${t(c.phase)}`}
      </span>
    </button>
  );

  const groups = [
    { key: "cases", label: t("Open cases"), count: cases.length, rows: cases.map(caseRow), empty: t("No open case.") },
    { key: "recent", label: t("Recent"), count: recent.length, rows: recent.map(convRow), empty: t("No recent conversation.") },
    ...(showArchive
      ? [{ key: "archived", label: t("Archived"), count: archived.length, rows: archived.map(convRow), empty: t("No archived conversation.") }]
      : []),
  ];

  const intents: Array<{ label: string; hint: string; kind: PickKind }> = [
    { label: t("Prepare a renewal"), hint: t("Opens or resumes the case — never a second one."), kind: "renew" },
    { label: t("Summarize a contract"), hint: t("Terms, dates and what to watch, with the source."), kind: "summary" },
    { label: t("Compare an offer"), hint: t("Against your contract and your target. No benchmark."), kind: "quote" },
  ];

  const pickOptions =
    pick?.kind === "quote"
      ? quotes.map((o) => ({
          key: o.id,
          label: `${o.supplier} · ${o.reference ?? o.fileName}`,
          go: () => {
            setPick(null);
            navigate(`/quotes/${encodeURIComponent(o.id)}`);
          },
        }))
      : contracts.map((c) => ({ key: c.contractId, label: c.supplier, go: () => pick && choose(pick.kind, c) }));

  const ctaOf = (m: ConversationMessage4) => {
    const action = m.actions[0];
    if (action?.kind === "compareQuote") return { label: t("Compare an offer"), go: () => setPick({ kind: "quote" }) };
    if (action?.kind === "openContract" && action.contractId) {
      const contractId = action.contractId;
      return { label: t("Open the contract"), go: () => navigate(`/contracts/${encodeURIComponent(contractId)}`) };
    }
    return null;
  };

  useGuide({
    where: t("Ask Raffa"),
    now: current ? t("Continue the conversation or start a new one.") : t("Write a question or pick a task."),
    why: t(
      "I answer only from validated contracts, with the source — or I say I cannot. Conversations stay on the left: recent, cases and archived.",
    ),
    next: t("A task becomes a guided case; an archived conversation reopens when you write in it."),
  });

  // "Chiedi a Raffa" links elsewhere (askLaunch.ts) arrive with the question in router state:
  // asked once, as a new conversation.
  const location = useLocation();
  const launched = useRef<string | null>(null);
  useEffect(() => {
    const query = (location.state as { query?: unknown } | null)?.query;
    if (typeof query !== "string" || !query.trim() || launched.current === location.key || !tenantId) return;
    launched.current = location.key;
    void ask(query);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.key, tenantId]);

  const onKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") void ask(q);
  };

  return (
    <div data-screen-label="Chiedi" className="k6-chiedi">
      <aside data-screen-label="Conversazioni" className="k6-rail">
        <div className="k6-rail-head">
          <span className="k6-rail-title">{t("Conversations")}</span>
          <button type="button" className="btn btn-primary k6-new" onClick={newConversation}>
            {t("+ New")}
          </button>
        </div>
        {groups.map((g) => (
          <div key={g.key} className="k6-group">
            <div className="k6-group-label">
              {g.label} · {g.count}
            </div>
            {g.rows}
            {g.count === 0 ? <div className="k6-group-empty">{g.empty}</div> : null}
          </div>
        ))}
        <button type="button" className="btn btn-ghost k6-archive-toggle" onClick={() => setShowArchive(!showArchive)}>
          {showArchive ? t("Hide archived ▴") : t("Archived · {n} ▾", { n: archived.length })}
        </button>
      </aside>
      <div className="k6-pane">
        <div className="k6-pane-head">
          <span className="k6-mark" />
          <span className="k6-pane-title">{current ? current.title : t("New conversation")}</span>
          <span className="k6-pane-meta">
            {current ? when(current.updatedAt) + (current.archivedAt ? ` · ${t("archived — write to reopen it")}` : "") : ""}
          </span>
          {current ? (
            <button type="button" className="btn btn-ghost k6-pane-archive" onClick={() => void toggleArchived()}>
              {current.archivedAt ? t("Reopen") : t("Archive")}
            </button>
          ) : null}
        </div>
        <div className="k6-scroll">
          <div className="k6-thread">
            {current ? null : (
              <>
                <div>
                  <div className="k6-empty-title">{t("What do you want to know or do?")}</div>
                  <div className="k6-empty-sub">
                    {t(
                      "A question gets an answer with its source, from validated contracts. A task becomes a guided case, one step at a time. Every conversation stays on the left and can be reopened.",
                    )}
                  </div>
                </div>
                <div className="k6-intents">
                  {intents.map((n) => (
                    <button key={n.kind} type="button" className="k6-intent" onClick={() => setPick({ kind: n.kind })}>
                      <div className="k6-intent-label">{n.label}</div>
                      <div className="k6-intent-hint">{n.hint}</div>
                    </button>
                  ))}
                </div>
              </>
            )}
            {pick ? (
              <div className="k6-pick">
                <div className="k6-pick-title">{t(PICK_TITLE[pick.kind])}</div>
                <div className="k6-pick-opts">
                  {pickOptions.map((o) => (
                    <button key={o.key} type="button" className="btn btn-secondary k6-pick-opt" onClick={o.go}>
                      {o.label}
                    </button>
                  ))}
                  <button type="button" className="btn btn-ghost k6-pick-cancel" onClick={() => setPick(null)}>
                    {t("Cancel")}
                  </button>
                </div>
              </div>
            ) : null}
            {(current?.messages ?? []).map((m) => {
              const raffa = m.role === "raffa";
              const src = m.citations[0]?.title;
              const cta = raffa ? ctaOf(m) : null;
              return (
                <div key={m.id} className={raffa ? "k6-qa is-raffa" : "k6-qa"}>
                  <span className="k6-qa-by">{raffa ? "Raffa.ai" : t("You⟦ask⟧")}</span>
                  <div className="k6-qa-body">
                    <div className="k6-qa-text">{m.markdown}</div>
                    {raffa && src ? (
                      <div className="k6-qa-src">
                        {`${t("Source:")} `}
                        {src}
                      </div>
                    ) : null}
                    {cta ? (
                      <button type="button" className="btn btn-secondary k6-qa-cta" onClick={cta.go}>
                        {cta.label}
                      </button>
                    ) : null}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
        <div className="k6-composer-wrap">
          <div className="k6-composer">
            <input
              className="input k6-composer-input"
              placeholder={t("E.g. “When must we give notice to Salesforce?”")}
              value={q}
              onChange={(e) => setQ(e.target.value)}
              onKeyDown={onKey}
            />
            <button type="button" className="btn btn-primary k6-composer-send" onClick={() => void ask(q)}>
              {t("Ask⟦button⟧")}
            </button>
          </div>
          <div className="k6-composer-foot">{t("Procurement only · cites its source or says it cannot")}</div>
        </div>
      </div>
    </div>
  );
}
