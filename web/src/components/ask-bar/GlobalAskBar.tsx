import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import type { ApiClient, CapabilityBody } from "../../api/client";
import type { WorkspaceRole } from "../shell/navItems";
import { isAskRoute } from "../shell/isAskRoute";
import { getAskBarCopy } from "./askSuggestions";
import "./ask-bar.css";

export interface GlobalAskBarProps {
  /** `useValidatedContractCount`'s `kbReady`, fetched once by `AppShell.tsx` and passed down --
   * this component never calls that API itself (ADR-024 V2 amendment; task E13/F09/US01/T01). */
  kbReady: boolean;
  /**
   * Task E25/F01/US01/T01 (AC-3): the server-derived role, threaded straight through from
   * `AppShell.tsx` (which already receives it from `App.tsx`) -- never re-derived here. Used only
   * to decide which catalog-sourced suggestion chips this bar shows (`askSuggestions.ts#
   * getAskBarCopy`); `GET /api/capabilities` itself is un-gated and identical for both roles
   * (AC-2, ADR-022 S16-11) -- this is presentation only, never a security control.
   */
  role: WorkspaceRole;
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
 * AC-3 / `raffa-v2/markup.html` "Ask bar — one pattern on every screen": two rows (square +
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
export default function GlobalAskBar({ kbReady, role, apiClient }: GlobalAskBarProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const [value, setValue] = useState("");
  const [capabilities, setCapabilities] = useState<readonly CapabilityBody[] | null>(null);
  const copy = getAskBarCopy(location.pathname, kbReady, capabilities, role);
  const hiddenOnAskScreen = isAskRoute(location.pathname);

  useEffect(() => {
    if (hiddenOnAskScreen) return;
    void apiClient.getCapabilities().then((result) => {
      setCapabilities(result.ok && result.catalog ? result.catalog.capabilities : null);
    });
  }, [apiClient, hiddenOnAskScreen]);

  useEffect(() => {
    if (hiddenOnAskScreen) return;
    function handleGlobalShortcut(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        inputRef.current?.focus();
      }
    }
    window.addEventListener("keydown", handleGlobalShortcut);
    return () => window.removeEventListener("keydown", handleGlobalShortcut);
  }, [hiddenOnAskScreen]);

  if (hiddenOnAskScreen) {
    return null;
  }

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
          aria-label="Ask Raffa"
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
