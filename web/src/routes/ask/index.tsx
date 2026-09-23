import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import type { ApiClient, CapabilityBody, DocumentListPageBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { useValidatedContractCount } from "../../components/shell/useValidatedContractCount";
import { usePollBudget } from "../../components/shell/usePollBudget";
import ReplyBody from "./reply/ReplyBody";
import ConsentDialog from "./reply/ConsentDialog";
import type { FeedbackAnswers } from "./reply/replyTypes";
import type { ReplyCitation } from "./reply/replyTypes";
import AskOffState from "./AskOffState";
import MarketRecordPanel from "./MarketRecordPanel";
import { useConversation } from "./useConversation";
import { useAskSessionStore, useAskSessionsSnapshot } from "./AskSessionsContext";
import { customTitleFor, draftSessionKey, resolveSessionKey } from "./askSessions";
import ConversationRenameField from "./ConversationRenameField";
import { requestReplyNotificationPermission } from "./AskReplyNotifier";
import { parseDocumentViewerHref } from "../documents/viewer/documentViewerViewModel";
import { useDocumentViewerOverlay } from "../documents/viewer/DocumentViewerOverlay";
import {
  ASK_HELLO,
  ASK_INPUT_PLACEHOLDER,
  ASK_NEW_CHAT_TITLE,
  ASK_RAFFA_KICKER,
  COMPOSER_NOTE,
  NEW_CHAT_INTRO,
  THINKING_COPY,
  feedbackSubmittedMessageIds,
  buildOffCopy,
  buildScopedBrief,
  buildScopeShort,
  buildStarterGroups,
  pendingConsent,
  fetchBoundContractChip,
  parseScopeContractId,
  resolveAskOffReason,
  resolveCitationOpenAction,
  suggestionsFor,
  type AskTurnView,
  type BoundContractChip,
} from "./askViewModel";
import { formatConversationTitle } from "./conversationTitle";
import { useValidatedSuppliers } from "./useValidatedSuppliers";
import { getContractTypeLabel } from "../contracts/portfolioTableFormatters";
import "./ask.css";

export interface AskRouteProps {
  apiClient: ApiClient;
}

const NO_TURNS: readonly AskTurnView[] = [];
const NO_FEEDBACK: ReadonlySet<string> = new Set();

interface CitationNoticeState {
  turnId: string;
  n: number;
  text: string;
}

/**
 * Route `/ask`, `/ask/:conversationId` (ADR-018/ADR-024; screens-v2.md #2 "Ask Raffa — home";
 * ADR-020 V2 amendment "screen 2"; task E13/F09/US01/T04, us-01-web-v2 AC-1/AC-3/AC-5/AC-6). V2
 * rebuild of the V1 screen `web/src/routes/ask/index.tsx` (task E07/F04/US01/T01) already occupied
 * -- replaces its single-turn `POST /api/chat/query` chat with real, resumable, per-user
 * conversations (`../../api/client.ts`'s `listConversations`/`createConversation`/`getConversation`/
 * `postMessage`, this same task's own addition) and the phase-2 rich reply renderer
 * (`./reply/ReplyBody.tsx`) in place of the old `ChatMessage.tsx` (deleted by this task).
 *
 * **State machine, one screen, four faces**: off (task text point (1), `useValidatedContractCount`
 * -- "from the shell hook" -- gates it); new chat / blank (no turns yet: a draft session, or none
 * at all); conversation (at least one turn exists, whether just asked or resumed); resume
 * (`routeConversationId` names a conversation this tab does not hold yet -- `useConversation`
 * fetches it). "New chat" and "conversation" are not two
 * components, only `turns.length === 0` vs `> 0` inside this one render -- the same "state, not a
 * route" shape the V1 screen already used for its own empty/answered/abstain faces.
 *
 * **Sessions, not component state.** The thread, the "Raffa is answering" flag, the title and the
 * bound contract of every conversation live in the shell's `askSessions.ts` store, keyed by
 * conversation id (or, for a new chat not created yet, by a draft key per `/ask` history entry).
 * This screen only shows one of them: a question keeps running after the user leaves for another
 * chat, a new chat or another screen, and its reply lands in the conversation that asked it --
 * never in whatever thread happens to be on screen -- which is what lets two chats run in parallel.
 * `AskReplyNotifier.tsx` and the rail report a reply that lands while its chat is not on screen.
 * `useConversation` is only asked to fetch a conversation this tab does not hold yet (a link, a
 * reload, a rail click on an older chat), so a chat this screen just built is never re-fetched over
 * content already on screen.
 *
 * Task E25/F06/US01/T01 (NW-60, wave w18): `AppShell.tsx` stops mounting `GlobalAskBar` on this
 * route (it duplicated this screen's own input), so this component now also owns the Cmd/Ctrl+K
 * shortcut for its composer -- see `composerInputRef`'s own comment below.
 *
 * Task E25/F03/US02/T01 (NW-56, wave w18): a scoped entry (`?scope=<contractId>`, from Contract
 * 360's "Ask about it") now briefs the contract in the new-chat block instead of rendering the
 * generic `ASK_HELLO` + scope line -- see `scopedBrief`/`askViewModel.ts#buildScopedBrief` below.
 * The off-state-first gate above is untouched (no scoped override of R-ASK-10, ux-ui-designer's own
 * w18 ruling for this gap): a scoped link into a tenant with zero validated contracts still lands on
 * the generic `AskOffState`, never a briefed-but-off face.
 *
 * Task E27/F04/US01/T01 (NW-78, wave w19; ADR-012 cl. 49 / ADR-020 37.2 per
 * `reports/architecture/waves/w19.md`; screens-v2.md #2 "scope line" as anchor): a persistent
 * `.tag-neutral` binding chip now renders above the thread for as long as this conversation is
 * scoped -- `boundContractId` (this conversation's own persisted `scopeContractId`, set from the
 * create response or the resumed conversation detail) and `boundContractChip` (that id's resolved
 * `askViewModel.ts#fetchBoundContractChip` result, `null` -- unrendered, AC-3 -- until it resolves).
 * Deliberately **not** the same state NW-56's brief above reads: that one is the transient,
 * pre-creation `scopeContractId` local (`?scope=`, gone the render after creation); this one is the
 * durable field the server persisted, so it is still there after a reload (AC-2 "survives resume").
 *
 * **Layout** (`Raffa.ai V2.dc.html` "ASK RAFFA.AI — conversation with rich answers"): a
 * conversation header (accent mark + `convTitle`, the `askScopeShort` line and "+ New chat"), the
 * scrolling thread (the new-chat block -- `askHello`, the intro and four labelled starter groups --
 * then one 72px-kicker grid per turn), and the screen's own composer pinned at the bottom (accent
 * mark + underlined input + "Ask", the two suggestion chips and "Procurement only · cites or
 * abstains"). Every figure is quoted in `ask.css`'s header comment.
 */
export default function AskRoute({ apiClient }: AskRouteProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const params = useParams<{ conversationId?: string }>();
  const workspace = loadCurrentWorkspace();
  const store = useAskSessionStore();
  const sessions = useAskSessionsSnapshot(store);

  const routeConversationId = params.conversationId ?? null;
  // `/ask/<id>` shows that conversation's session; a plain `/ask` shows this history entry's own
  // draft, so "+ New chat" (a new entry) is always blank even while another chat is still answering.
  const routeSessionKey = routeConversationId ?? draftSessionKey(location.key);
  const sessionKey = resolveSessionKey(sessions, routeSessionKey);
  const session = sessions.sessions.get(sessionKey) ?? null;
  const currentConversationId = session?.conversationId ?? routeConversationId;

  const resumeTargetId = routeConversationId !== null && session === null ? routeConversationId : null;
  const { state: resumeState } = useConversation(apiClient, workspace?.id, resumeTargetId);
  useEffect(() => {
    if (resumeState.phase !== "ready") return;
    // AC-2: the bound contract is rebuilt from the conversation detail wire, so the chip survives
    // resume -- a resumed URL carries no `?scope=` at all, only `/ask/<conversationId>`.
    store.hydrate(resumeState.conversation.id, {
      turns: resumeState.turns,
      title: resumeState.conversation.title,
      customTitle: resumeState.conversation.customTitle ?? null,
      boundContractId: resumeState.conversation.scopeContractId,
    });
  }, [store, resumeState]);

  // AC-1 "the URL becomes /ask/<conversationId>": the draft on screen was just created on the
  // server, so follow it. A draft the user already left is not followed -- it shows up in the rail.
  const promotedConversationId = routeConversationId === null && sessionKey !== routeSessionKey ? sessionKey : null;
  useEffect(() => {
    if (promotedConversationId !== null) navigate(`/ask/${promotedConversationId}`, { replace: true });
  }, [promotedConversationId, navigate]);

  // Which session is on screen decides whether a landing reply counts as unread.
  useEffect(() => {
    store.setViewing(sessionKey);
  }, [store, sessionKey]);
  useEffect(() => () => store.setViewing(null), [store]);

  const turns = session?.turns ?? NO_TURNS;
  const asking = session?.pending ?? false;
  // `convTitle`: the conversation's own title -- the server's on resume, the first question's
  // (`deriveConversationTitle`, the server's own rule) while this tab created it -- so the header
  // reads the same before and after a reload.
  const conversationTitle = session?.title ?? null;
  // NW-78 (wave w19): the durable source of the persistent binding chip -- this conversation's own
  // persisted `scopeContractId` (create response or resumed detail), **never** the transient
  // `scopeContractId` local further down, which is only "what to send" pre-creation.
  const boundContractId = session?.boundContractId ?? null;

  const [boundTitle, setBoundTitle] = useState<string | null>(null);
  const supplierNames = useValidatedSuppliers(apiClient, workspace?.id);
  // What the 360-header fetch resolved `boundContractId` to; `null` leaves the chip unrendered
  // (AC-3), whether nothing is bound yet or the fetch has not settled.
  const [boundContractChip, setBoundContractChip] = useState<BoundContractChip | null>(null);

  const [question, setQuestion] = useState(() => store.composerText(sessionKey));
  const [citationNotice, setCitationNotice] = useState<CitationNoticeState | null>(null);
  const [marketPanelRecordId, setMarketPanelRecordId] = useState<string | null>(null);
  const [renamingTitle, setRenamingTitle] = useState(false);
  const [renameFailed, setRenameFailed] = useState(false);

  // Switching chats swaps the composer's unsent text for that chat's own, and drops the notice and
  // market panel opened on the previous thread.
  const questionRef = useRef(question);
  questionRef.current = question;
  const shownSessionKey = useRef(sessionKey);
  useEffect(() => {
    if (shownSessionKey.current === sessionKey) return;
    store.saveComposerText(shownSessionKey.current, questionRef.current);
    shownSessionKey.current = sessionKey;
    setQuestion(store.composerText(sessionKey));
    setCitationNotice(null);
    setMarketPanelRecordId(null);
    setRenamingTitle(false);
    setRenameFailed(false);
  }, [store, sessionKey]);
  useEffect(() => () => store.saveComposerText(shownSessionKey.current, questionRef.current), [store]);

  // AC-1: "the validated-contract count is 0 (from the shell hook)" -- the same hook
  // AppShell.tsx/GlobalAskBar.tsx already share for the identical gate, never re-derived here.
  // `countsCheckedAt` re-keys it after every off-state counts tick below, so a gate that flips
  // while this screen is open flips here too (ADR-012 w15 §4 "Ask must re-read while it is off").
  const [countsCheckedAt, setCountsCheckedAt] = useState(0);
  const { count: validatedContractCount, kbReady } = useValidatedContractCount(apiClient, countsCheckedAt);

  // Off-state sub-copy: which of the three variants applies is read off the server's own `counts`
  // (ADR-027 §D7; askViewModel.ts#resolveAskOffReason), never inferred from `totalCount` -- task
  // E16/F03/US01/T01 (ADR-012 w15 §4). Scoped to only run while off: the on-state never needs this
  // fetch at all. While at least one document is in flight the one-row read repeats on the same
  // 2 s cadence Documents polls on, under the same five-minute no-change budget (ADR-020 w15 §8.3),
  // and it stops the moment the gate flips -- a user sitting on /ask while a document completes
  // sees Ask switch on without a reload.
  const [documentCounts, setDocumentCounts] = useState<DocumentListPageBody["counts"] | null>(null);
  const loadDocumentCounts = useCallback(() => {
    if (!workspace) return;
    void apiClient.listDocuments(workspace.id, { pageSize: 1 }).then((result) => {
      setDocumentCounts(result.ok && result.page ? result.page.counts : null);
      setCountsCheckedAt(Date.now());
    });
  }, [apiClient, workspace?.id]);
  useEffect(() => {
    if (kbReady) return;
    loadDocumentCounts();
  }, [kbReady, loadDocumentCounts]);
  const offReason = resolveAskOffReason(documentCounts);
  const countsFingerprint = useMemo(
    () =>
      documentCounts === null
        ? "none"
        : `${documentCounts.all}/${documentCounts.needsAttention}/${documentCounts.needsReview}/${documentCounts.processing}/${documentCounts.rejected}`,
    [documentCounts],
  );
  const { paused: offUpdatesPaused, resume: resumeOffUpdates } = usePollBudget({
    active: !kbReady && workspace !== null && offReason === "processing",
    fingerprint: countsFingerprint,
    onTick: loadDocumentCounts,
  });

  // AC-5 `/ask?scope=<contractId>`: a scoped new chat. Only consulted while there is no current
  // conversation yet -- once one exists (resumed or just created), the scope query string (if still
  // present from the original navigation) no longer applies to it.
  const searchParams = new URLSearchParams(location.search);
  const scopeContractId = currentConversationId === null ? parseScopeContractId(searchParams.get("scope")) : undefined;

  // Bound chat title is supplier + contract (never the question, never a guid). Scoped new chats
  // read it off `?scope=`, created and resumed conversations off their persisted scope; both
  // resolve it from GET /api/contracts/{id}.
  const boundScopeId = scopeContractId ?? boundContractId;

  const [scopedSupplierName, setScopedSupplierName] = useState<string | null>(null);
  useEffect(() => {
    setScopedSupplierName(null);
    setBoundTitle(null);
    if (boundScopeId === null || !workspace) return;
    // A reply for the previous chat's contract must never retitle the chat now on screen.
    let current = true;
    void apiClient.getContract360(workspace.id, boundScopeId).then((result) => {
      if (!current || !result.ok || !result.contract) return;
      const { supplierName, type } = result.contract.header;
      const name = supplierName !== null && supplierName.trim() !== "" ? supplierName : null;
      setScopedSupplierName(name);
      setBoundTitle(formatConversationTitle({ supplierName: name, contractLabel: getContractTypeLabel(type) }));
    });
    return () => {
      current = false;
    };
  }, [apiClient, workspace?.id, boundScopeId]);

  // NW-78: the persistent chip's own supplier+type fetch, off `boundContractId`, the durable id,
  // for as long as this conversation is scoped, turns or no turns, freshly created or resumed.
  useEffect(() => {
    setBoundContractChip(null);
    if (boundContractId === null || !workspace) return;
    let current = true;
    void fetchBoundContractChip(apiClient, workspace.id, boundContractId).then((chip) => {
      if (current) setBoundContractChip(chip);
    });
    return () => {
      current = false;
    };
  }, [apiClient, workspace?.id, boundContractId]);

  // Task text point (2): "two suggestion chips from GET /api/capabilities (suggestionsFor("ask"))".
  // Fetched once -- the catalog is static and tenant-agnostic (getCapabilities's own OpenAPI
  // description), the same "fetch once, never re-poll" shape useValidatedContractCount already
  // establishes for a different endpoint.
  const [capabilities, setCapabilities] = useState<readonly CapabilityBody[] | null>(null);
  useEffect(() => {
    void apiClient.getCapabilities().then((result) => {
      setCapabilities(result.ok && result.catalog ? result.catalog.capabilities : null);
    });
  }, [apiClient]);

  const suggestions = scopeContractId !== undefined ? suggestionsFor(capabilities, scopedSupplierName) : suggestionsFor(capabilities);

  // NW-56: the brief that replaces ASK_HELLO + the generic scope line in the new-chat block below,
  // read only while scopeContractId !== undefined. Cheap and pure, so (like `suggestions` above)
  // this is recomputed every render rather than memoized.
  const scopedBrief = buildScopedBrief(scopedSupplierName);

  // ADR-030: the interview a typed message answers, the create-then-ask sequence and the reply
  // itself are all the store's (`askSessions.ts#send`), so the reply lands in this session even if
  // the user has moved on. A session still answering refuses a second question.
  const ask = useCallback(
    (rawText: string, interviewAnswer?: { messageId: string; questionKey: string; optionKey: string | null }) => {
      const text = rawText.trim();
      if (text === "" || !workspace) return;

      const sent = store.send({ key: sessionKey, apiClient, tenantId: workspace.id, text, interviewAnswer, scopeContractId });
      if (sent === null) return;

      // Inside the click/Enter that sent the question -- the only moment a browser lets a page ask.
      requestReplyNotificationPermission();
      setQuestion("");
      setCitationNotice(null);
    },
    // Depends on workspace?.id (a primitive), not workspace itself -- loadCurrentWorkspace() returns
    // a fresh object every call, the same convention ../contracts/contract360/index.tsx#load already
    // establishes for this app.
    [store, sessionKey, apiClient, workspace?.id, scopeContractId],
  );

  /**
   * ADR-030 D5: the feedback card's one call. On success the server has stored the request,
   * opened the issue when configured, and appended the confirmation turn -- appended to this
   * session too so the live thread matches what a resume would show. A 409 (already answered) is
   * also `ok`, with no new turn.
   */
  const submitFeedback = useCallback(
    (messageId: string, answers: FeedbackAnswers): Promise<{ ok: boolean }> =>
      workspace ? store.submitFeedback({ key: sessionKey, apiClient, tenantId: workspace.id, messageId, answers }) : Promise.resolve({ ok: false }),
    [store, sessionKey, apiClient, workspace?.id],
  );

  // A chat that exists on the server can be renamed from its header (the rail does the same).
  const renameConversation = useCallback(
    (name: string) => {
      setRenamingTitle(false);
      setRenameFailed(false);
      if (!workspace || currentConversationId === null) return;
      void store.rename({ apiClient, tenantId: workspace.id, conversationId: currentConversationId, name }).then((result) => {
        if (!result.ok) setRenameFailed(true);
      });
    },
    [store, apiClient, workspace?.id, currentConversationId],
  );

  const feedbackDone = session?.feedbackDone ?? NO_FEEDBACK;
  const submittedFromThread = feedbackSubmittedMessageIds(turns);

  // AC-1 / GlobalAskBar's own contract: a query typed into the global Ask bar arrives here as
  // router state and is asked automatically, exactly once, and only while this is genuinely a new
  // chat (never against a conversation already being resumed). "Once" is per history entry: the
  // draft session exists from the first send on (and keeps resolving to the conversation it became).
  useEffect(() => {
    const seedQuery = (location.state as { query?: string } | null)?.query;
    if (routeConversationId !== null || typeof seedQuery !== "string" || seedQuery.trim() === "") return;
    const snapshot = store.getSnapshot();
    if (snapshot.sessions.has(resolveSessionKey(snapshot, routeSessionKey))) return;
    ask(seedQuery);
  }, [location.state, ask, routeConversationId, routeSessionKey, store]);

  /**
   * Task text point (3): a tenant citation navigates (with `state.from = "ask"`, AC-1 of the
   * sibling contract360-landing story); a market citation opens the side panel; a Raffa feature
   * card navigates to its own href. `askViewModel.ts#resolveCitationOpenAction`'s own doc comment
   * has the full decision table -- this is only the "then do it" half.
   */
  const overlay = useDocumentViewerOverlay();
  const openCitation = useCallback(
    (turn: Extract<AskTurnView, { role: "raffa" }>, citation: ReplyCitation) => {
      setCitationNotice(null);
      const action = resolveCitationOpenAction(citation, turn.wireCitations);

      if (action.kind === "navigate") {
        const viewer = parseDocumentViewerHref(action.href);
        if (viewer !== null && overlay !== null) {
          overlay.open(viewer);
          return;
        }
        navigate(action.href, { state: { from: "ask" } });
        return;
      }
      if (action.kind === "market-panel") {
        setMarketPanelRecordId(action.recordId);
        return;
      }
      if (action.kind === "external") {
        // ADR-030: a public web source leaves the app in a new tab, never through the router.
        window.open(action.url, "_blank", "noopener,noreferrer");
        return;
      }
      setCitationNotice({ turnId: turn.id, n: citation.n, text: "This citation can't be opened right now." });
    },
    [navigate, overlay],
  );

  // Task E25/F06/US01/T01 (NW-60; AC-2): `AppShell.tsx` no longer mounts `GlobalAskBar` on this
  // route, so this screen's own composer input takes over the Cmd/Ctrl+K shortcut GlobalAskBar.tsx
  // used to own here -- same self-contained pattern (own ref, own window listener, no context).
  // `composerInputRef.current` is only non-null while the composer is actually on screen (the final
  // render below, past the off/loading/not-found/error returns), so the shortcut is a safe no-op
  // otherwise -- nothing else on those other faces claims the input.
  const composerInputRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    function handleGlobalShortcut(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        composerInputRef.current?.focus();
      }
    }
    window.addEventListener("keydown", handleGlobalShortcut);
    return () => window.removeEventListener("keydown", handleGlobalShortcut);
  }, []);

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before asking Raffa.ai a question.</p>
      </div>
    );
  }

  // AC-1/R-ASK-10: off, before anything else -- no chat surface at all with zero validated
  // contracts.
  if (!kbReady) {
    return (
      <AskOffState
        copy={buildOffCopy(offReason)}
        updatesPaused={offReason === "processing" && offUpdatesPaused}
        onCheckAgain={resumeOffUpdates}
      />
    );
  }

  // Resume faces: only for a conversation this tab does not hold yet. Once `hydrate` has seeded the
  // store, the conversation renders from its session like any other.
  if (session === null && routeConversationId !== null && resumeState.phase !== "not-found" && resumeState.phase !== "error") {
    return (
      <div className="ask-screen" role="status" aria-live="polite">
        <p className="micro-meta">Loading conversation…</p>
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="skeleton ask-resume-skeleton-row" />
        ))}
      </div>
    );
  }

  if (session === null && resumeState.phase === "not-found") {
    return (
      <div className="empty-state" role="status">
        <h3>Conversation not found</h3>
        <p className="micro-meta">This conversation does not exist, or is not yours.</p>
        <Link to="/ask" className="btn btn-secondary">
          Ask Raffa
        </Link>
      </div>
    );
  }

  if (session === null && resumeState.phase === "error") {
    return (
      <div className="error-state" role="alert">
        <h4>Conversation unavailable</h4>
        <p className="micro-meta">
          {resumeState.message}
          {resumeState.statusCode !== null && ` (HTTP ${resumeState.statusCode})`}
        </p>
      </div>
    );
  }

  const hasTurns = turns.length > 0;
  // A name the user gave the chat wins over the bound supplier + contract and the first question.
  const conversationName = currentConversationId === null ? null : customTitleFor(sessions, currentConversationId, session?.customTitle);
  const headerTitle = conversationName ?? boundTitle ?? conversationTitle ?? ASK_NEW_CHAT_TITLE;
  const canRename = currentConversationId !== null;
  const scopeShort = buildScopeShort(validatedContractCount, supplierNames);
  const starterGroups = buildStarterGroups(supplierNames[0] ?? null);

  // A new history entry, so a new draft: this chat keeps running (and keeps its unsent text).
  const newChat = () => navigate("/ask", { state: { newChat: true } });

  return (
    <div className="ask-screen">
      <div className="ask-conv-header">
        <div className="ask-conv-title">
          <span className="ask-conv-mark" aria-hidden="true" />
          {renamingTitle && canRename ? (
            <ConversationRenameField
              className="ask-conv-rename"
              initialValue={headerTitle}
              label="Rename this chat"
              onCommit={renameConversation}
              onCancel={() => setRenamingTitle(false)}
            />
          ) : (
            <h2 className="ask-conv-name" onDoubleClick={canRename ? () => setRenamingTitle(true) : undefined}>
              {headerTitle}
            </h2>
          )}
          {canRename && !renamingTitle && (
            <button
              type="button"
              className="ask-conv-rename-button"
              onClick={() => {
                setRenameFailed(false);
                setRenamingTitle(true);
              }}
            >
              Rename
            </button>
          )}
          {renameFailed && (
            <span className="ask-conv-rename-error" role="alert">
              Could not rename this chat. Try again.
            </span>
          )}
          {/* NW-78 (AC-1/AC-2/AC-3): beside the title, independent of hasTurns -- a just-scoped
              conversation is bound from the instant its create response resolves (already true by
              then, see the optimistic "you" bubble in ask() above), not only once it has been
              resumed with a full history. */}
          {boundContractChip !== null && (
            <Link to={boundContractChip.href} className="ask-bound-chip">
              <span className="tag tag-neutral">{boundContractChip.label}</span>
            </Link>
          )}
        </div>
        <div className="ask-conv-meta">
          <span className="ask-conv-scope">{scopeShort}</span>
          <button type="button" className="btn btn-ghost ask-new-chat-button" onClick={newChat}>
            + New chat
          </button>
        </div>
      </div>

      <div className={`ask-screen-body${marketPanelRecordId !== null ? " has-panel" : ""}`}>
        <div className="ask-chat-column">
          <div className="ask-chat-log" role="log" aria-live="polite">
            <div className="ask-thread">
              {!hasTurns && (
                <div className="ask-new-chat">
                  {scopeContractId !== undefined ? (
                    // NW-56/AC-1/AC-3: a scoped entry briefs the contract -- supplier kicker + a
                    // heading naming it -- instead of the generic hello, and a contract-specific
                    // one-line scope instead of the intro paragraph.
                    <>
                      <p className="screen-kicker">{scopedBrief.kicker}</p>
                      <h3 className="ask-new-chat-hello">{scopedBrief.heading}</h3>
                      <p className="text-muted ask-new-chat-intro">{scopedBrief.scopeLine}</p>
                    </>
                  ) : (
                    <>
                      <h3 className="ask-new-chat-hello">{ASK_HELLO}</h3>
                      <p className="text-muted ask-new-chat-intro">{NEW_CHAT_INTRO}</p>
                      <div className="ask-starters">
                        {starterGroups.map((group) => (
                          <div key={group.label} className="ask-starter-group">
                            <div className="ask-starter-label">{group.label}</div>
                            <div className="ask-starter-items">
                              {group.items.map((starter) => (
                                <button key={starter} type="button" className="ask-starter" aria-label={starter} onClick={() => ask(starter)}>
                                  {starter} →
                                </button>
                              ))}
                            </div>
                          </div>
                        ))}
                      </div>
                    </>
                  )}
                </div>
              )}

              {turns.map((turn) =>
                turn.role === "you" ? (
                  <div key={turn.id} className="ask-message" data-role="you">
                    <div className="ask-message-who">You</div>
                    <div className="ask-message-content">
                      <div className="ask-message-text">{turn.text}</div>
                    </div>
                  </div>
                ) : (
                  <div key={turn.id} className="ask-message" data-role="raffa">
                    <div className="ask-message-who">{ASK_RAFFA_KICKER}</div>
                    <div className="ask-message-content">
                      <ReplyBody
                        reply={turn.reply}
                        onOpenCitation={(citation) => openCitation(turn, citation)}
                        onFollowUp={ask}
                        messageId={turn.messageId}
                        onSubmitFeedback={submitFeedback}
                        feedbackDone={turn.messageId !== null && (feedbackDone.has(turn.messageId) || submittedFromThread.has(turn.messageId))}
                        onInterviewOption={(reply, questionKey, option) => {
                          if (reply.messageId !== null) ask(option.label, { messageId: reply.messageId, questionKey, optionKey: option.key });
                        }}
                      />
                      {citationNotice !== null && citationNotice.turnId === turn.id && (
                        <p className="hint" role="status">
                          [{citationNotice.n}] {citationNotice.text}
                        </p>
                      )}
                    </div>
                  </div>
                ),
              )}

              {(() => {
                // ADR-030: the consent alert -- rendered only while the last Raffa turn is an
                // unanswered consent question; answering it (either way) posts the option by key.
                const consent = pendingConsent(turns);
                return consent !== null && !asking ? (
                  <ConsentDialog
                    reply={consent.reply}
                    question={consent.question}
                    onDecide={(reply, question, option) => {
                      if (reply.messageId !== null) ask(option.label, { messageId: reply.messageId, questionKey: question.key, optionKey: option.key });
                    }}
                  />
                ) : null;
              })()}

              {asking && (
                <div className="ask-thinking" role="status" aria-live="polite">
                  <div className="ask-thinking-who">{ASK_RAFFA_KICKER}</div>
                  <div className="ask-thinking-copy">
                    <span className="ask-thinking-spinner" aria-hidden="true" />
                    {THINKING_COPY}
                  </div>
                </div>
              )}
            </div>
          </div>

          <div className="ask-composer">
            <div className="ask-composer-row">
              <span className="ask-composer-mark" aria-hidden="true" />
              <input
                ref={composerInputRef}
                className="input ask-composer-input"
                placeholder={ASK_INPUT_PLACEHOLDER}
                aria-label="Ask Raffa a question"
                value={question}
                onChange={(event) => setQuestion(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Enter") {
                    ask(question);
                  }
                }}
              />
              <button type="button" className="btn btn-primary ask-composer-send" disabled={asking || question.trim() === ""} onClick={() => ask(question)}>
                Ask
              </button>
            </div>
            <div className="ask-composer-chips">
              {suggestions.map((suggestion) => (
                <button key={suggestion} type="button" className="ask-suggestion" aria-label={suggestion} disabled={asking} onClick={() => ask(suggestion)}>
                  {suggestion} →
                </button>
              ))}
              <span className="ask-composer-note">{COMPOSER_NOTE}</span>
            </div>
          </div>
        </div>

        {marketPanelRecordId !== null && (
          <MarketRecordPanel apiClient={apiClient} recordId={marketPanelRecordId} onClose={() => setMarketPanelRecordId(null)} />
        )}
      </div>
    </div>
  );
}
