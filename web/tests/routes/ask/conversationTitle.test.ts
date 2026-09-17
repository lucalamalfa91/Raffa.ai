import { describe, expect, it } from "vitest";
import {
  ASK_FALLBACK_TITLE,
  conversationDisplayTitle,
  filterConversations,
  formatConversationTitle,
  indexPortfolioByContractId,
} from "../../../src/routes/ask/conversationTitle";
import type { PortfolioListItem } from "../../../src/api/client";

function portfolioItem(overrides: Partial<PortfolioListItem> = {}): PortfolioListItem {
  return {
    contractId: "contract-1",
    supplierId: null,
    supplierName: "Salesforce",
    type: "Msa",
    annualSpend: null,
    currency: "CHF",
    startDate: null,
    endDate: null,
    renewalDate: null,
    cancellationDeadline: null,
    autoRenewal: false,
    status: "active",
    risk: null,
    ...overrides,
  };
}

describe("conversationTitle", () => {
  it("formats supplier and contract as an em-dash pair", () => {
    expect(formatConversationTitle({ supplierName: "Salesforce", contractLabel: "MSA" })).toBe(
      "Salesforce — MSA",
    );
  });

  it("falls back to Ask Raffa when nothing is bound — never a guid", () => {
    expect(formatConversationTitle({})).toBe(ASK_FALLBACK_TITLE);
    expect(conversationDisplayTitle({ scopeContractId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }, new Map())).toBe(
      ASK_FALLBACK_TITLE,
    );
  });

  it("joins a portfolio row onto a scoped conversation", () => {
    const map = indexPortfolioByContractId([portfolioItem()]);
    expect(conversationDisplayTitle({ scopeContractId: "contract-1" }, map)).toBe("Salesforce — MSA");
  });

  it("filters chats by the displayed title, not the stored question", () => {
    const conversations = [
      { id: "1", title: "quando è la scadenza?", scopeContractId: "contract-1" },
      { id: "2", title: "quando è la scadenza?", scopeContractId: null },
    ];
    const map = indexPortfolioByContractId([portfolioItem()]);
    const titleOf = (conversation: (typeof conversations)[number]) => conversationDisplayTitle(conversation, map);
    expect(filterConversations(conversations, "sales", titleOf).map((row) => row.id)).toEqual(["1"]);
    expect(filterConversations(conversations, "raffa", titleOf).map((row) => row.id)).toEqual(["2"]);
  });
});
