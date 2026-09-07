import { useCallback, useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import ChatMessage from "./ChatMessage";
import {
  ASK_SUGGESTIONS,
  EMPTY_STATE_COPY,
  THINKING_COPY,
  TRANSPORT_ERROR_REASON,
  buildContigoMessage,
  buildErrorMessage,
  buildYouMessage,
  nextMessageId,
  resolveCitationContractId,
  type ChatCitationView,
  type ChatMessageView,
} from "./askViewModel";
import "./ask.css";

export interface AskRouteProps {
  apiClient: ApiClient;
}

interface CitationNoticeState {
  messageId: string;
  n: number;
  text: string;
}

/**
 * Route `/ask` (ADR-018; screens.md #7 "Ask Contigo"; ADR-020 screen 7; task E07/F04/US01/T01,
 * us-01-ask-contigo). Wired into `../../components/shell/WorkspaceShellApp.tsx`'s `ask` route in
 * place of that shell task's `ScaffoldScreen` placeholder, the same seam `../contracts/index.tsx`
 * (PortfolioRoute) and `../contracts/contract360/index.tsx` already used.
 *
 * **Entry point (AC-1 "chat")**: `../../components/ask-bar/GlobalAskBar.tsx` already navigates here
 * with `{ state: { query } }` on Enter -- this screen's own header comment anticipated it verbatim
 * ("consumed by whichever future task builds the real Ask Contigo screen"). The `askedInitialQuery`
 * ref makes sure that seed query is only ever asked once per mount, even though `location.state`
 * does not change again after the navigation that carried it.
 *
 * **One call, one turn**: every question -- typed, a suggestion click, or the seeded router-state
 * query -- goes through the same `ask()` function, which calls the real `POST /api/chat/query`
 * (`apiClient.askContigo`) exactly once per turn and appends exactly one "You" bubble and one
 * "Contigo" bubble to the log. `askViewModel.ts#buildContigoMessage` is the one place that decides
 * whether that reply is an `answer` (AC-2 citations), an `abstain` (AC-3), or an `error` (a
 * transport/400 failure, never conflated with an honest AI abstention -- see that module's own
 * `ChatMessageKind` doc comment).
 *
 * **Citations open Contract 360 (AC-2)**: only resolved on click, not eagerly for every citation on
 * every answer (`askViewModel.ts#resolveCitationContractId`'s own doc comment has the full reason --
 * in short, only a `Document:<id>` citation can be resolved at all today). A citation that cannot be
 * opened shows an inline, honest reason next to that specific chip (`citationNotice`) rather than
 * silently doing nothing.
 */
export default function AskRoute({ apiClient }: AskRouteProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const workspace = loadCurrentWorkspace();

  const [messages, setMessages] = useState<ChatMessageView[]>([]);
  const [question, setQuestion] = useState("");
  const [asking, setAsking] = useState(false);
  const [citationBusy, setCitationBusy] = useState(false);
  const [citationNotice, setCitationNotice] = useState<CitationNoticeState | null>(null);
  const askedInitialQuery = useRef(false);

  const ask = useCallback(
    (rawText: string) => {
      const text = rawText.trim();
      if (text === "" || !workspace) return;

      setMessages((previous) => [...previous, buildYouMessage(nextMessageId(), text)]);
      setQuestion("");
      setCitationNotice(null);
      setAsking(true);

      void apiClient.askContigo(workspace.id, { question: text }).then((result) => {
        setAsking(false);
        const reply =
          result.ok && result.response !== null
            ? buildContigoMessage(nextMessageId(), result.response)
            : buildErrorMessage(nextMessageId(), result.error ?? TRANSPORT_ERROR_REASON);
        setMessages((previous) => [...previous, reply]);
      });
    },
    // Depends on workspace?.id (a primitive), not workspace itself: loadCurrentWorkspace() returns a
    // fresh object every call, the same convention ../contracts/contract360/index.tsx's own load()
    // already established for this app. workspace itself is still read from render scope inside the
    // callback body (guarded by the `!workspace` check above), just not listed as a dependency.
    [apiClient, workspace?.id],
  );

  // AC-1 / GlobalAskBar's own contract: a query typed into the global Ask bar arrives here as
  // router state and is asked automatically, exactly once.
  useEffect(() => {
    if (askedInitialQuery.current) return;
    const seedQuery = (location.state as { query?: string } | null)?.query;
    if (typeof seedQuery === "string" && seedQuery.trim() !== "") {
      askedInitialQuery.current = true;
      ask(seedQuery);
    }
  }, [location.state, ask]);

  const openCitation = useCallback(
    async (message: ChatMessageView, citation: ChatCitationView) => {
      if (!workspace) return;
      setCitationNotice(null);
      setCitationBusy(true);
      const result = await resolveCitationContractId(apiClient, workspace.id, citation);
      setCitationBusy(false);

      if (result.ok) {
        // AC-2 "opening Contract 360 > Clauses" -- ../contracts/contract360/index.tsx reads this
        // exact `state.tab` shape (contract360ViewModel.ts#isContract360TabName).
        navigate(`/contracts/${result.contractId}`, { state: { tab: "Clauses" } });
        return;
      }

      setCitationNotice({ messageId: message.id, n: citation.n, text: result.reason });
    },
    // Same workspace?.id-not-workspace convention as ask() above.
    [apiClient, workspace?.id, navigate],
  );

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before asking Contigo a question.</p>
      </div>
    );
  }

  return (
    <div className="ask-screen">
      <div className="ask-screen-header">
        <div>
          <p className="screen-kicker">R1 · Q&amp;A with evidence</p>
          <h2 className="screen-title">Ask Contigo</h2>
        </div>
      </div>

      <div className="ask-screen-body">
        <div className="ask-chat-column">
          <div className="ask-chat-log" role="log" aria-live="polite">
            {messages.length === 0 && (
              <div className="ask-chat-empty">
                <p className="micro-meta">{EMPTY_STATE_COPY}</p>
              </div>
            )}

            {messages.map((message) => (
              <ChatMessage
                key={message.id}
                message={message}
                citationBusy={citationBusy}
                onOpenCitation={(citation) => void openCitation(message, citation)}
                citationNotice={
                  citationNotice !== null && citationNotice.messageId === message.id
                    ? { n: citationNotice.n, text: citationNotice.text }
                    : null
                }
              />
            ))}

            {asking && (
              <div className="ask-thinking" role="status" aria-live="polite">
                <div className="ask-thinking-who">Contigo</div>
                <div className="ask-thinking-copy">
                  <span className="ask-thinking-spinner" aria-hidden="true" />
                  {THINKING_COPY}
                </div>
              </div>
            )}
          </div>

          <div className="ask-input-row">
            <input
              className="input"
              placeholder="Ask about spend, renewals, clauses, liability…"
              aria-label="Ask Contigo a question"
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

        <div className="ask-suggestions">
          <h6>Try</h6>
          {ASK_SUGGESTIONS.map((suggestion) => (
            <button key={suggestion} type="button" className="ask-suggestion" onClick={() => ask(suggestion)}>
              {suggestion}
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}
