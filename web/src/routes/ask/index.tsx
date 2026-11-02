import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
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
import { useGuide, useGuideState } from "../../components/shell/guide";
import { useShellContext } from "../../components/shell/shellContext";
import { useFixtureSession } from "../../fixtures/FixtureSessionContext";
import { useI18n } from "../../i18n/I18nProvider";
import { conversationDay, isSameDay, todayIso } from "./askDates";
import "./ask4.css";
import "./chat.css";
import ReplyBody from "./ReplyBody";
import AgentExtras from "./AgentExtras";
import FeedbackCard from "./FeedbackCard";
import DraftPanel, { ArtifactCard } from "./DraftPanel";
import { splitEmailDraft, type EmailDraft } from "./draft";
import ArtifactViewPanel, { ContentCard } from "./ArtifactView";
import { artifactRequest, buildArtifact, type Artifact, type ArtifactKind } from "./artifacts";

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
/** What a conversation is called: the name its owner gave it, else the automatic title. */
function convName(c: { title: string; customTitle: string | null }): string {
  return c.customTitle?.trim() ? c.customTitle : c.title;
}

function parityRun(): boolean {
  try {
    return window.sessionStorage.getItem("raffa4.parity") === "1";
  } catch {
    return false;
  }
}

export default function AskRoute({ apiClient }: AskRouteProps) {
  const api = useMemo(() => askOfferApi(apiClient), [apiClient]);
  const tenantId = useShellContext()?.workspaceId ?? "";
  const fixture = useFixtureSession();
  const today = todayIso(fixture?.now);
  const { t, lang } = useI18n();
  const navigate = useNavigate();
  const { conversationId } = useParams();

  const [railW, setRailW] = useState<number>(() => {
    try { return Math.min(520, Math.max(200, Number(localStorage.getItem("raffa.railW")) || 260)); } catch { return 260; }
  });
  const [dragging, setDragging] = useState(false);
  const startResize = (e: React.PointerEvent<HTMLDivElement>) => {
    e.preventDefault();
    const x0 = e.clientX, w0 = railW;
    let w = w0;
    setDragging(true);
    const move = (ev: PointerEvent) => { w = Math.min(520, Math.max(200, w0 + ev.clientX - x0)); setRailW(w); };
    const up = () => {
      setDragging(false);
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      try { localStorage.setItem("raffa.railW", String(w)); } catch { /* ignore */ }
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  };
  const [recent, setRecent] = useState<ConversationSummary4[]>([]);
  const [archived, setArchived] = useState<ConversationSummary4[]>([]);
  const [cases, setCases] = useState<OpenCase4[]>([]);
  const [contracts, setContracts] = useState<AskContract[]>([]);
  // A question that starts from a contract (`/ask?scope=<contractId>&q=…`) is about that contract: Raffa
  // never asks which one. The scope sticks to the new conversation until the user starts another.
  const [searchParams] = useSearchParams();
  const scopeParam = searchParams.get("scope");
  const [scopeId, setScopeId] = useState<string | null>(scopeParam && scopeParam.trim() ? scopeParam.trim() : null);
  const [quotes, setQuotes] = useState<QuoteListItem4[]>([]);
  const [current, setCurrent] = useState<ConversationDetail4 | null>(null);
  const [pick, setPick] = useState<Pick | null>(null);
  const [q, setQ] = useState("");
  const [showArchive, setShowArchive] = useState(false);
  const [busy, setBusy] = useState(false);
  // Public web research for the next question: offered only when the workspace opted in.
  const [webAvailable, setWebAvailable] = useState(false);
  const [webOn, setWebOn] = useState(false);
  useEffect(() => {
    if (!tenantId) return;
    let alive = true;
    void apiClient.getWorkspaceSettings(tenantId).then((r) => {
      if (alive) setWebAvailable(!!r.settings?.webResearchEnabled && r.settings?.webResearchAvailable !== false);
    });
    return () => {
      alive = false;
    };
  }, [apiClient, tenantId]);
  /** The question being answered: shown at once, with the thinking row under it, until Raffa's reply arrives. */
  const [pendingQuestion, setPendingQuestion] = useState<string | null>(null);
  const thinkingRef = useRef<HTMLDivElement>(null);
  /** The email draft open in the right-hand panel (the artifact beside the conversation). */
  const [panel, setPanel] = useState<{ id: string; draft?: EmailDraft; artifact?: Artifact } | null>(null);
  /** Dashboards, KPIs and documents Raffa built in this conversation (kept on the page; the panel shows one). */
  const [contents, setContents] = useState<Artifact[]>([]);
  const [building, setBuilding] = useState(false);
  const [uploadingOffer, setUploadingOffer] = useState(false);
  const offerInput = useRef<HTMLInputElement>(null);
  const [renaming, setRenaming] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [convError, setConvError] = useState<string | null>(null);

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

  // The side panel needs the room: Guida Raffa steps aside while it is open and comes back when it closes.
  const guide = useGuideState();
  const guideYielded = useRef(false);
  useEffect(() => {
    if (panel && guide.open) {
      guideYielded.current = true;
      guide.toggle();
    } else if (!panel && guideYielded.current) {
      guideYielded.current = false;
      if (!guide.open) guide.toggle();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [panel]);

  // Switching to another conversation closes the panel.
  useEffect(() => {
    setPanel((p) => (p && (p.artifact || current?.messages.some((m) => m.id === p.id)) ? p : null));
  }, [current]);

  // Keep the question and the thinking row in view while Raffa works.
  useEffect(() => {
    if (pendingQuestion !== null) thinkingRef.current?.scrollIntoView?.({ block: "end" });
  }, [pendingQuestion]);

  /** Builds a dashboard, KPIs or a document from the validated contracts and opens it in the side panel. */
  const createContent = async (kind: ArtifactKind) => {
    if (!tenantId || building) return;
    setBuilding(true);
    setConvError(null);
    const r = await apiClient.listContractsV4(tenantId);
    setBuilding(false);
    if (!r.ok || !r.data) {
      setConvError(t("The contracts could not be loaded."));
      return;
    }
    const made = buildArtifact(kind, r.data.rows, t, today);
    setContents((list) => [...list, made]);
    setPanel({ id: made.id, artifact: made });
  };

  const ask = async (
    question: string,
    contractId?: string,
    answerOnly = false,
    interviewAnswer?: { messageId: string; questionKey: string; optionKey?: string | null },
  ) => {
    const text = question.trim();
    if (!text || busy) return;
    // "Create a dashboard / KPIs / a brief": built from the contracts, shown in the panel.
    const requested = artifactRequest(text);
    if (requested) {
      setQ("");
      void createContent(requested);
      return;
    }
    setBusy(true);
    setPendingQuestion(text);
    setPick(null);
    setQ("");
    const r = await api.ask(tenantId, {
      conversationId: current?.id ?? null,
      question: text,
      contractId: contractId ?? current?.scopeContractId ?? scopeId ?? null,
      answerOnly,
      lang,
      interviewAnswer: interviewAnswer ?? null,
      webResearch: webOn,
    });
    setWebOn(false);
    setBusy(false);
    setPendingQuestion(null);
    if (!r.data) return;
    setQ("");
    setPick(r.data.needsContract ? { kind: "q", text } : null);
    show(r.data.conversation);
    // An email Raffa just drafted opens in the side panel by itself (the user can close it).
    const fresh = [...r.data.conversation.messages].reverse().find((m) => m.role === "raffa");
    const drafted = fresh ? (fresh.payload?.draft ?? splitEmailDraft(fresh.markdown).draft) : null;
    if (fresh && drafted) setPanel({ id: fresh.id, draft: drafted });
    void reloadLists();
    // The capability check may still be looking: its follow-up turn lands in the conversation shortly.
    if (r.data.capabilityCheck === "pending") {
      const id = r.data.conversation.id;
      const known = r.data.conversation.messages.length;
      for (let i = 0; i < 6; i++) {
        await new Promise((done) => window.setTimeout(done, 2500));
        const d = await api.getConversation(tenantId, id);
        if (d.data && d.data.messages.length > known) {
          show(d.data);
          break;
        }
      }
    }
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

  /** Name the open conversation (blank clears it, the automatic title shows again) -- the retired rail's "Rename". */
  const saveRename = async () => {
    if (!current || renaming === null) return;
    const r = await apiClient.renameConversation(tenantId, current.id, renaming.trim() === "" ? null : renaming.trim());
    if (r.ok) {
      // The title shown is the server's (a custom name, else the automatic one): read it back.
      const d = await api.getConversation(tenantId, current.id);
      if (d.data) setCurrent(d.data);
      setRenaming(null);
      setConvError(null);
      void reloadLists();
    } else {
      setConvError(r.error ?? t("The conversation could not be renamed."));
    }
  };

  /** Delete the open conversation for good -- the retired rail's "Delete". */
  const deleteCurrent = async () => {
    if (!current) return;
    const r = await apiClient.deleteConversation(tenantId, current.id);
    if (r.ok) {
      setConfirmDelete(false);
      setConvError(null);
      setCurrent(null);
      navigate("/ask");
      void reloadLists();
    } else {
      setConvError(r.error ?? t("The conversation could not be deleted."));
    }
  };

  /** Add an offer to compare (PDF, Word, Excel or a scan) -- the retired Quote check's upload. */
  const uploadOffer = async (file: File) => {
    setUploadingOffer(true);
    setConvError(null);
    const r = await apiClient.uploadQuote(tenantId, file);
    setUploadingOffer(false);
    if (offerInput.current) offerInput.current.value = "";
    if (r.ok && r.quote) {
      setPick(null);
      navigate(`/quotes/${encodeURIComponent(r.quote.id)}`);
    } else {
      setConvError(r.error ?? t("The offer could not be uploaded."));
    }
  };

  const newConversation = () => {
    setPick(null);
    setQ("");
    setCurrent(null);
    setScopeId(null);
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
        <span className="k6-conv-title">{convName(c)}</span>
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

  const creations: Array<{ label: string; hint: string; kind: ArtifactKind }> = [
    { label: t("Build a dashboard"), hint: t("Value by supplier and notice deadlines, from your contracts."), kind: "dashboard" },
    { label: t("Show the KPIs"), hint: t("Contracts, yearly value and what falls due soon."), kind: "kpi" },
    { label: t("Write a brief"), hint: t("A one-page portfolio summary you can copy or download."), kind: "document" },
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
    const fromState = (location.state as { query?: unknown } | null)?.query;
    const query = typeof fromState === "string" && fromState.trim() ? fromState : searchParams.get("q");
    const scope = scopeParam && scopeParam.trim() ? scopeParam.trim() : null;
    if (scope && scope !== scopeId) setScopeId(scope);
    if (typeof query !== "string" || !query.trim() || launched.current === location.key || !tenantId) return;
    launched.current = location.key;
    void ask(query, scope ?? undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.key, tenantId]);

  const onKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") void ask(q);
  };

  return (
    <div data-screen-label="Chiedi" className={panel ? "k6-chiedi has-panel" : "k6-chiedi"} style={{ ["--k6-rail-w" as string]: `${railW}px` }}>
      <aside data-screen-label="Conversazioni" className="k6-rail">
        <div className={dragging ? "k6-rail-grip is-drag" : "k6-rail-grip"} onPointerDown={startResize} role="separator" aria-orientation="vertical" />
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
          {renaming !== null ? (
            <form
              className="k6-rename"
              data-parity-hide
              onSubmit={(e) => {
                e.preventDefault();
                void saveRename();
              }}
            >
              <input
                className="input k6-rename-input"
                value={renaming}
                maxLength={48}
                autoFocus
                aria-label={t("Conversation name")}
                onChange={(e) => setRenaming(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Escape") setRenaming(null);
                }}
              />
              <button type="submit" className="btn btn-secondary k6-pane-act">
                {t("Save")}
              </button>
              <button type="button" className="btn btn-ghost k6-pane-act" onClick={() => setRenaming(null)}>
                {t("Cancel")}
              </button>
            </form>
          ) : (
            <span className="k6-pane-title">{current ? convName(current) : t("New conversation")}</span>
          )}
          <span className="k6-pane-meta">
            {current ? when(current.updatedAt) + (current.archivedAt ? ` · ${t("archived — write to reopen it")}` : "") : ""}
          </span>
          {current ? (
            <button type="button" className="btn btn-ghost k6-pane-archive" onClick={() => void toggleArchived()}>
              {current.archivedAt ? t("Reopen") : t("Archive")}
            </button>
          ) : null}
          {current && renaming === null ? (
            confirmDelete ? (
              <span className="k6-confirm" data-parity-hide>
                <span>{t("Delete this conversation?")}</span>
                <button type="button" className="btn btn-secondary k6-pane-act" onClick={() => void deleteCurrent()}>
                  {t("Delete")}
                </button>
                <button type="button" className="btn btn-ghost k6-pane-act" onClick={() => setConfirmDelete(false)}>
                  {t("Cancel")}
                </button>
              </span>
            ) : (
              <span className="k6-pane-acts" data-parity-hide>
                <button type="button" className="btn btn-ghost k6-pane-act" onClick={() => setRenaming(convName(current))}>
                  {t("Rename")}
                </button>
                <button type="button" className="btn btn-ghost k6-pane-act" onClick={() => setConfirmDelete(true)}>
                  {t("Delete")}
                </button>
              </span>
            )
          ) : null}
          {convError ? (
            <span className="k6-conv-error" role="alert">
              {convError}
            </span>
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
                    <button key={n.kind} type="button" className="k6-intent" onClick={() => {
                      // Started from a contract: the task is about that contract, no need to pick one.
                      const about = contracts.find((c) => c.contractId === scopeId);
                      if (about && n.kind !== "quote") choose(n.kind, about);
                      else setPick({ kind: n.kind });
                    }}>
                      <div className="k6-intent-label">{n.label}</div>
                      <div className="k6-intent-hint">{n.hint}</div>
                    </button>
                  ))}
                  {creations.map((n) => (
                    <button key={n.kind} type="button" className="k6-intent" data-parity-hide disabled={building} onClick={() => void createContent(n.kind)}>
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
                  {pick.kind === "quote" ? (
                    <span data-parity-hide>
                      <input
                        ref={offerInput}
                        type="file"
                        accept=".pdf,.docx,.xlsx,.png,.jpg,.jpeg"
                        className="k6-offer-file"
                        aria-label={t("Upload an offer")}
                        disabled={uploadingOffer}
                        onChange={(e) => {
                          const f = e.target.files?.[0];
                          if (f) void uploadOffer(f);
                        }}
                      />
                      <button type="button" className="btn btn-primary k6-pick-opt" disabled={uploadingOffer} onClick={() => offerInput.current?.click()}>
                        {uploadingOffer ? t("Raffa is reading…") : t("Upload an offer")}
                      </button>
                    </span>
                  ) : null}
                  <button type="button" className="btn btn-ghost k6-pick-cancel" onClick={() => setPick(null)}>
                    {t("Cancel")}
                  </button>
                </div>
                {convError && !current ? (
                  <div className="k6-conv-error" role="alert">
                    {convError}
                  </div>
                ) : null}
              </div>
            ) : null}
            {(current?.messages ?? []).map((m, _i, all) => {
              const lastRaffaId = [...all].reverse().find((x) => x.role === "raffa")?.id;
              const raffa = m.role === "raffa";
              const cta = raffa ? ctaOf(m) : null;
              return (
                <div key={m.id} className={raffa ? "k6-qa is-raffa" : "k6-qa"}>
                  <span className="k6-qa-by">{raffa ? "Raffa.ai" : t("You⟦ask⟧")}</span>
                  <div className="k6-qa-body">
                    {raffa ? (
                      (() => {
                        const split = splitEmailDraft(m.markdown);
                        const text = split.text;
                        const draft = m.payload?.draft ?? split.draft;
                        // The parity run compares with the prototype's plain reply ("Source: …"): see PARITY.md D7.
                        if (parityRun()) {
                          const src = m.citations[0]?.title;
                          return (
                            <>
                              <div className="k6-qa-text">{m.markdown}</div>
                              {src ? <div className="k6-qa-src">{`${t("Source:")} ${src}`}</div> : null}
                            </>
                          );
                        }
                        return (
                          <>
                            <ReplyBody text={text} sources={m.citations} />
                            {draft ? (
                              <ArtifactCard draft={draft} active={panel?.id === m.id} onOpen={() => setPanel(panel?.id === m.id ? null : { id: m.id, draft })} />
                            ) : null}
                            {m.payload?.feedbackOffer &&
                            current &&
                            !all.some((x) => x.payload?.feedbackResult?.forMessageId === m.id) ? (
                              <span data-parity-hide>
                                <FeedbackCard
                                  offer={m.payload.feedbackOffer}
                                  onSubmit={async (answers) => {
                                    const res = await apiClient.postConversationFeedback(tenantId, current.id, { messageId: m.id, answers });
                                    if (res.ok) {
                                      const d = await api.getConversation(tenantId, current.id);
                                      if (d.data) show(d.data);
                                    }
                                    return { ok: res.ok };
                                  }}
                                />
                              </span>
                            ) : null}
                            <AgentExtras
                              message={m}
                              latest={m.id === lastRaffaId}
                              busy={busy}
                              onAsk={(q) => void ask(q)}
                              onInterview={(messageId, questionKey, option) => void ask(option.label, undefined, false, { messageId, questionKey, optionKey: option.key })}
                              onNavigate={(href) => (/^https?:/.test(href) ? window.open(href, "_blank", "noopener") : navigate(href))}
                            />
                          </>
                        );
                      })()
                    ) : (
                      <div className="k6-qa-text">{m.markdown}</div>
                    )}
                    {cta ? (
                      <button type="button" className="btn btn-secondary k6-qa-cta" onClick={cta.go}>
                        {cta.label}
                      </button>
                    ) : null}
                  </div>
                </div>
              );
            })}
            {contents.map((c) => (
              <div key={c.id} className="k6-qa is-raffa">
                <span className="k6-qa-by">Raffa.ai</span>
                <div className="k6-qa-body">
                  <ContentCard artifact={c} active={panel?.id === c.id} onOpen={() => setPanel(panel?.id === c.id ? null : { id: c.id, artifact: c })} />
                </div>
              </div>
            ))}
            {pendingQuestion !== null ? (
              <>
                <div className="k6-qa">
                  <span className="k6-qa-by">{t("You⟦ask⟧")}</span>
                  <div className="k6-qa-body">
                    <div className="k6-qa-text">{pendingQuestion}</div>
                  </div>
                </div>
                <div className="k6-qa is-raffa k6-thinking" role="status" aria-live="polite" ref={thinkingRef}>
                  <span className="k6-qa-by">Raffa.ai</span>
                  <div className="k6-thinking-copy">
                    <span className="k6-thinking-spinner" aria-hidden="true" />
                    {t("Reading your contracts and checking the evidence…")}
                  </div>
                </div>
              </>
            ) : null}
          </div>
        </div>
        {(() => {
          const aboutId = current?.scopeContractId ?? scopeId;
          const about = aboutId ? contracts.find((c) => c.contractId === aboutId) : undefined;
          return about ? (
            <div className="k6-scope" data-parity-hide>
              <span>{t("About {supplier}", { supplier: about.supplier })}</span>
              {!current ? (
                <button type="button" className="btn btn-ghost k6-scope-x" aria-label={t("Ask about all contracts")} onClick={() => setScopeId(null)}>
                  ×
                </button>
              ) : null}
            </div>
          ) : null;
        })()}
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
          <div className="k6-composer-foot">
            {t("Procurement only · cites its source or says it cannot")}
            {webAvailable ? (
              <label className="k6-web-toggle" data-parity-hide>
                <input type="checkbox" checked={webOn} onChange={(e) => setWebOn(e.target.checked)} />{" "}
                {t("Also search the public web for this question")}
              </label>
            ) : null}
          </div>
        </div>
      </div>
      {panel?.draft ? <DraftPanel draft={panel.draft} onClose={() => setPanel(null)} /> : null}
      {panel?.artifact ? <ArtifactViewPanel artifact={panel.artifact} onClose={() => setPanel(null)} /> : null}
    </div>
  );
}
