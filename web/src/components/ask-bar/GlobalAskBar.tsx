import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import type { ApiClient, CapabilityBody } from "../../api/client";
import type { WorkspaceRole } from "../shell/navItems";
import { loadCurrentWorkspace } from "../../routes/signin/workspaceStore";
import { isAskRoute } from "../shell/isAskRoute";
import { contractIdForPath, getAskBarCopy } from "./askSuggestions";
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
   *
   * Task E27/F03/US01/T01 (NW-77): also calls `getContract360` itself (only while the current route
   * is Contract 360) to source the notice chip's real supplier name -- see `supplierName` below.
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
 * 3. **Scoped from Contract 360** (task E27/F03/US01/T01, NW-77; ADR-012 cl. 49 / ADR-020 §37.1 per
 *    `reports/architecture/waves/w19.md`; `ia-v2.md` "on Contract 360 the chips name the current
 *    supplier"). While the current route is `/contracts/:contractId` (never its `/review`
 *    sub-route -- `askSuggestions.ts#contractIdForPath`'s own doc comment), `submit` navigates
 *    `/ask?scope=<contractId>` instead of the plain `/ask` above, reusing `AskRoute`'s own w18
 *    `parseScopeContractId` (`askViewModel.ts`) to create the scoped conversation -- never a new
 *    nav-state field, the query still rides `state.query` unchanged. The notice chip's supplier
 *    name is the open contract's real one for the same reason -- see `supplierName` below and
 *    `askSuggestions.ts#c360Chips`.
 *
 * Cmd/Ctrl+K focuses this input from anywhere in the app (design-system.md "⌘K opens Ask").
 */
export default function GlobalAskBar({ kbReady, role, apiClient }: GlobalAskBarProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const [value, setValue] = useState("");
  const [capabilities, setCapabilities] = useState<readonly CapabilityBody[] | null>(null);

  // NW-77 AC-1: null on every screen but Contract 360 -- see askSuggestions.ts#contractIdForPath's
  // own doc comment. Both `submit`'s scoped navigate below and the supplier fetch just below read
  // this same value, so they can never disagree about which contract (if any) is open.
  const contractId = contractIdForPath(location.pathname);

  // NW-77 AC-2: the open contract's real supplier name for the c360 notice chip -- the same
  // `getContract360` read `routes/ask/askViewModel.ts#buildScopedSuggestions`'s own caller
  // (`routes/ask/index.tsx`) already does for the Ask screen's own scoped chips. `loadCurrentWorkspace`
  // (not a prop) matches that same file's own convention -- `GlobalAskBar` is rendered outside
  // `AppShell`'s `<Outlet/>` (`AppShell.tsx`), so it has no routed access to `:contractId` via
  // `useParams` and no `Outlet context` either; `location.pathname` above is what actually carries it
  // here. `cancelled` guards against an out-of-order response after a quick Contract-to-Contract
  // navigation overwriting the chip with the wrong supplier (GlobalAskBar stays mounted across every
  // route, unlike `AskRoute`'s own identical effect, which only re-fires per conversation).
  const [supplierName, setSupplierName] = useState<string | null>(null);
  useEffect(() => {
    if (contractId === null) {
      setSupplierName(null);
      return;
    }
    const workspace = loadCurrentWorkspace();
    if (!workspace) {
      setSupplierName(null);
      return;
    }
    let cancelled = false;
    void apiClient.getContract360(workspace.id, contractId).then((result) => {
      if (cancelled) return;
      if (!result.ok || !result.contract) {
        setSupplierName(null);
        return;
      }
      const { supplierName: name } = result.contract.header;
      setSupplierName(name !== null && name.trim() !== "" ? name : null);
    });
    return () => {
      cancelled = true;
    };
  }, [apiClient, contractId]);

  const copy = getAskBarCopy(location.pathname, kbReady, capabilities, role, supplierName);
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
    // NW-77 AC-1: `?scope=` (query string), never a new nav-state field (ADR-012 cl. 49) -- the same
    // bare-id template `Contract360Header.tsx`'s own "Ask about it" link already uses, reused by
    // `AskRoute`'s w18 `parseScopeContractId` (`askViewModel.ts`) to create the scoped conversation.
    const path = contractId !== null ? `/ask?scope=${contractId}` : "/ask";
    navigate(path, { state: { query: trimmed, newChat: true } });
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
