/**
 * One way into Ask Raffa from any screen: a new chat that asks `question` straight away, optionally
 * bound to a contract. It is the same navigation the global Ask bar makes on submit
 * (`GlobalAskBar.tsx`): `/ask` (or `/ask?scope=<contractId>`, which `AskRoute` reads through
 * `askViewModel.ts#parseScopeContractId` to create a scoped conversation) with `{ query, newChat }`
 * in router state, which `AskRoute` asks exactly once for that history entry. Savings, Renewals and
 * any other screen that wants a "Ask Raffa about this" control build it here, so a change to how
 * Ask is opened happens in one place.
 */
export interface AskLaunch {
  to: string;
  state: { query: string; newChat: true };
}

export function buildAskLaunch(question: string, scopeContractId: string | null = null): AskLaunch {
  const scope = scopeContractId?.trim() ?? "";
  return {
    to: scope === "" ? "/ask" : `/ask?scope=${encodeURIComponent(scope)}`,
    state: { query: question.trim(), newChat: true },
  };
}

/**
 * The questions Savings and Renewals hand to Ask. Each is phrased to land on the Ask intent it is
 * meant for (`Raffa.Chat` `IntentPlanner` / `CapabilityGapCatalog` lexicons): "approach … renewal"
 * is a renewal strategy, "why … top" the priority explanation, "save" a savings turn, "market" a
 * benchmark, "Draft … email" the drafted negotiation email (ADR-030). The operations Ask cannot
 * perform yet (reminder, calendar, export, sending a letter) are phrased so Ask recognises them as
 * a capability gap: it says so in one sentence, offers the nearest alternative and can file the
 * request for the team. A question names the supplier, never an id; without a name (`null`) it
 * says "this contract" -- the chat is bound to that contract anyway.
 */
type Supplier = string | null;

export const ASK_PROMPTS = {
  whereToSave: "Where can we save?",
  renewalsStartFirst: "Which renewals should we start first?",
  saveWithSupplier: (supplier: Supplier) => (supplier === null ? "Where can we save on this contract?" : `Where can we save with ${supplier}?`),
  marketCheck: (supplier: Supplier) => (supplier === null ? "Are we paying above market on this contract?" : `Are we paying above market with ${supplier}?`),
  renewalApproach: (supplier: Supplier) => (supplier === null ? "How should we approach this renewal?" : `How should we approach the ${supplier} renewal?`),
  renewalWhyTop: (supplier: Supplier) =>
    supplier === null ? "Why is this contract at the top of our renewals?" : `Why is ${supplier} at the top of our renewals?`,
  negotiationEmail: (supplier: Supplier) =>
    supplier === null ? "Draft the renewal negotiation email for this contract" : `Draft the renewal negotiation email for ${supplier}`,
  startFirstAmong: (suppliers: readonly string[]) =>
    suppliers.length === 0 ? "Which renewals should we start first?" : `Which of these renewals should we start first: ${suppliers.join(", ")}?`,
  reminder: (supplier: Supplier) => (supplier === null ? "Remind me before this contract's notice deadline" : `Remind me before the ${supplier} notice deadline`),
  calendar: (supplier: Supplier) =>
    supplier === null ? "Add this contract's notice deadline to my calendar" : `Add the ${supplier} notice deadline to my calendar`,
  exportBrief: (supplier: Supplier) => (supplier === null ? "Export this renewal brief to Excel" : `Export the ${supplier} renewal brief to Excel`),
  sendNotice: (supplier: Supplier) =>
    supplier === null ? "Send the cancellation notice letter to the supplier" : `Send the cancellation notice letter to ${supplier}`,
} as const;
