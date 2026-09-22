import { describe, expect, it, vi } from "vitest";
import type { ApiClient, ConversationCitationBody, ConversationMessageBody, ConversationReplyBody } from "../../../src/api/client";
import {
  ASK_HELLO,
  buildErrorTurn,
  buildInterviewAnswerRequest,
  buildOffCopy,
  buildRaffaTurnFromReply,
  buildScopeLine,
  buildScopedBrief,
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
  markInterviewAnswered,
  nextTurnId,
  parseScopeContractId,
  pendingConsent,
  pendingInterview,
  resolveAskOffReason,
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
  it("passes tenant/market/raffa/calc through unchanged", () => {
    expect(toCitationCorpus("tenant")).toBe("tenant");
    expect(toCitationCorpus("market")).toBe("market");
    expect(toCitationCorpus("raffa")).toBe("raffa");
    // `PackCorpus.Calc` -- a calculator's own output -- keeps its provenance so the evidence card
    // can file it under Raffa instead of passing it off as a validated contract.
    expect(toCitationCorpus("calc")).toBe("calc");
  });

  it("folds an unrecognised value into 'tenant'", () => {
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
      documentId: "doc-1",
      page: 12,
      href: "/contracts/contract-1?page=12",
      // Task E28/F03/US02/T01 (NW-83/NW-93): echoed verbatim from the wire's own `contractId` so
      // `CitationCard.tsx` can build its two-CTA card's "Open contract" action.
      contractId: "contract-1",
    });
  });

  // Task E28/F03/US02/T01 (NW-83/NW-93): once the wire's own `href` has already resolved to the
  // W18 viewer route (`AskCopilotService.ResolveTenantClauseLinks`, no bare page left to append),
  // this mapper changes nothing about it -- `CitationCard.tsx` reads the viewer href verbatim.
  it("passes a viewer-route href through unchanged (no ?page= re-synthesis once the backend already resolved one)", () => {
    const view = mapConversationCitation(
      citation({ href: "/documents/doc-1/viewer?page=12&clause=clause-1", documentId: "doc-1" }),
    );

    expect(view.href).toBe("/documents/doc-1/viewer?page=12&clause=clause-1");
    expect(view.contractId).toBe("contract-1");
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
      reply({ kind: "abstain", answerMarkdown: "Nothing in the validated contracts supports a reliable answer.", citations: [], actions: [], followUps: [] }),
    );

    expect(mapped).toEqual({ kind: "abstain", reason: "Nothing in the validated contracts supports a reliable answer.", actions: [] });
  });

  // Task E25/F05/US02/T01 (abstain-recovery-web; ADR-024 "every abstain has a clickable next
  // step"): the abstain branch no longer drops `turn.actions` -- it maps them the same way
  // redirect/refusal already do, via the shared `mapConversationAction`. `ReplyBody.tsx` (not this
  // mapper) is what forces the result to render secondary-only; the "primary" kind below is the
  // honest output of `toReplyActionKind(0)`, proven separately by the `mapConversationAction`
  // describe block above.
  it("maps an abstain reply's recovery action the same way answer/redirect/refusal map theirs", () => {
    const mapped = mapConversationReplyToReply(
      reply({
        kind: "abstain",
        answerMarkdown: "Nothing in the validated contracts supports a reliable answer.",
        citations: [],
        actions: [{ label: "Upload a contract", href: "/documents", kind: "upload" }],
        followUps: [],
      }),
    );

    expect(mapped).toEqual({
      kind: "abstain",
      reason: "Nothing in the validated contracts supports a reliable answer.",
      actions: [{ label: "Upload a contract", href: "/documents", kind: "primary" }],
    });
  });

  // A gap is never a dead end: the server's next-step questions ride along on an abstain too.
  it("maps an abstain reply's follow-up questions when the server sent some", () => {
    const mapped = mapConversationReplyToReply(
      reply({
        kind: "abstain",
        answerMarkdown: "Nothing in your validated contracts supports a reliable answer.",
        citations: [],
        actions: [],
        followUps: ["Which contracts are most critical?", "Where can we save?"],
      }),
    );

    expect(mapped).toEqual({
      kind: "abstain",
      reason: "Nothing in your validated contracts supports a reliable answer.",
      actions: [],
      followUps: ["Which contracts are most critical?", "Where can we save?"],
    });
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

describe("interview mapping and answering (ADR-030)", () => {
  function replyBody(overrides: Partial<ConversationReplyBody> = {}): ConversationReplyBody {
    return {
      conversationId: "conv-1",
      messageId: "msg-1",
      kind: "answer",
      answerMarkdown: "…",
      citations: [],
      actions: [],
      provenance: { sources: [], modelId: null, promptVersion: null, inputHash: null },
      followUps: [],
      ...overrides,
    };
  }

  function storedMessage(overrides: Partial<ConversationMessageBody> = {}): ConversationMessageBody {
    return {
      id: "m-1",
      role: "raffa",
      kind: "answer",
      markdown: "…",
      citations: [],
      actions: [],
      modelId: null,
      promptVersion: null,
      inputHash: null,
      createdAt: "2026-09-08T00:00:00Z",
      ...overrides,
    };
  }

  const INTERVIEW_WIRE = {
    prompt: "Before I answer, one quick check.",
    questions: [
      {
        key: "interpretation",
        prompt: "Which of these do you mean?",
        presentation: "choice" as const,
        allowFreeText: true,
        options: [
          { key: "portfolio-overview", label: "The most critical contracts and where we can save", hint: null },
          { key: "total-spend", label: "Our total annual spend across contracts", hint: null },
        ],
      },
    ],
    answered: false,
  };

  it("maps an interview reply with its questions, options and the server message id", () => {
    const reply = mapConversationReplyToReply(
      replyBody({ kind: "interview", answerMarkdown: "Before I answer, one quick check.", citations: [], actions: [], followUps: [], interview: INTERVIEW_WIRE }),
    );

    expect(reply.kind).toBe("interview");
    if (reply.kind !== "interview") throw new Error("unreachable");
    expect(reply.prompt).toBe("Before I answer, one quick check.");
    expect(reply.messageId).toBe("msg-1");
    expect(reply.answered).toBe(false);
    expect(reply.questions[0].options.map((o) => o.key)).toEqual(["portfolio-overview", "total-spend"]);
    expect(reply.questions[0].options[0].hint).toBeNull();
  });

  it("maps a stored interview message with the server's answered flag and its own id", () => {
    const reply = mapConversationMessageToReply({
      ...storedMessage({ kind: "interview", markdown: "Before I answer, one quick check.", citations: [], actions: [] }),
      id: "m-9",
      interview: { ...INTERVIEW_WIRE, answered: true },
    });

    expect(reply).toMatchObject({ kind: "interview", answered: true, messageId: "m-9" });
  });

  it("pendingInterview names the interview the next typed message answers, and nothing once answered", () => {
    const interviewTurn = buildRaffaTurnFromReply(
      "t-2",
      replyBody({ kind: "interview", answerMarkdown: "…", citations: [], actions: [], followUps: [], interview: INTERVIEW_WIRE }),
    );

    expect(pendingInterview([buildYouTurn("t-1", "Did you over all my contract?"), interviewTurn])).toEqual({ messageId: "msg-1", questionKey: "interpretation" });
    expect(pendingInterview(markInterviewAnswered([interviewTurn], "msg-1"))).toBeNull();
    expect(pendingInterview([buildYouTurn("t-1", "hi")])).toBeNull();
    expect(pendingInterview([interviewTurn, buildYouTurn("t-3", "typed")])).toBeNull();
  });

  it("buildInterviewAnswerRequest sends the label as the transcript line and the keys as the answer", () => {
    expect(buildInterviewAnswerRequest("Our total annual spend across contracts", "msg-1", "interpretation", "total-spend")).toEqual({
      question: "Our total annual spend across contracts",
      interviewAnswer: { messageId: "msg-1", questionKey: "interpretation", optionKey: "total-spend" },
    });
    expect(buildInterviewAnswerRequest("the first one", "msg-1", "interpretation", null)).toEqual({
      question: "the first one",
      interviewAnswer: { messageId: "msg-1", questionKey: "interpretation", freeText: true },
    });
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

// Task E16/F03/US01/T01 (ADR-020 w15 §2.1, ADR-012 w15 §4): three states, read off the server's
// `counts`, and the third is never collapsed back into the "still processing" sentence.
describe("buildOffCopy (R-ASK-10; screens-v2.md #2 off state)", () => {
  it("names the 'upload first' variant when the tenant has no document at all", () => {
    const copy = buildOffCopy("no-documents");
    expect(copy.ctaLabel).toBe("Upload a contract");
    expect(copy.reason).toBe("Upload a contract first. Raffa.ai extracts the facts, you sign off the weak ones, and Ask switches on.");
  });

  it("names the 'still processing' variant when a document is in flight or waiting for review", () => {
    const copy = buildOffCopy("processing");
    expect(copy.ctaLabel).toBe("Go to Documents");
    expect(copy.reason).toBe(
      "Your document is still processing or waiting for review. Ask only answers from facts that passed validation — so it never guesses.",
    );
  });

  it("names the third variant -- 'could not finish' -- when documents are held but none is in flight or validated", () => {
    const copy = buildOffCopy("stalled");
    expect(copy.ctaLabel).toBe("Go to Documents");
    expect(copy.reason).toBe(
      "Raffa.ai could not finish processing your documents. Ask only answers from facts that passed validation — so it never guesses.",
    );
    expect(copy.reason).not.toMatch(/still processing/i);
  });
});

describe("resolveAskOffReason (ADR-027 §D7 counts -> the off-copy variant)", () => {
  const counts = (overrides: Partial<{ all: number; needsAttention: number; needsReview: number; processing: number; rejected: number }>) => ({
    all: 0,
    needsAttention: 0,
    needsReview: 0,
    processing: 0,
    rejected: 0,
    ...overrides,
  });

  it("is 'no-documents' with no counts yet, or when Raffa.ai holds nothing", () => {
    expect(resolveAskOffReason(null)).toBe("no-documents");
    expect(resolveAskOffReason(counts({}))).toBe("no-documents");
  });

  it("is 'no-documents' for a tenant holding only refused files -- `all` excludes Rejected, and each refusal was explained on its row", () => {
    expect(resolveAskOffReason(counts({ rejected: 3 }))).toBe("no-documents");
  });

  it("is 'processing' while at least one document is Uploaded/Processing or waiting for review", () => {
    expect(resolveAskOffReason(counts({ all: 2, needsAttention: 2, processing: 1 }))).toBe("processing");
    expect(resolveAskOffReason(counts({ all: 1, needsAttention: 1, needsReview: 1 }))).toBe("processing");
  });

  it("is 'stalled' when documents are held, none is in flight and none is waiting for review (only Failed)", () => {
    expect(resolveAskOffReason(counts({ all: 1, needsAttention: 1 }))).toBe("stalled");
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

// Task E25/F03/US02/T01 (NW-56): a scoped `?scope=<contractId>` entry (Contract 360's "Ask about
// it") briefs the contract instead of rendering ASK_HELLO + the generic buildScopeLine sentence.
describe("buildScopedBrief (NW-56; ADR-020 heading copy)", () => {
  it("names the real supplier in the kicker and the heading", () => {
    expect(buildScopedBrief("Salesforce")).toEqual({
      kicker: "Salesforce",
      heading: "Ask about Salesforce",
      scopeLine: "Answers cite this contract's pages.",
    });
  });

  it("falls back to 'this contract' -- not buildScopedSuggestions' own 'this supplier' -- when the name is null", () => {
    expect(buildScopedBrief(null)).toEqual({
      kicker: "this contract",
      heading: "Ask about this contract",
      scopeLine: "Answers cite this contract's pages.",
    });
  });

  it("falls back the same way for a blank name (still loading, or genuinely unresolved)", () => {
    expect(buildScopedBrief("   ")).toEqual({
      kicker: "this contract",
      heading: "Ask about this contract",
      scopeLine: "Answers cite this contract's pages.",
    });
  });

  it("trims a supplier name carrying incidental whitespace", () => {
    expect(buildScopedBrief("  Salesforce  ").kicker).toBe("Salesforce");
  });

  it("never renders the generic ASK_HELLO as its heading, scoped or not", () => {
    expect(buildScopedBrief("Salesforce").heading).not.toBe(ASK_HELLO);
    expect(buildScopedBrief(null).heading).not.toBe(ASK_HELLO);
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
      listWorkspaces: vi.fn(),
      getWorkspaceMembers: vi.fn(),
      getWorkspaceSettings: vi.fn(),
      updateWorkspaceSettings: vi.fn(),
      revokeInvitation: vi.fn(),
      removeMember: vi.fn(),
      getInvitation: vi.fn(),
      acceptInvitation: vi.fn(),
      acceptPendingInvitation: vi.fn(),
      uploadDocument: vi.fn(),
      getDocument: vi.fn(),
      listDocuments: vi.fn(),
      getDocumentPreviewUrl: vi.fn(),
      reprocessDocument: vi.fn(),
      deleteDocument: vi.fn(),
    deleteAllDocuments: vi.fn(),
      prioritiseDocument: vi.fn(),
      getPortfolio: vi.fn(),
      getContract360: vi.fn(),
      getRenewals: vi.fn(),
      getRenewalPriority: vi.fn(),
      getCorrectionHistory: vi.fn(),
      correctContract: vi.fn(),
      getContractEvidence: vi.fn(),
      getContractStrategy: vi.fn(),
      validateDocument: vi.fn(),
      postRenewalAction: vi.fn(),
      getQuote: vi.fn(),
      getNegotiationSteps: vi.fn(),
      putNegotiationSteps: vi.fn(),
      uploadQuote: vi.fn(),
      getQuoteAssessment: vi.fn(),
      recalculateQuoteAssessment: vi.fn(),
      captureNegotiationOutcome: vi.fn(),
      // Pre-existing gap inherited from task E25/F04/US01/T01 (NW-57, merged ahead of this task):
      // `ApiClient` gained `getQuoteBenchmarkHistory` (client.ts:1488) without this file's own
      // mock keeping up. Fixed here since this file is already this task's own; the same gap in
      // other suites' mockApiClient helpers is untouched -- out of this task's file scope.
      getQuoteBenchmarkHistory: vi.fn(),
      // Task E29/F04/US01/T01 (todo-web): same "sibling task widened ApiClient without this file's
      // mock keeping up" gap this file's own comment above already documents for its neighbour.
      getRenewalNegotiationTodos: vi.fn(),
      tickRenewalNegotiationTodo: vi.fn(),
      askRaffa: vi.fn(),
      getSavingsKpis: vi.fn(),
      getSavingsOpportunities: vi.fn(),
      listConversations: vi.fn(),
      createConversation: vi.fn(),
      getConversation: vi.fn(),
      postMessage: vi.fn(),
    deleteConversation: vi.fn(),
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
    // Task E27/F04/US01/T01 (binding-chip, NW-78): the success result now also carries
    // `scopeContractId`, echoed straight off `created.conversation` (here `null`, matching this
    // test's own unscoped mock response at line ~530) -- see `createConversationAndAsk`'s own doc
    // comment for why the bound-contract chip must read this field rather than the caller's own
    // `scopeContractId` argument.
    expect(result).toEqual({ ok: true, conversationId: "conv-1", reply, scopeContractId: null });
  });

  it("passes scopeContractId through to createConversation when supplied, and echoes the created conversation's own persisted value back on the result (NW-78/AC-2)", async () => {
    const createConversation = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 201,
      conversation: { id: "conv-1", title: "New chat", scopeContractId: "contract-1", updatedAt: "2026-09-08T00:00:00Z" },
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

    const result = await createConversationAndAsk(apiClient, "tenant-1", "When must we give notice to Salesforce?", "contract-1");

    expect(createConversation).toHaveBeenCalledWith("tenant-1", { scopeContractId: "contract-1" });
    expect(result).toEqual({ ok: true, conversationId: "conv-1", reply, scopeContractId: "contract-1" });
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

describe("web research (ADR-030)", () => {
  function webReply(overrides: Partial<ConversationReplyBody> = {}): ConversationReplyBody {
    return {
      conversationId: "conv-1",
      messageId: "msg-web",
      kind: "answer",
      answerMarkdown: "Public, unverified: a 5-10% uplift cap is common [1].",
      citations: [
        { n: 1, corpus: "web", title: "example.com · SaaS renewals", subtitle: null, snippet: "5-10% uplift cap", documentId: null, contractId: null, page: null, section: null, previewUrl: null, href: "https://example.com/a", recordId: null },
      ],
      actions: [],
      provenance: { sources: ["web"], modelId: "gpt-research", promptVersion: "research-v1", inputHash: "h", unverified: true },
      followUps: [],
      ...overrides,
    };
  }

  it("keeps the web corpus as-is", () => {
    expect(toCitationCorpus("web")).toBe("web");
  });

  it("maps a web answer as unverified, from provenance or from a web citation", () => {
    const fromProvenance = mapConversationReplyToReply(webReply());
    expect(fromProvenance.kind).toBe("answer");
    if (fromProvenance.kind === "answer") {
      expect(fromProvenance.unverifiedWeb).toBe(true);
      expect(fromProvenance.citations[0].corpus).toBe("web");
      expect(fromProvenance.citations[0].href).toBe("https://example.com/a");
    }

    const fromCitation = mapConversationReplyToReply(
      webReply({ provenance: { sources: ["web"], modelId: null, promptVersion: null, inputHash: null } }),
    );
    if (fromCitation.kind === "answer") expect(fromCitation.unverifiedWeb).toBe(true);

    const ordinary = mapConversationReplyToReply(
      webReply({ citations: [], provenance: { sources: [], modelId: null, promptVersion: null, inputHash: null } }),
    );
    if (ordinary.kind === "answer") expect(ordinary.unverifiedWeb).toBeUndefined();
  });

  it("opens a web citation externally, and only over https", () => {
    const turn = buildRaffaTurnFromReply("t1", webReply());
    if (turn.role !== "raffa" || turn.reply.kind !== "answer") throw new Error("expected an answer turn");

    expect(resolveCitationOpenAction(turn.reply.citations[0], turn.wireCitations)).toEqual({ kind: "external", url: "https://example.com/a" });
    expect(resolveCitationOpenAction({ ...turn.reply.citations[0], href: "http://example.com/a" }, turn.wireCitations)).toEqual({ kind: "none" });
  });

  it("pendingConsent finds an unanswered consent question and nothing else", () => {
    const consent = buildRaffaTurnFromReply(
      "t2",
      webReply({
        kind: "interview",
        citations: [],
        interview: {
          prompt: "Allow?",
          answered: false,
          questions: [
            {
              key: "web-consent",
              prompt: "Allow?",
              presentation: "consent",
              allowFreeText: false,
              options: [
                { key: "allow", label: "Yes", hint: null },
                { key: "decline", label: "No", hint: null },
              ],
            },
          ],
        },
      }),
    );
    const found = pendingConsent([consent]);
    expect(found?.question.key).toBe("web-consent");
    expect(found?.reply.messageId).toBe("msg-web");

    const choice = buildRaffaTurnFromReply(
      "t3",
      webReply({
        kind: "interview",
        citations: [],
        interview: { prompt: "Which?", answered: false, questions: [{ key: "interpretation", prompt: "Which?", presentation: "choice", allowFreeText: true, options: [] }] },
      }),
    );
    expect(pendingConsent([choice])).toBeNull();
    expect(pendingConsent([consent, choice])).toBeNull();
  });
});
