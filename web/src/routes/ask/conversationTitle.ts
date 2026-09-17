import type { ConversationSummaryBody, PortfolioListItem } from "../../api/client";
import { getContractTypeLabel } from "../contracts/portfolioTableFormatters";

/** Short human fallback when no supplier/contract is bound — never a guid, never the question. */
export const ASK_FALLBACK_TITLE = "Ask Raffa";

export function formatConversationTitle(input: {
  supplierName?: string | null;
  contractLabel?: string | null;
}): string {
  const supplier = input.supplierName?.trim() ?? "";
  const contract = input.contractLabel?.trim() ?? "";
  if (supplier !== "" && contract !== "") return `${supplier} — ${contract}`;
  if (supplier !== "") return supplier;
  if (contract !== "") return contract;
  return ASK_FALLBACK_TITLE;
}

export function conversationDisplayTitle(
  conversation: Pick<ConversationSummaryBody, "scopeContractId">,
  portfolioById: ReadonlyMap<string, Pick<PortfolioListItem, "supplierName" | "type">>,
): string {
  const scopeId = conversation.scopeContractId?.trim() ?? "";
  if (scopeId === "") return ASK_FALLBACK_TITLE;

  const row = portfolioById.get(scopeId);
  if (row === undefined) return ASK_FALLBACK_TITLE;
  return formatConversationTitle({
    supplierName: row.supplierName,
    contractLabel: getContractTypeLabel(row.type),
  });
}

export function filterConversations<T extends { title: string; id: string }>(
  conversations: readonly T[],
  query: string,
  displayTitle: (conversation: T) => string,
): readonly T[] {
  const needle = query.trim().toLowerCase();
  if (needle === "") return conversations;
  return conversations.filter((conversation) => displayTitle(conversation).toLowerCase().includes(needle));
}

export function indexPortfolioByContractId(
  items: readonly PortfolioListItem[],
): ReadonlyMap<string, Pick<PortfolioListItem, "supplierName" | "type">> {
  const map = new Map<string, Pick<PortfolioListItem, "supplierName" | "type">>();
  for (const item of items) {
    map.set(item.contractId, { supplierName: item.supplierName, type: item.type });
  }
  return map;
}
