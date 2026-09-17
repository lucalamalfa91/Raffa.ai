import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import type { ApiClient, CapabilityBody, DocumentListPageBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { useValidatedContractCount } from "../../components/shell/useValidatedContractCount";
import { usePollBudget } from "../../components/shell/usePollBudget";
import ReplyBody from "./reply/ReplyBody";
import type { ReplyCitation } from "./reply/replyTypes";
import AskOffState from "./AskOffState";
import MarketRecordPanel from "./MarketRecordPanel";
import { useConversation } from "./useConversation";
import {
  ASK_HELLO,
  ASK_INPUT_PLACEHOLDER,
  NEW_CHAT_TRAILER,
  THINKING_COPY,
  TRANSPORT_ERROR_REASON,
  buildRaffaTurnFromReply,
  buildErrorTurn,
  buildOffCopy,
  buildScopedBrief,
  buildScopeLine,
  buildYouTurn,
  createConversationAndAsk,
  deriveConversationTitle,
  nextTurnId,
  parseScopeContractId,
  resolveAskOffReason,
  resolveCitationOpenAction,
  suggestionsFor,
  type AskTurnView,
} from "./askViewModel";
import "./ask.css";

export interface AskRouteProps {
  apiClient: ApiClient;
}

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
 * -- "from the shell hook" -- gates it); new chat / blank (no turns yet, `routeConversationId` and
 * `createdConversationId.current` both null); conversation (at least one turn exists, whether just
 * asked or resumed); resume (`routeConversationId` names a conversation this screen has not yet
 * loaded turns for -- `useConversation` fetches it). "New chat" and "conversation" are not two
 * components, only `turns.length === 0` vs `> 0` inside this one render -- the same "state, not a
 * route" shape the V1 screen already used for its own empty/answered/abstain faces.
 *
 * **One id, two sources.** `createdConversationId` (a ref, not state -- it must survive the render
 * that follows `ask()`'s own `setTurns` without waiting for React to flush) remembers a
 * conversation *this component itself* just created via `createConversationAndAsk`, so a second
 * message typed before the resulting `navigate(...)` call's own re-render lands still posts to the
 * right conversation, and so `useConversation` below is never asked to re-fetch the very thing this
 * screen just built turns for optimistically (which would flash a loading skeleton over content
 * already on screen for no reason). `routeConversationId` (the `:conversationId` route param) is
 * the other source -- resuming a link/rail click. `currentConversationId` is whichever is set;
 * `resumeTargetId` is the route id *only* when it is not the one this screen already created, which
 * is what actually tells `useConversation` whether to fetch at all.
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
 */
export default function AskRoute({ apiClient }: AskRouteProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const params = useParams<{ conversationId?: string }>();
  const workspace = loadCurrentWorkspace();

  const routeConversationId = params.conversationId ?? null;
  const createdConversationId = useRef<string | null>(null);
  const currentConversationId = routeConversationId ?? createdConversationId.current;
  const resumeTargetId = routeConversationId !== null && routeConversationId !== createdConversationId.current ? routeConversationId : null;

  const { state: resumeState } = useConversation(apiClient, workspace?.id, resumeTargetId);

  const [turns, setTurns] = useState<readonly AskTurnView[]>([]);
  const [title, setTitle] = useState<string | null>(null);
  const [question, setQuestion] = useState("");
  const [asking, setAsking] = useState(false);
  const [citationNotice, setCitationNotice] = useState<CitationNoticeState | null>(null);
  const [marketPanelRecordId, setMarketPanelRecordId] = useState<string | null>(null);
  const askedInitialQuery = useRef(false);

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

  // Task text point (2): "chips then name the supplier as c360Chips" -- app.jsx's own
  // supplier-templated chip pair needs a real supplier name; GET /api/contracts/{id} is the only
  // read that has one (a typed `supplierName` on the 360 header since task E13/F03/US01/T02 -- see
  // ../contracts/contract360/contract360ViewModel.ts#resolveSupplierLabel's own doc comment for the
  // full provenance -- read here rather than imported, per this task's own "independent,
  // separately-evolving screens duplicate a small read" convention). A null/blank name stays `null`
  // so `buildScopedSuggestions` falls back to its own "this supplier" wording.
  const [scopedSupplierName, setScopedSupplierName] = useState<string | null>(null);
  useEffect(() => {
    if (scopeContractId === undefined || !workspace) {
      setScopedSupplierName(null);
      return;
    }
    void apiClient.getContract360(workspace.id, scopeContractId).then((result) => {
      if (!result.ok || !result.contract) {
        setScopedSupplierName(null);
        return;
      }
      const { supplierName } = result.contract.header;
      setScopedSupplierName(supplierName !== null && supplierName.trim() !== "" ? supplierName : null);
    });
  }, [apiClient, workspace?.id, scopeContractId]);

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

  // Resuming (or navigating back to a fresh /ask) seeds/clears this screen's own turn list.
  useEffect(() => {
    if (resumeState.phase === "ready") {
      setTurns(resumeState.turns);
      setTitle(resumeState.conversation.title);
    }
  }, [resumeState]);

  useEffect(() => {
    if (routeConversationId === null) {
      createdConversationId.current = null;
      setTurns([]);
      setTitle(null);
    }
  }, [routeConversationId]);

  const ask = useCallback(
    (rawText: string) => {
      const text = rawText.trim();
      if (text === "" || !workspace) return;

      setTurns((previous) => [...previous, buildYouTurn(nextTurnId(), text)]);
      setTitle((previous) => previous ?? deriveConversationTitle(text));
      setQuestion("");
      setCitationNotice(null);
      setAsking(true);

      const openConversationId = routeConversationId ?? createdConversationId.current;

      if (openConversationId === null) {
        // AC-1/task text point (2): "a question creates a conversation ... then posts the message";
        // "the URL becomes /ask/<conversationId>".
        void createConversationAndAsk(apiClient, workspace.id, text, scopeContractId).then((result) => {
          setAsking(false);
          if (!result.ok) {
            setTurns((previous) => [...previous, buildErrorTurn(nextTurnId(), result.reason)]);
            return;
          }
          createdConversationId.current = result.conversationId;
          setTurns((previous) => [...previous, buildRaffaTurnFromReply(nextTurnId(), result.reply)]);
          navigate(`/ask/${result.conversationId}`, { replace: true });
        });
        return;
      }

      void apiClient.postMessage(workspace.id, openConversationId, { question: text }).then((result) => {
        setAsking(false);
        const turn =
          result.ok && result.reply
            ? buildRaffaTurnFromReply(nextTurnId(), result.reply)
            : buildErrorTurn(nextTurnId(), result.error ?? TRANSPORT_ERROR_REASON);
        setTurns((previous) => [...previous, turn]);
      });
    },
    // Depends on workspace?.id (a primitive), not workspace itself -- loadCurrentWorkspace() returns
    // a fresh object every call, the same convention ../contracts/contract360/index.tsx#load already
    // establishes for this app.
    [apiClient, workspace?.id, routeConversationId, scopeContractId, navigate],
  );

  // AC-1 / GlobalAskBar's own contract: a query typed into the global Ask bar arrives here as
  // router state and is asked automatically, exactly once, and only while this is genuinely a new
  // chat (never against a conversation already being resumed).
  useEffect(() => {
    if (askedInitialQuery.current) return;
    const seedQuery = (location.state as { query?: string } | null)?.query;
    if (typeof seedQuery === "string" && seedQuery.trim() !== "" && currentConversationId === null) {
      askedInitialQuery.current = true;
      ask(seedQuery);
    }
  }, [location.state, ask, currentConversationId]);

  /**
   * Task text point (3): a tenant citation navigates (with `state.from = "ask"`, AC-1 of the
   * sibling contract360-landing story); a market citation opens the side panel; a Raffa feature
   * card navigates to its own href. `askViewModel.ts#resolveCitationOpenAction`'s own doc comment
   * has the full decision table -- this is only the "then do it" half.
   */
  const openCitation = useCallback(
    (turn: Extract<AskTurnView, { role: "raffa" }>, citation: ReplyCitation) => {
      setCitationNotice(null);
      const action = resolveCitationOpenAction(citation, turn.wireCitations);

      if (action.kind === "navigate") {
        navigate(action.href, { state: { from: "ask" } });
        return;
      }
      if (action.kind === "market-panel") {
        setMarketPanelRecordId(action.recordId);
        return;
      }
      setCitationNotice({ turnId: turn.id, n: citation.n, text: "This citation can't be opened right now." });
    },
    [navigate],
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

  if (resumeState.phase === "loading") {
    return (
      <div className="ask-screen" role="status" aria-live="polite">
        <p className="micro-meta">Loading conversation…</p>
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="skeleton ask-resume-skeleton-row" />
        ))}
      </div>
    );
  }

  if (resumeState.phase === "not-found") {
    return (
      <div className="empty-state" role="status">
        <h3>Conversation not found</h3>
        <p className="micro-meta">This conversation does not exist, or is not yours.</p>
        <Link to="/ask" className="btn btn-secondary">
          + New chat
        </Link>
      </div>
    );
  }

  if (resumeState.phase === "error") {
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

  return (
    <div className="ask-screen">
      {hasTurns && (
        <div className="ask-screen-header">
          <div>
            <h2 className="screen-title">{title ?? "New chat"}</h2>
          </div>
          <Link to="/ask" className="btn btn-secondary">
            + New chat
          </Link>
        </div>
      )}

      <div className={`ask-screen-body${marketPanelRecordId !== null ? " has-panel" : ""}`}>
        <div className="ask-chat-column">
          <div className="ask-chat-log" role="log" aria-live="polite">
            {!hasTurns && (
              <div className="ask-new-chat">
                {scopeContractId !== undefined ? (
                  // NW-56/AC-1/AC-3: a scoped entry briefs the contract -- supplier kicker + a
                  // heading naming it -- instead of the generic hello, and a contract-specific
                  // one-line scope instead of the "N validated contracts" sentence below.
                  <>
                    <p className="screen-kicker">{scopedBrief.kicker}</p>
                    <h3 className="ask-new-chat-hello">{scopedBrief.heading}</h3>
                    <p className="micro-meta ask-new-chat-scope">{scopedBrief.scopeLine}</p>
                  </>
                ) : (
                  <>
                    <h3 className="ask-new-chat-hello">{ASK_HELLO}</h3>
                    <p className="micro-meta ask-new-chat-scope">
                      {buildScopeLine(validatedContractCount, [])}. {NEW_CHAT_TRAILER}
                    </p>
                  </>
                )}
                <div className="ask-new-chat-chips">
                  {suggestions.map((suggestion) => (
                    <button key={suggestion} type="button" className="ask-suggestion" aria-label={suggestion} onClick={() => ask(suggestion)}>
                      {suggestion} →
                    </button>
                  ))}
                </div>
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
                  <div className="ask-message-who">Raffa</div>
                  <div className="ask-message-content">
                    <ReplyBody reply={turn.reply} onOpenCitation={(citation) => openCitation(turn, citation)} onFollowUp={ask} />
                    {citationNotice !== null && citationNotice.turnId === turn.id && (
                      <p className="hint" role="status">
                        [{citationNotice.n}] {citationNotice.text}
                      </p>
                    )}
                  </div>
                </div>
              ),
            )}

            {asking && (
              <div className="ask-thinking" role="status" aria-live="polite">
                <div className="ask-thinking-who">Raffa</div>
                <div className="ask-thinking-copy">
                  <span className="ask-thinking-spinner" aria-hidden="true" />
                  {THINKING_COPY}
                </div>
              </div>
            )}
          </div>

          <div className="ask-input-row">
            <input
              ref={composerInputRef}
              className="input"
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
            <button type="button" className="btn btn-primary" disabled={asking || question.trim() === ""} onClick={() => ask(question)}>
              Ask
            </button>
          </div>
        </div>

        {marketPanelRecordId !== null && (
          <MarketRecordPanel apiClient={apiClient} recordId={marketPanelRecordId} onClose={() => setMarketPanelRecordId(null)} />
        )}
      </div>
    </div>
  );
}
