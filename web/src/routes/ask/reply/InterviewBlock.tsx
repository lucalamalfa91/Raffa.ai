import type { InterviewOption, InterviewReply } from "./replyTypes";

export interface InterviewBlockProps {
  reply: InterviewReply;
  /** Fires with the question's key and the option the user picked; the caller posts the option's
   * label as the transcript line and the keys as the answer (`buildInterviewAnswerRequest`). */
  onOption: (questionKey: string, option: InterviewOption) => void;
}

/**
 * ADR-030: the interview's own body -- one `<fieldset>` per question, its prompt as the legend and
 * one chip per option (a real `<button>`, keyboard-reachable, `title` carries the option's hint).
 * Once the interview is answered (`reply.answered`, set locally the moment an option is clicked
 * and by the server on resume) the whole fieldset is disabled, so a transcript never offers a
 * second chance at a question already taken. A `consent` question is rendered by the alert
 * dialog the route mounts, not here; the chips below are its keyboard-visible fallback.
 */
export default function InterviewBlock({ reply, onOption }: InterviewBlockProps) {
  return (
    <div className="reply-interview" data-answered={reply.answered ? "true" : "false"}>
      {reply.questions.map((question) => (
        <fieldset
          key={question.key}
          className="reply-interview-question"
          data-presentation={question.presentation}
          disabled={reply.answered}
        >
          <legend className="reply-interview-prompt">{question.prompt}</legend>
          <div className="reply-interview-options">
            {question.options.map((option) => (
              <button
                key={option.key}
                type="button"
                className="reply-interview-option"
                title={option.hint ?? undefined}
                disabled={reply.answered}
                onClick={() => onOption(question.key, option)}
              >
                {option.label} →
              </button>
            ))}
          </div>
          {question.allowFreeText && !reply.answered && (
            <p className="reply-interview-hint micro-meta">Or just type your answer below.</p>
          )}
        </fieldset>
      ))}
    </div>
  );
}
