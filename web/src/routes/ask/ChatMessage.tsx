import type { ChatCitationView, ChatMessageView } from "./askViewModel";

export interface CitationNoticeView {
  n: number;
  text: string;
}

export interface ChatMessageProps {
  message: ChatMessageView;
  onOpenCitation: (citation: ChatCitationView) => void;
  /** True while any citation on the screen is being resolved -- disables every chip so a second
   * click can't race the first (there is at most one in-flight resolution at a time; see
   * `index.tsx`). */
  citationBusy: boolean;
  /** Set only when the most recent citation click on *this* message could not open Contract 360 --
   * `index.tsx` clears it on the next question or the next citation click. */
  citationNotice: CitationNoticeView | null;
}

/**
 * One chat turn (route `/ask`, ADR-018; screens.md #7; ADR-020 screen 7; task E07/F04/US01/T01).
 * Markup mirrors the compiled prototype's own chat-row template
 * (`inputs/design/prototypes/day1-demo.html`, the `scr.ask` block's `sc-for list="{{ chat }}"`) --
 * a 72px role column + text column, translated from that block's inline styles into
 * `../../styles/components.css`'s shared classes (`.abstain-block`, `.micro-meta`, `.hint`,
 * `.error-state`) plus this screen's own `ask.css` composites, per ADR-019's "consume tokens, do
 * not fork them".
 */
export default function ChatMessage({ message, onOpenCitation, citationBusy, citationNotice }: ChatMessageProps) {
  return (
    <div className="ask-message" data-role={message.role} data-kind={message.kind}>
      <div className="ask-message-who">{message.role === "you" ? "You" : "Contigo"}</div>
      <div className="ask-message-content">
        {message.text !== "" && <div className="ask-message-text">{message.text}</div>}

        {message.kind === "abstain" && (
          <div className="abstain-block">
            <strong>Cannot determine reliably.</strong> {message.reason}
          </div>
        )}

        {message.kind === "error" && (
          <div className="error-state" role="alert">
            <p className="micro-meta">{message.reason}</p>
          </div>
        )}

        {message.citations.length > 0 && (
          <div className="ask-citations">
            {message.citations.map((citation) => (
              <button
                key={citation.n}
                type="button"
                className="ask-citation-chip"
                disabled={citationBusy}
                onClick={() => onOpenCitation(citation)}
              >
                <span className="ask-citation-index">[{citation.n}]</span> {citation.documentId}
                {citation.page !== null && ` · p.${citation.page}`}
                {citation.section !== null && citation.section !== "" && ` §${citation.section}`}
              </button>
            ))}
          </div>
        )}

        {citationNotice !== null && (
          <p className="hint" role="status">
            [{citationNotice.n}] {citationNotice.text}
          </p>
        )}

        {message.route !== null && <div className="ask-message-route micro-meta">{message.route}</div>}
      </div>
    </div>
  );
}
