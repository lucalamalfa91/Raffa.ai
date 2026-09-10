import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import type { ApiClient, CapabilityBody } from "../../api/client";
import { getAskBarCopy } from "./askSuggestions";
import "./ask-bar.css";

export interface GlobalAskBarProps {
  /** `useValidatedContractCount`'s `kbReady`, fetched once by `AppShell.tsx` and passed down --
   * this component never calls that API itself (ADR-024 V2 amendment; task E13/F09/US01/T01). */
  kbReady: boolean;
  /**
   * Task E13/F09/US01/T04 (gap G-CAPABILITIES): this component fetches `GET /api/capabilities`
   * itself (once -- the catalog is static and tenant-agnostic, `getCapabilities`'s own OpenAPI
   * description) to source its own per-screen suggestion chips from the real catalog
   * (`askSuggestions.ts#getAskBarCopy`), falling back to the prototype's `chipsFor` pair while the
   * fetch is in flight or if it fails.
   */
  apiClient: ApiClient;
}

/**
 * AC-3 / `contigo-v2/markup.html` "Ask bar — one pattern on every screen": two rows (square +
 * heading-weight input, then quiet text chips with a trailing "→") on every app screen.
 *
 * 1. **Submit always opens a new chat.** `app.jsx`'s own global-bar handler is `go('ask')` then
 *    `ask(text,'global')`. `{ newChat: true }` in the navigation state carries that intent to
 *    `AskRoute`.
 * 2. **Off state matches the prototype.** Placeholder swaps to the off-copy, the input is
 *    `disabled={kbOff}`, the square greys, and the chips empty (`askChips: kbReady ? askChips : []`).
 *
 * Cmd/Ctrl+K focuses this input from anywhere in the app (design-system.md "⌘K opens Ask").
 */
export default function GlobalAskBar({ kbReady, apiClient }: GlobalAskBarProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const [value, setValue] = useState("");
  const [capabilities, setCapabilities] = useState<readonly CapabilityBody[] | null>(null);
  const copy = getAskBarCopy(location.pathname, kbReady, capabilities);

  useEffect(() => {
    void apiClient.getCapabilities().then((result) => {
      setCapabilities(result.ok && result.catalog ? result.catalog.capabilities : null);
    });
  }, [apiClient]);

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
      <div className="ask-bar-row">
        <span className={`ask-bar-mark${kbReady ? "" : " is-off"}`} aria-hidden="true" />
        <input
          ref={inputRef}
          className="input ask-bar-input"
          placeholder={copy.placeholder}
          aria-label="Ask Contigo"
          value={value}
          disabled={!kbReady}
          onChange={(event) => setValue(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              submit(value);
            }
          }}
        />
      </div>
      <div className="ask-bar-chips">
        {copy.suggestions.map((suggestion) => (
          <button
            key={suggestion}
            type="button"
            className="ask-bar-chip"
            aria-label={suggestion}
            onClick={() => submit(suggestion)}
          >
            {suggestion} →
          </button>
        ))}
      </div>
    </div>
  );
}
