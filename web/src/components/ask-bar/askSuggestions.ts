import type { CapabilityBody } from "../../api/client";

/**
 * Global Ask bar copy (ADR-019/design-system.md "Global Ask bar": "surface-
 * colored strip ... heading-weight input, 2 contextual suggestion chips").
 * The default placeholder text and the exactly-2-chips count are confirmed
 * against inputs/design/prototypes/day1-demo.html's compiled bundle
 * (`askPlaceholder:'Ask Contigo — spend, renewals, clauses, liability…'`,
 * `hint-placeholder-count="2"` on the `askChips` loop) — not invented.
 *
 * The prototype varies `askPlaceholder` per *entity* once a specific
 * contract is open (`'Ask Contigo about '+cur.supplier+' — clauses, dates,
 * spend…'`). This scaffold contextualises by *route* instead: the entity
 * itself (which contract, which renewal) is not loaded here — the real
 * screens that will know it ship in epic-07/epic-08. Suggestion copy is
 * placeholder text, not backed by real query intelligence; the actual Ask
 * Contigo chat ships in epic-07/feature-04-ask-contigo-ui.
 *
 * Task E13/F09/US01/T01 (ADR-024 V2 amendment, gap G-IA-V2) added the
 * `kbReady` off-copy below: `contigo-v2/app.jsx`'s global bar swaps its
 * whole placeholder to `askPlaceholder:kbReady?'...':'Ask Contigo switches
 * on after your first validated contract'` regardless of route -- the
 * route-contextual copy this module already had stays in force once ready,
 * `getAskBarCopy` only overrides the placeholder text while `!kbReady`.
 */
export interface AskBarCopy {
  placeholder: string;
  suggestions: readonly [string, string];
}

/** `contigo-v2/app.jsx` global-bar `askPlaceholder`'s own off-state literal, quoted verbatim. */
const KB_OFF_PLACEHOLDER = "Ask Contigo switches on after your first validated contract";

const DEFAULT_COPY: AskBarCopy = {
  placeholder: "Ask Contigo — spend, renewals, clauses, liability…",
  suggestions: ["Which contracts renew in the next 45 days?", "Where can we save money this quarter?"],
};

const COPY_BY_PATH_PREFIX: ReadonlyArray<readonly [string, AskBarCopy]> = [
  [
    "/contracts",
    {
      placeholder: "Ask Contigo about this portfolio — clauses, dates, spend…",
      suggestions: ["Which contracts are missing a renewal date?", "Show me high-risk termination clauses"],
    },
  ],
  [
    "/renewals",
    {
      placeholder: "Ask Contigo about renewals — thresholds, owners, next steps…",
      suggestions: ["Which renewals are overdue for action?", "Summarize this quarter's renewal pipeline"],
    },
  ],
  [
    "/documents",
    {
      placeholder: "Ask Contigo about a document — status, evidence, extraction…",
      suggestions: ["Which uploads need review?", "What failed processing this week?"],
    },
  ],
  [
    "/review",
    {
      placeholder: "Ask Contigo about the review queue — confidence, evidence…",
      suggestions: ["Which fields have the lowest confidence?", "Show me everything flagged, not just failed"],
    },
  ],
  [
    "/quotes",
    {
      placeholder: "Ask Contigo about this quote — SKUs, benchmark, target price…",
      suggestions: ["What is the market benchmark for this SKU?", "Summarize the negotiation levers"],
    },
  ],
];

/**
 * Looks up contextual copy by the longest matching route prefix, falling back to the prototype's own
 * default line -- then, while `!kbReady`, overrides just the placeholder with the V2 off-copy
 * (`KB_OFF_PLACEHOLDER`). Suggestion chips are left as-is even when off: this task's own text names
 * only the placeholder swap (see this module's header comment).
 *
 * Task E13/F09/US01/T04 (web-ask-v2, gap G-CAPABILITIES): `capabilities`, when supplied and it has
 * a matching entry for the current route, overrides the static `suggestions` pair with the real
 * capability catalog's own `exampleQuestions` (task text: "suggestions from capabilities per screen
 * (fallback to static copy)") -- `capabilities` defaults to `undefined`/`null` (every pre-existing
 * call site keeps behaving exactly as before this task, unchanged).
 */
export function getAskBarCopy(
  pathname: string,
  kbReady: boolean,
  capabilities?: readonly CapabilityBody[] | null,
): AskBarCopy {
  const match = COPY_BY_PATH_PREFIX.find(([prefix]) => pathname.startsWith(prefix));
  const base = match ? match[1] : DEFAULT_COPY;
  const placeholder = kbReady ? base.placeholder : KB_OFF_PLACEHOLDER;

  const catalogSuggestions = suggestionsFromCapabilityCatalog(capabilities ?? null, capabilityKeyForPath(pathname));
  const suggestions = catalogSuggestions ?? base.suggestions;

  return { placeholder, suggestions };
}

/** `../../routes/ask/askViewModel.ts`'s own `CapabilityBody`-shaped catalog entries carry a `key`
 * this module maps route prefixes onto -- additive to (not replacing) `COPY_BY_PATH_PREFIX` above,
 * since that table's own static copy must keep working unchanged whenever the catalog has not
 * loaded yet (see `getAskBarCopy`'s own doc comment). "/ask" itself, and any unmatched prefix, maps
 * to the `ask` capability -- the same fallback `CapabilityCatalog.SuggestionsFor` (backend) uses for
 * an unrecognised screen key. */
const CAPABILITY_KEY_BY_PATH_PREFIX: ReadonlyArray<readonly [string, string]> = [
  ["/contracts", "portfolio"],
  ["/renewals", "renewals"],
  ["/documents", "documents"],
  ["/quotes", "quote-check"],
  ["/savings", "savings"],
  ["/workspace/members", "workspace-members"],
];

function capabilityKeyForPath(pathname: string): string {
  const match = CAPABILITY_KEY_BY_PATH_PREFIX.find(([prefix]) => pathname.startsWith(prefix));
  return match ? match[1] : "ask";
}

/**
 * `capabilities[key].exampleQuestions`, first two -- `null` when `capabilities` has not loaded yet,
 * has no entry for `key`, or that entry has fewer than two example questions (never a single-chip
 * row; the caller's own static fallback covers that instead). Exported for direct unit coverage
 * (`askSuggestions.test.ts` alongside `GlobalAskBar.test.tsx`'s own rendering-level proof).
 */
export function suggestionsFromCapabilityCatalog(
  capabilities: readonly CapabilityBody[] | null,
  key: string,
): readonly [string, string] | null {
  if (capabilities === null) return null;
  const match = capabilities.find((capability) => capability.key === key);
  if (!match || match.exampleQuestions.length < 2) return null;
  return [match.exampleQuestions[0], match.exampleQuestions[1]];
}
