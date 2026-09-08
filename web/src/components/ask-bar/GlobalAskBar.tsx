import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { getAskBarCopy } from "./askSuggestions";
import "./ask-bar.css";

export interface GlobalAskBarProps {
  /** `useValidatedContractCount`'s `kbReady`, fetched once by `AppShell.tsx` and passed down --
   * this component never calls the API itself (ADR-024 V2 amendment; task E13/F09/US01/T01). */
  kbReady: boolean;
}

/**
 * AC-3 / design-system.md "Global Ask bar": on every app screen, a surface-colored strip with a red
 * square, a heading-weight input, and 2 contextual suggestion chips. V2 keeps this markup and CSS
 * unchanged (e11) -- task E13/F09/US01/T01 (gap G-IA-V2) only changes two behaviours:
 *
 * 1. **Submit always opens a new chat.** `app.jsx`'s own global-bar handler is `go('ask')` then
 *    `ask(text,'global')` -- i.e. every submit here starts a fresh conversation, never appends to
 *    whatever the `/ask` screen happens to be showing. `{ newChat: true }` in the navigation state
 *    carries that intent to `AskRoute` (`routes/ask/**`, this task's own "do not touch" boundary) --
 *    inert until F09/T04 wires conversations and makes `AskRoute` reset on it; today `AskRoute` only
 *    reads `state.query` (unaffected by the extra key), so this is a safe, additive contract.
 * 2. **The placeholder switches off** (`app.jsx`: `askPlaceholder:kbReady?'...':'Ask Contigo switches
 *    on after your first validated contract'`) when there is no validated contract yet -- see
 *    `askSuggestions.ts#getAskBarCopy`. Suggestion chips and the per-route contextual copy this bar
 *    already had (task e06/e11) are unchanged; the prototype's own `disabled={kbOff}`/emptied-chips
 *    treatment is not reproduced here -- narrower than the prototype, but this task's own text names
 *    only the placeholder swap.
 *
 * Cmd/Ctrl+K focuses this input from anywhere in the app (design-system.md "⌘K opens Ask").
 */
export default function GlobalAskBar({ kbReady }: GlobalAskBarProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const [value, setValue] = useState("");
  const copy = getAskBarCopy(location.pathname, kbReady);

  useEffect(() => {
    function handleGlobalShortcut(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        inputRef.current?.focus();
      }
    }
    window.addEventListener("keydown", handleGlobalShortcut);
    return () => window.removeEventListener("keydown", handleGlobalShortcut);
  }, []);

  const submit = (query: string) => {
    const trimmed = query.trim();
    if (!trimmed) return;
    setValue("");
    navigate("/ask", { state: { query: trimmed, newChat: true } });
  };

  return (
    <div className="ask-bar" role="search">
      <span className="ask-bar-mark" aria-hidden="true" />
      <input
        ref={inputRef}
        className="input ask-bar-input"
        placeholder={copy.placeholder}
        aria-label="Ask Contigo"
        value={value}
        onChange={(event) => setValue(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            submit(value);
          }
        }}
      />
      <div className="ask-bar-chips">
        {copy.suggestions.map((suggestion) => (
          <button
            key={suggestion}
            type="button"
            className="ask-bar-chip"
            onClick={() => submit(suggestion)}
          >
            {suggestion}
          </button>
        ))}
      </div>
    </div>
  );
}
