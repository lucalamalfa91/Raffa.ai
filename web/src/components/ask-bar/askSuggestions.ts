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
 */
export interface AskBarCopy {
  placeholder: string;
  suggestions: readonly [string, string];
}

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

/** Looks up contextual copy by the longest matching route prefix, falling back to the prototype's own default line. */
export function getAskBarCopy(pathname: string): AskBarCopy {
  const match = COPY_BY_PATH_PREFIX.find(([prefix]) => pathname.startsWith(prefix));
  return match ? match[1] : DEFAULT_COPY;
}
