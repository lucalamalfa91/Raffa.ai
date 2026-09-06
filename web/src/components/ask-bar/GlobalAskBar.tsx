import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { getAskBarCopy } from "./askSuggestions";
import "./ask-bar.css";

/**
 * AC-3 / design-system.md "Global Ask bar": on every app screen, a
 * surface-colored strip with a red square, a heading-weight input, and 2
 * contextual suggestion chips.
 *
 * Enter submits the typed query and navigates to /ask, mirroring the
 * prototype's own handler almost line for line (day1-demo.html:
 * `askGlobal:e=>{ if(e.key==='Enter'&&e.target.value.trim()){ const
 * t=e.target.value; e.target.value=''; ask(t); this.go('ask'); } }`) — clear
 * the input, then hand the query to the Ask screen via router state (the
 * future /ask screen, epic-07/feature-04-ask-contigo-ui, reads
 * `useLocation().state?.query`). Cmd/Ctrl+K focuses this input from
 * anywhere in the app (design-system.md "⌘K opens Ask").
 *
 * This bar only gets the user to /ask with their query typed in for them —
 * it does not answer it. The chat/citations/abstain screen itself is
 * out of this task's scope.
 */
export default function GlobalAskBar() {
  const location = useLocation();
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const [value, setValue] = useState("");
  const copy = getAskBarCopy(location.pathname);

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
    navigate("/ask", { state: { query: trimmed } });
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
