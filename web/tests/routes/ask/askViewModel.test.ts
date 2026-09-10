import { describe, expect, it, vi } from "vitest";
import type { ApiClient, ConversationCitationBody, ConversationMessageBody, ConversationReplyBody } from "../../../src/api/client";
import {
  ASK_HELLO,
  buildRaffaTurnFromReply,
  buildErrorTurn,
  buildOffCopy,
  buildScopeLine,
  buildScopedSuggestions,
  buildTenantCitationHref,
  buildTurnsFromConversation,
  buildYouTurn,
  createConversationAndAsk,
  deriveConversationTitle,
  mapConversationAction,
  mapConversationCitation,
  mapConversationMessageToReply,
  mapConversationReplyToReply,
  nextTurnId,
  parseScopeContractId,
  resolveCitationOpenAction,
  suggestionsFor,
  toCitationCorpus,
} from "../../../src/routes/ask/askViewModel";

function citation(overrides: Partial<ConversationCitationBody> = {}): ConversationCitationBody {
  return {
    n: 1,
    corpus: "tenant",
    title: "Salesforce · MSA 2024",
    subtitle: "p.12 §8.4",
    snippet: "automatically renew for successive twelve (12) month periods",
    documentId: "doc-1",
    contractId: "contract-1",
    page: 12,
    section: "8.4",
    previewUrl: "/api/documents/doc-1/preview",
    href: "/contracts/contract-1",
    recordId: null,
    ...overrides,
  };
}

describe("toCitationCorpus", () => {
  it("passes tenant/market/raffa through unchanged", () => {
    expect(toCitationCorpus("tenant")).toBe("tenant");
    expect(toCitationCorpus("market")).toBe("market");
    expect(toCitationCorpus("raffa")).toBe("raffa");
  });

  it("folds an unrecognised value (e.g. the real backend's internal 'calc') into 'tenant'", () => {
    expect(toCitationCorpus("calc")).toBe("tenant");
    expect(toCitationCorpus("anything-else")).toBe("tenant");
  });
});

describe("buildTenantCitationHref", () => {
  it("appends ?page= when a page is known and href has no query string yet", () => {
    expect(buildTenantCitationHref("/contracts/contract-1", 12)).toBe("/contracts/contract-1?page=12");
  });

  it("returns the bare href unchanged when no page is known", () => {
    expect(buildTenantCitationHref("/contracts/contract-1", null)).toBe("/contracts/contract-1");
  });

  it("trusts an href that already carries a query string (defensive against a future backend fix)", () => {
    expect(buildTenantCitationHref("/contracts/contract-1?clause=cl-1", 12)).toBe("/contracts/contract-1?clause=cl-1");
  });

  it("returns null when href itself is null", () => {
    expect(buildTenantCitationHref(null, 12)).toBeNull();
  });
});

describe("mapConversationCitation", () => {
  it("maps a tenant citation, enriching href with ?page=", () => {
    const view = mapConversationCitation(citation());

    expect(view).toEqual({
      n: 1,
      corpus: "tenant",
      title: "Salesforce · MSA 2024",
      subtitle: "p.12 §8.4",
      snippet: "automatically renew for successive twelve (12) month periods",
      previewUrl: "/api/documents/doc-1/preview",
      href: "/contracts/contract-1?page=12",
    });
  });

  it("leaves a market citation's href untouched (never ?page=-enriched)", () => {
    const view = mapConversationCitation(
      citation({ corpus: "market", href: null, page: null, recordId: "rec-1", subtitle: "representative market data · mock feed · updated 2026-09-01" }),
    );

    expect(view.corpus).toBe("market");
    expect(view.href).toBeNull();
  });

  it("falls back to an empty string subtitle when the wire sends null", () => {
    const view = mapConversationCitation(citation({ subtitle: null }));
    expect(view.subtitle).toBe("");
  });
});

describe("mapConversationAction", () => {
  it("maps the first action to primary and every other to secondary, regardless of wire kind", () => {
    expect(mapConversationAction({ label: "Open Contract 360 →", href: "/contracts/contract-1", kind: "navigate" }, 0)).toEqual({
      label: "Open Contract 360 →",
      href: "/contracts/contract-1",
      kind: "primary",
    });
    expect(mapConversationAction({ label: "Upload in Documents", href: "/documents", kind: "upload" }, 1)).toEqual({
      label: "Upload in Documents",
      href: "/documents",
      kind: "secondary",
    });
  });
});

describe("mapConversationReplyToReply / mapConversationMessageToReply", () => {
  function reply(overrides: Partial<ConversationReplyBody> = {}): ConversationReplyBody {
    return {
      conversationId: "conv-1",
      messageId: "msg-1",
      kind: "answer",
      answerMarkdown: "Salesforce ends on **15 January 2027** [1].",
      citations: [citation()],
      actions: [{ label: "Open Contract 360 →", href: "/contracts/contract-1", kind: "navigate" }],
      provenance: { sources: ["tenant"], modelId: "gpt", promptVersion: "answer-v2.1", inputHash: "abc" },
      followUps: ["Where can I push on the renewal?"],
      ...overrides,
    };
  }

  it("maps an answer reply with citations, actions and followUps intact", () => {
    const mapped = mapConversationReplyToReply(reply());

    expect(mapped.kind).toBe("answer");
    if (mapped.kind !== "answer") throw new Error("expected answer");
    expect(mapped.answerMarkdown).toBe("Salesforce ends on **15 January 2027** [1].");
    expect(mapped.citations).toHaveLength(1);
    expect(mapped.actions).toEqual([{ label: "Open Contract 360 →", href: "/contracts/contract-1", kind: "primary" }]);
    expect(mapped.followUps).toEqual(["Where can I push on the renewal?"]);
  });

  it("maps a redirect/refusal reply to warm prose + actions, no citations slot at all", () => {
    const mapped = mapConversationReplyToReply(reply({ kind: "redirect", answerMarkdown: "Ask never accepts attachments." }));

    expect(mapped.kind).toBe("redirect");
    if (mapped.kind !== "redirect") throw new Error("expected redirect");
    expect(mapped.answerMarkdown).toBe("Ask never accepts attachments.");
    expect(mapped).not.toHaveProperty("citations");
  });

  it("maps an abstain reply's answerMarkdown onto reason (the wire has no separate reason field)", () => {
    const mapped = mapConversationReplyToReply(
      reply({ kind: "abstain", answerMarkdown: "Nothing in the validated contracts supports a reliable answer.", citations: [], actions: [] }),
    );

    expect(mapped).toEqual({ kind: "abstain", reason: "Nothing in the validated contracts supports a reliable answer." });
  });

  it("maps a stored message the same way, with an always-empty followUps (no such column on ConversationMessage)", () => {
    const message: ConversationMessageBody = {
      id: "msg-1",
      role: "raffa",
      kind: "answer",
      markdown: "AWS liability is capped at USD 500,000 [1].",
      citations: [citation()],
      actions: [],
      modelId: null,
      promptVersion: null,
      inputHash: null,
      createdAt: "2026-09-08T00:00:00Z",
    };

    const mapped = mapConversationMessageToReply(message);
    expect(mapped.kind).toBe("answer");
    if (mapped.kind !== "answer") throw new Error("expected answer");
    expect(mapped.followUps).toEqual([]);
  });
});

describe("buildYouTurn / buildRaffaTurnFromReply / buildRaffaTurnFromMessage / buildTurnsFromConversation", () => {
  it("builds a plain you turn", () => {
    expect(buildYouTurn("t1", "What is our AWS spend?")).toEqual({ id: "t1", role: "you", text: "What is our AWS spend?" });
  });

  it("keeps the original wire citations alongside the mapped reply, for later click resolution", () => {
    const turn = buildRaffaTurnFromReply("t2", {
      conversationId: "conv-1",
      messageId: "msg-1",
      kind: "answer",
      answerMarkdown: "…",
      citations: [citation({ corpus: "market", recordId: "rec-1" })],
      actions: [],
      provenance: { sources: [], modelId: null, promptVersion: null, inputHash: null },
      followUps: [],
    });

    expect(turn.role).toBe("raffa");
    if (turn.role !== "raffa") throw new Error("expected raffa");
    expect(turn.wireCitations[0].recordId).toBe("rec-1");
  });

  it("builds turns from a resumed conversation, oldest first, matching the wire's own message order", () => {
    const turns = buildTurnsFromConversation({
      id: "conv-1",
      title: "When does Salesforce expire?",
      scopeContractId: null,
      createdAt: "2026-09-08T00:00:00Z",
      updatedAt: "2026-09-08T00:05:00Z",
      messages: [
        { id: "m1", role: "you", kind: "answer", markdown: "When does Salesforce expire?", citations: [], actions: [], modelId: null, promptVersion: null, inputHash: null, createdAt: "2026-09-08T00:00:00Z" },
        { id: "m2", role: "raffa", kind: "answer", markdown: "…", citations: [], actions: [], modelId: null, promptVersion: null, inputHash: null, createdAt: "2026-09-08T00:00:05Z" },
      ],
    });

    expect(turns.map((t) => t.role)).toEqual(["you", "raffa"]);
    expect(turns[0]).toEqual({ id: "m1", role: "you", text: "When does Salesforce expire?" });
  });

  it("builds a distinct error turn (never confused with an abstain)", () => {
    const turn = buildErrorTurn("t3", "network down");
    expect(turn.role).toBe("raffa");
    if (turn.role !== "raffa") throw new Error("expected raffa");
    expect(turn.reply).toEqual({ kind: "error", reason: "network down" });
  });

  it("nextTurnId never repeats within one process", () => {
    expect(nextTurnId()).not.toBe(nextTurnId());
  });
});

describe("buildOffCopy (R-ASK-10; screens-v2.md #2 off state)", () => {
  it("names the 'upload first' variant when the tenant has no document at all", () => {
    const copy = buildOffCopy(false);
    expect(copy.ctaLabel).toBe("Upload a contract");
    expect(copy.reason).toMatch(/upload a contract first/i);
  });

  it("names the 'still processing' variant when at least one document exists but none is validated", () => {
    const copy = buildOffCopy(true);
    expect(copy.ctaLabel).toBe("Go to Documents");
    expect(copy.reason).toMatch(/still processing or waiting for review/i);
  });
});

describe("buildScopeLine (R-ASK-10)", () => {
  it("pluralizes 'contract' for exactly one validated contract", () => {
    expect(buildScopeLine(1, [])).toBe("Answers only from 1 validated contract · cites or abstains");
  });

  it("pluralizes 'contracts' for zero or more than one", () => {
    expect(buildScopeLine(0, [])).toBe("Answers only from 0 validated contracts · cites or abstains");
    expect(buildScopeLine(3, [])).toBe("Answers only from 3 validated contracts · cites or abstains");
  });

  it("appends a parenthetical name list only when names are known", () => {
    expect(buildScopeLine(2, ["Salesforce", "AWS"])).toBe("Answers only from 2 validated contracts (Salesforce, AWS) · cites or abstains");
  });
});

describe("buildScopedSuggestions / suggestionsFor", () => {
  it("templates the two prototype-verbatim c360Chips with the real supplier name", () => {
    expect(buildScopedSuggestions("Salesforce")).toEqual([
      "When must we give notice to Salesforce?",
      "What is our liability cap with Salesforce?",
    ]);
  });

  it("falls back to 'this supplier' when the name is null or blank", () => {
    expect(buildScopedSuggestions(null)).toEqual([
      "When must we give notice to this supplier?",
      "What is our liability cap with this supplier?",
    ]);
    expect(buildScopedSuggestions("  ")[0]).toContain("this supplier");
  });

  it("suggestionsFor returns the scoped pair when a supplierName (even null) is explicitly passed", () => {
    expect(suggestionsFor(null, "Salesforce")).toEqual(buildScopedSuggestions("Salesforce"));
    expect(suggestionsFor(null, null)).toEqual(buildScopedSuggestions(null));
  });

  it("suggestionsFor reads the catalog's own ask exampleQuestions, first two, when unscoped", () => {
    const result = suggestionsFor([
      {
        key: "ask",
        title: "Ask Raffa",
        routePattern: "/ask",
        description: "…",
        exampleQuestions: ["What can Raffa do?", "When does this contract expire?", "What liabilities do we have?"],
        roleGate: "any",
        availability: "always",
        howTo: [],
      },
    ]);

    expect(result).toEqual(["What can Raffa do?", "When does this contract expire?"]);
  });

  it("suggestionsFor falls back to the static pair when the catalog has not loaded or has no ask entry", () => {
    expect(suggestionsFor(null)).toEqual(["When does a contract expire?", "What liabilities do we have?"]);
    expect(suggestionsFor([])).toEqual(["When does a contract expire?", "What liabilities do we have?"]);
  });
});

describe("deriveConversationTitle", () => {
  it("collapses embedded whitespace/newlines to single spaces", () => {
    expect(deriveConversationTitle("What   is\nour   AWS\tspend?")).toBe("What is our AWS spend?");
  });

  it("hard-truncates at 48 characters with no ellipsis", () => {
    const long = "Which of our contracts have unlimited liability and no cap on total exposure?";
    const title = deriveConversationTitle(long);
    expect(title).toHaveLength(48);
    expect(title).toBe(long.slice(0, 48));
  });
});

describe("resolveCitationOpenAction (AC-3; R-EVD-02)", () => {
  it("resolves a tenant citation to navigate, using its own (already-enriched) href", () => {
    const view = mapConversationCitation(citation());
    expect(resolveCitationOpenAction(view, [citation()])).toEqual({ kind: "navigate", href: "/contracts/contract-1?page=12" });
  });

  it("resolves a raffa feature citation to navigate, using its own href", () => {
    const wire = citation({ corpus: "raffa", href: "/renewals", page: null, contractId: null, documentId: null });
    const view = mapConversationCitation(wire);
    expect(resolveCitationOpenAction(view, [wire])).toEqual({ kind: "navigate", href: "/renewals" });
  });

  it("resolves a market citation to open the panel, recovering recordId from the wire citation (not on the presentational type)", () => {
    const wire = citation({ corpus: "market", href: null, page: null, recordId: "rec-1" });
    const view = mapConversationCitation(wire);
    expect(resolveCitationOpenAction(view, [wire])).toEqual({ kind: "market-panel", recordId: "rec-1" });
  });

  it("resolves to 'none' when a market citation's wire recordId is missing", () => {
    const wire = citation({ corpus: "market", href: null, page: null, recordId: null });
    const view = mapConversationCitation(wire);
    expect(resolveCitationOpenAction(view, [wire])).toEqual({ kind: "none" });
  });

  it("resolves to 'none' when a tenant citation has no href at all", () => {
    const wire = citation({ href: null, page: null });
    const view = mapConversationCitation(wire);
    expect(resolveCitationOpenAction(view, [wire])).toEqual({ kind: "none" });
  });
});

describe("parseScopeContractId", () => {
  it("returns undefined for a null or blank query value", () => {
    expect(parseScopeContractId(null)).toBeUndefined();
    expect(parseScopeContractId("   ")).toBeUndefined();
  });

  it("returns the trimmed value otherwise", () => {
    expect(parseScopeContractId(" contract-1 ")).toBe("contract-1");
  });
});

describe("createConversationAndAsk", () => {
  function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
    return {
      getHealth: vi.fn(),
      createWorkspace: vi.fn(),
      inviteWorkspaceMember: vi.fn(),
      uploadDocument: vi.fn(),
      getDocument: vi.fn(),
      listDocuments: vi.fn(),
      getDocumentPreviewUrl: vi.fn(),
      reprocessDocument: vi.fn(),
      deleteDocument: vi.fn(),
      getPortfolio: vi.fn(),
      getContract360: vi.fn(),
      getRenewals: vi.fn(),
      getRenewalPriority: vi.fn(),
      getCorrectionHistory: vi.fn(),
      correctContract: vi.fn(),
      getContractEvidence: vi.fn(),
      validateDocument: vi.fn(),
      postRenewalAction: vi.fn(),
      uploadQuote: vi.fn(),
      getQuoteAssessment: vi.fn(),
      recalculateQuoteAssessment: vi.fn(),
      captureNegotiationOutcome: vi.fn(),
      askRaffa: vi.fn(),
      getSavingsKpis: vi.fn(),
      getSavingsOpportunities: vi.fn(),
      listConversations: vi.fn(),
      createConversation: vi.fn(),
      getConversation: vi.fn(),
      postMessage: vi.fn(),
      getCapabilities: vi.fn(),
      getMarketRecord: vi.fn(),
      ...overrides,
    };
  }

  it("creates the conversation, then posts the message, then returns both the id and the reply", async () => {
    const createConversation = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 201,
      conversation: { id: "conv-1", title: "New chat", scopeContractId: null, updatedAt: "2026-09-08T00:00:00Z" },
      error: null,
    });
    const reply: ConversationReplyBody = {
      conversationId: "conv-1",
      messageId: "msg-1",
      kind: "answer",
      answerMarkdown: "…",
      citations: [],
      actions: [],
      provenance: { sources: [], modelId: null, promptVersion: null, inputHash: null },
      followUps: [],
    };
    const postMessage = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, reply, error: null });
    const apiClient = mockApiClient({ createConversation, postMessage });

    const result = await createConversationAndAsk(apiClient, "tenant-1", "When does Salesforce expire?", undefined);

    expect(createConversation).toHaveBeenCalledWith("tenant-1", {});
    expect(postMessage).toHaveBeenCalledWith("tenant-1", "conv-1", { question: "When does Salesforce expire?" });
    expect(result).toEqual({ ok: true, conversationId: "conv-1", reply });
  });

  it("passes scopeContractId through to createConversation when supplied", async () => {
    const createConversation = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 201,
      conversation: { id: "conv-1", title: "New chat", scopeContractId: "contract-1", updatedAt: "2026-09-08T00:00:00Z" },
      error: null,
    });
    const postMessage = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      reply: {
        conversationId: "conv-1",
        messageId: "msg-1",
        kind: "answer",
        answerMarkdown: "…",
        citations: [],
        actions: [],
        provenance: { sources: [], modelId: null, promptVersion: null, inputHash: null },
        followUps: [],
      },
      error: null,
    });
    const apiClient = mockApiClient({ createConversation, postMessage });

    await createConversationAndAsk(apiClient, "tenant-1", "When must we give notice to Salesforce?", "contract-1");

    expect(createConversation).toHaveBeenCalledWith("tenant-1", { scopeContractId: "contract-1" });
  });

  it("reports failure (with no conversationId) when creation itself fails", async () => {
    const createConversation = vi.fn().mockResolvedValue({ ok: false, statusCode: 400, conversation: null, error: "bad request" });
    const apiClient = mockApiClient({ createConversation });

    const result = await createConversationAndAsk(apiClient, "tenant-1", "…", undefined);

    expect(result).toEqual({ ok: false, conversationId: null, reason: "bad request" });
    expect(apiClient.postMessage).not.toHaveBeenCalled();
  });

  it("reports failure (with the real conversationId, still worth resuming) when only the message post fails", async () => {
    const createConversation = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 201,
      conversation: { id: "conv-1", title: "New chat", scopeContractId: null, updatedAt: "2026-09-08T00:00:00Z" },
      error: null,
    });
    const postMessage = vi.fn().mockResolvedValue({ ok: false, statusCode: 400, reply: null, error: "A non-empty 'question' is required." });
    const apiClient = mockApiClient({ createConversation, postMessage });

    const result = await createConversationAndAsk(apiClient, "tenant-1", "…", undefined);

    expect(result).toEqual({ ok: false, conversationId: "conv-1", reason: "A non-empty 'question' is required." });
  });
});

describe("ASK_HELLO", () => {
  it("is the prototype's own verbatim string", () => {
    expect(ASK_HELLO).toBe("What do you want to know?");
  });
});
