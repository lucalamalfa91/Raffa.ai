import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import ReplyBody from "../../../../src/routes/ask/reply/ReplyBody";
import type { FeedbackAnswers, Reply, ReplyDraft } from "../../../../src/routes/ask/reply/replyTypes";

type SubmitFeedback = (messageId: string, answers: FeedbackAnswers) => Promise<{ ok: boolean }>;

interface RenderExtra {
  messageId?: string | null;
  feedbackDone?: boolean;
  onSubmitFeedback?: SubmitFeedback;
  onOpenDraft?: (draft: ReplyDraft) => void;
  draftOpen?: boolean;
  showFollowUps?: boolean;
}

function renderReply(reply: Reply, extra: RenderExtra = {}) {
  const onOpenCitation = vi.fn();
  const onFollowUp = vi.fn();
  const { container } = render(
    <MemoryRouter>
      <ReplyBody
        reply={reply}
        onOpenCitation={onOpenCitation}
        onFollowUp={onFollowUp}
        messageId={extra.messageId}
        onSubmitFeedback={extra.onSubmitFeedback}
        feedbackDone={extra.feedbackDone}
        onOpenDraft={extra.onOpenDraft}
        draftOpen={extra.draftOpen}
        showFollowUps={extra.showFollowUps}
      />
    </MemoryRouter>,
  );
  return { container, onOpenCitation, onFollowUp };
}

// ADR-030: a server-localised feedback offer, as `payload.feedbackOffer` carries it.
const FEEDBACK_OFFER = {
  prompt: "Want to report this to the Raffa.ai team so they can build it?",
  yesLabel: "Yes",
  noLabel: "No",
  nextLabel: "Next",
  backLabel: "Back",
  submitLabel: "Send",
  sendingLabel: "Sending…",
  thanksLabel: "Thanks!",
  errorLabel: "I couldn't send the report. Please try again.",
  publicNotice: "Your answers will be public on GitHub.",
  questions: [
    { key: "what" as const, kind: "text" as const, label: "What exactly should Raffa do?", prefill: "Send the email", choices: null },
    { key: "frequency" as const, kind: "choice" as const, label: "How often?", prefill: null, choices: [{ key: "weekly", label: "every week" }] },
    { key: "importance" as const, kind: "choice" as const, label: "How important?", prefill: null, choices: [{ key: "blocking", label: "blocking" }] },
  ],
};

const DRAFT_REPLY: Reply = {
  kind: "draft",
  answerMarkdown: "I can't create or send emails from Raffa.ai yet, but I can help you write the renewal email. Here is a draft.",
  draft: { subject: "Salesforce renewal – request to revise the commercial terms", body: "Dear Salesforce team,\n\nWe are writing about the renewal.\n\nKind regards" },
  gap: { key: "email-draft", title: "Draft and send negotiation emails", language: "en" },
  feedbackOffer: FEEDBACK_OFFER,
  citations: [{ n: 1, corpus: "tenant", title: "Salesforce · MSA 2024", subtitle: "p.12 §8.4", snippet: "auto-renews", href: "/contracts/contract-1" }],
  actions: [
    { label: "Open Renewals →", href: "/renewals?select=contract-1", kind: "primary" },
    { label: "Open Contract 360", href: "/contracts/contract-1", kind: "secondary" },
  ],
  followUps: ["What levers do I have on the Salesforce renewal?"],
};

// requirements.md §6's own JSON example, adapted onto this module's `Reply` type.
const ANSWER_REPLY: Reply = {
  kind: "answer",
  answerMarkdown: "Salesforce ends on **15 January 2027** [1] and the market band supports it [2].",
  citations: [
    {
      n: 1,
      corpus: "tenant",
      title: "Salesforce · MSA 2024",
      subtitle: "p.12 §8.4",
      snippet: "automatically renew for successive twelve (12) month periods",
      previewUrl: "/api/documents/doc-1/preview",
      href: "/contracts/contract-1?clause=8.4",
    },
    {
      n: 2,
      corpus: "market",
      title: "Sales Cloud Enterprise · CH · 500-2 000 employees",
      subtitle: "representative market data · mock feed · updated 2026-09-01",
      snippet: "P25 118 · P50 132 · P75 149 CHF/user/month · n = 214",
    },
  ],
  actions: [
    { label: "Open Contract 360 →", href: "/contracts/contract-1", kind: "primary" },
    { label: "Track it in Renewals", href: "/renewals?select=contract-1", kind: "secondary" },
  ],
  followUps: ["Where can I push on the Salesforce renewal?"],
};

// Task E13/F09/US01/T02's own "Tests required" row: "unit | card variants, actions, layouts per kind".
describe("ReplyBody (task E13/F09/US01/T02, AC-3)", () => {
  it("answer: renders markdown, one evidence card (sections per source), its actions and follow-ups", () => {
    const { container } = renderReply(ANSWER_REPLY);

    // One card for the whole answer, never one per citation (EvidenceCard.tsx).
    expect(container.querySelectorAll(".citation-card")).toHaveLength(1);
    expect(screen.getByText("Your contracts")).toBeInTheDocument();
    expect(screen.getByText("Market · representative")).toBeInTheDocument();
    expect(container.querySelector(".evidence-group-title")).toHaveTextContent("Salesforce");
    // The reply's actions live in the card's single action row.
    expect(screen.getByRole("link", { name: "Open Contract 360 →" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Track it in Renewals" })).toBeInTheDocument();
    expect(container.querySelectorAll(".reply-actions")).toHaveLength(1);
    expect(screen.getByRole("button", { name: /Where can I push on the Salesforce renewal\?/ })).toBeInTheDocument();
  });

  it("answer: clicking a citation row calls onOpenCitation with that exact citation", async () => {
    const user = userEvent.setup();
    const { onOpenCitation } = renderReply(ANSWER_REPLY);

    await user.click(screen.getByRole("button", { name: /^2 sources/ }));
    await user.click(screen.getByRole("button", { name: "Open source 2" }));

    expect(onOpenCitation).toHaveBeenCalledTimes(1);
    expect(onOpenCitation).toHaveBeenCalledWith(ANSWER_REPLY.citations[1]);
  });

  it("answer: an inline [n] chip opens the same citation the sources list does", async () => {
    const user = userEvent.setup();
    const { onOpenCitation } = renderReply(ANSWER_REPLY);

    await user.click(screen.getByRole("button", { name: "Source 2: Sales Cloud Enterprise · CH · 500-2 000 employees" }));

    expect(onOpenCitation).toHaveBeenCalledWith(ANSWER_REPLY.citations[1]);
  });

  it("answer: follow-ups render only on the latest turn (showFollowUps)", () => {
    const { container } = renderReply(ANSWER_REPLY, { showFollowUps: false });

    expect(container.querySelector(".reply-followups")).toBeNull();
    expect(screen.queryByRole("button", { name: /Where can I push on the Salesforce renewal\?/ })).not.toBeInTheDocument();
  });

  it("answer: with no citations, the actions still render as a plain row", () => {
    const { container } = renderReply({ ...ANSWER_REPLY, citations: [] });

    expect(container.querySelector(".citation-card")).toBeNull();
    expect(screen.getByRole("link", { name: "Open Contract 360 →" })).toBeInTheDocument();
  });

  it("answer: clicking a follow-up calls onFollowUp with its question", async () => {
    const user = userEvent.setup();
    const { onFollowUp } = renderReply(ANSWER_REPLY);

    await user.click(screen.getByRole("button", { name: /Where can I push on the Salesforce renewal\?/ }));

    expect(onFollowUp).toHaveBeenCalledTimes(1);
    expect(onFollowUp).toHaveBeenCalledWith("Where can I push on the Salesforce renewal?");
  });

  it("answer: never renders a route line or a guid anywhere", () => {
    const { container } = renderReply(ANSWER_REPLY);

    expect(container.textContent).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i);
    expect(container.textContent).not.toContain("Structured query");
  });

  it("redirect: renders warm prose and one CTA, never the abstain block", () => {
    const { container } = renderReply({
      kind: "redirect",
      answerMarkdown: "Ask needs at least one validated contract.",
      actions: [{ label: "Upload a contract", href: "/documents", kind: "primary" }],
    });

    expect(screen.getByText("Ask needs at least one validated contract.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Upload a contract" })).toBeInTheDocument();
    expect(container.querySelector(".abstain-block")).toBeNull();
  });

  it("redirect: renders only the first CTA even if the reply carried more than one", () => {
    renderReply({
      kind: "redirect",
      answerMarkdown: "Hi! Ask me about your contracts.",
      actions: [
        { label: "Upload a contract", href: "/documents", kind: "primary" },
        { label: "Go to Documents", href: "/documents", kind: "secondary" },
      ],
    });

    expect(screen.getAllByRole("link")).toHaveLength(1);
  });

  it("refusal: renders warm prose and one CTA, never the abstain block", () => {
    const { container } = renderReply({
      kind: "refusal",
      answerMarkdown: "I can't give legal advice, but I can tell you whether the liability cap sits above the market band.",
      actions: [{ label: "Open Contract 360 →", href: "/contracts/contract-1", kind: "primary" }],
    });

    expect(container.querySelector(".abstain-block")).toBeNull();
    expect(screen.getByRole("link", { name: "Open Contract 360 →" })).toBeInTheDocument();
  });

  it("abstain: renders the server's next-step questions, and clicking one asks it", async () => {
    const user = userEvent.setup();
    const { container, onFollowUp } = renderReply({
      kind: "abstain",
      reason: "Here's how to prepare the renewal: name the supplier and I'll draft the email.",
      followUps: ["Which contracts are most critical?", "Where can we save?"],
    });

    expect(container.querySelector('[data-reply-kind="abstain"]')).not.toBeNull();
    expect(container.querySelectorAll(".reply-followup")).toHaveLength(2);

    await user.click(screen.getByRole("button", { name: /Where can we save\?/ }));

    expect(onFollowUp).toHaveBeenCalledTimes(1);
    expect(onFollowUp).toHaveBeenCalledWith("Where can we save?");
  });

  it("abstain: renders the server's proposal as plain reply prose -- never the 'I don't have data I trust' banner", () => {
    const { container } = renderReply({
      kind: "abstain",
      reason:
        "Here's how to prepare the renewal:\n\n" +
        "- **Pin the deadline**: Renewals shows when to move on each contract.\n" +
        "- **Write to the supplier**: name the supplier and I'll draft a ready-to-send email.",
    });

    expect(container.querySelector(".abstain-block")).toBeNull();
    expect(container.textContent).not.toContain("I don't have data I trust");
    expect(container.textContent).toContain("Here's how to prepare the renewal:");
    // Markdown, like an answer: a bullet list with bold lead-ins.
    expect(container.querySelectorAll('[data-reply-kind="abstain"] li')).toHaveLength(2);
    expect(screen.getByText("Pin the deadline").tagName).toBe("STRONG");
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("error: renders the existing .error-state, not the abstain block", () => {
    const { container } = renderReply({
      kind: "error",
      reason: "Raffa.ai's Q&A service is temporarily unavailable. Try again in a moment.",
    });

    expect(container.querySelector(".error-state")).not.toBeNull();
    expect(container.querySelector(".abstain-block")).toBeNull();
    expect(screen.getByRole("alert")).toHaveTextContent("Raffa.ai's Q&A service is temporarily unavailable. Try again in a moment.");
  });
});

// ADR-030: the interview -- one clarifying question with chips, answered by key.
describe("ReplyBody interview (ADR-030)", () => {
  const INTERVIEW: Reply = {
    kind: "interview",
    prompt: "Before I answer, one quick check.",
    answered: false,
    messageId: "msg-7",
    questions: [
      {
        key: "interpretation",
        prompt: "Which of these do you mean?",
        presentation: "choice",
        allowFreeText: true,
        options: [
          { key: "portfolio-overview", label: "The most critical contracts and where we can save", hint: null },
          { key: "total-spend", label: "Our total annual spend across contracts", hint: null },
          { key: "web-research", label: "Search the public web for market practice (asks first)", hint: "Nothing from your contracts leaves Raffa." },
        ],
      },
    ],
  };

  it("renders the lead-in, the question and one chip per option, plus the free-text hint", () => {
    const { container } = renderReply(INTERVIEW);

    expect(container.querySelector('[data-reply-kind="interview"]')).not.toBeNull();
    expect(screen.getByText("Before I answer, one quick check.")).toBeInTheDocument();
    expect(screen.getByText("Which of these do you mean?")).toBeInTheDocument();
    expect(screen.getAllByRole("button")).toHaveLength(3);
    expect(screen.getByRole("button", { name: /Search the public web/ })).toHaveAttribute("title", "Nothing from your contracts leaves Raffa.");
    expect(screen.getByText("Or just type your answer below.")).toBeInTheDocument();
    expect(container.querySelector(".abstain-block")).toBeNull();
    expect(container.querySelector(".citation-card")).toBeNull();
  });

  it("clicking a chip reports the question key and the option -- never a label lookup", async () => {
    const user = userEvent.setup();
    const onInterviewOption = vi.fn();
    render(
      <MemoryRouter>
        <ReplyBody reply={INTERVIEW} onOpenCitation={vi.fn()} onFollowUp={vi.fn()} onInterviewOption={onInterviewOption} />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("button", { name: /total annual spend/ }));

    expect(onInterviewOption).toHaveBeenCalledTimes(1);
    const [reply, questionKey, option] = onInterviewOption.mock.calls[0];
    expect(reply).toBe(INTERVIEW);
    expect(questionKey).toBe("interpretation");
    expect(option.key).toBe("total-spend");
  });

  it("an answered interview keeps its chips visible but disabled, and drops the free-text hint", () => {
    const { container } = renderReply({ ...INTERVIEW, answered: true });

    expect(container.querySelector('.reply-interview[data-answered="true"]')).not.toBeNull();
    expect(screen.getAllByRole("button").every((button) => (button as HTMLButtonElement).disabled)).toBe(true);
    expect(screen.queryByText("Or just type your answer below.")).toBeNull();
  });
});

// Task E25/F05/US02/T01 (abstain-recovery-web; parent story us-02-abstain-recovery-web
// AC-1/AC-2/AC-3; ADR-024 "every abstain has a clickable next step", ADR-019 native link). The
// abstain test above (AC-3) predates the recovery action and already proves the no-`actions`-field
// shape renders no link; these prove the two shapes
// `askViewModel.ts#buildReply` can now additionally produce once it maps `turn.actions`.
describe("ReplyBody abstain recovery action (task E25/F05/US02/T01)", () => {
  it("AC-1/AC-2: renders the recovery action as a secondary ActionRow -- never primary, even when the mapper marked it primary", () => {
    const { container } = renderReply({
      kind: "abstain",
      reason: "Nothing in the 2 validated contract(s) supports a reliable answer.",
      // `askViewModel.ts#buildReply` maps this through the same `mapConversationAction` the
      // answer/redirect/refusal branches use, which marks the first (and only) action "primary"
      // (`toReplyActionKind(0)`, see `mapConversationAction` test above) -- this proves ReplyBody
      // itself, not the mapper, enforces "never primary" for abstain (ADR-024).
      actions: [{ label: "Upload a contract", href: "/documents", kind: "primary" }],
    });

    expect(container.querySelector('[data-reply-kind="abstain"]')).not.toBeNull();
    const action = screen.getByRole("link", { name: "Upload a contract" });
    expect(action).toHaveAttribute("href", "/documents");
    expect(action).toHaveClass("btn-secondary");
    expect(action).not.toHaveClass("btn-primary");
  });

  it("AC-3: an abstain with an explicit empty actions array still renders its prose, never an empty screen", () => {
    const { container } = renderReply({
      kind: "abstain",
      reason: "Let's start from your first contract: upload it in Documents.",
      actions: [],
    });

    const body = container.querySelector('[data-reply-kind="abstain"]');
    expect(body).not.toBeNull();
    expect(body?.textContent).toContain("Let's start from your first contract: upload it in Documents.");
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });
});

// ADR-030 D2/D5/D6: the fifth kind, the capability-gap redirect and the external action.
describe("ReplyBody (ADR-030)", () => {
  it("draft: renders preface, the email as an artifact card (never inline), cards, actions, follow-ups and the feedback card", () => {
    const { container } = renderReply(DRAFT_REPLY, { messageId: "msg-1", onSubmitFeedback: vi.fn(), onOpenDraft: vi.fn() });

    expect(container.querySelector('[data-reply-kind="draft"]')).not.toBeNull();
    expect(screen.getByText(/I can't create or send emails from Raffa.ai yet/)).toBeInTheDocument();
    // The email itself lives in the side panel; the thread only carries its one-line card.
    const card = screen.getByRole("button", { name: "Open draft email: Salesforce renewal – request to revise the commercial terms" });
    expect(card).toHaveAttribute("aria-expanded", "false");
    expect(card).toHaveTextContent("Draft email · Click to open");
    expect(container.textContent).not.toContain("We are writing about the renewal.");
    expect(screen.queryByRole("button", { name: "Copy email" })).not.toBeInTheDocument();
    expect(screen.getByText("Your contracts")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Renewals →" })).toHaveAttribute("href", "/renewals?select=contract-1");
    expect(screen.getByRole("button", { name: /What levers do I have/ })).toBeInTheDocument();
    expect(screen.getByText(FEEDBACK_OFFER.prompt)).toBeInTheDocument();
    expect(container.querySelector(".abstain-block")).toBeNull();
  });

  it("draft: clicking the card asks the screen to open this draft; an open draft reads as open", async () => {
    const user = userEvent.setup();
    const onOpenDraft = vi.fn();
    renderReply(DRAFT_REPLY, { onOpenDraft, draftOpen: true });

    const card = screen.getByRole("button", { name: /^Open draft email:/ });
    expect(card).toHaveAttribute("aria-expanded", "true");
    expect(card).toHaveTextContent("Draft email · Open");
    await user.click(card);

    expect(onOpenDraft).toHaveBeenCalledWith(DRAFT_REPLY.kind === "draft" ? DRAFT_REPLY.draft : null);
  });

  it("draft: without a screen to open it in, the card is a static summary, not a dead button", () => {
    const { container } = renderReply(DRAFT_REPLY);

    expect(screen.queryByRole("button", { name: /^Open draft email:/ })).not.toBeInTheDocument();
    expect(container.querySelector(".artifact-card")).toHaveTextContent("Salesforce renewal – request to revise the commercial terms");
  });

  it("draft: hides the feedback card once the offer was answered, or without a server message id", () => {
    const done = renderReply(DRAFT_REPLY, { messageId: "msg-1", onSubmitFeedback: vi.fn(), feedbackDone: true });
    expect(done.container.querySelector(".reply-feedback")).toBeNull();

    const noId = renderReply(DRAFT_REPLY, { messageId: null, onSubmitFeedback: vi.fn() });
    expect(noId.container.querySelector(".reply-feedback")).toBeNull();
  });

  it("draft: the feedback card submits against this turn's message id", async () => {
    const onSubmitFeedback = vi.fn().mockResolvedValue({ ok: true });
    const user = userEvent.setup();
    renderReply(DRAFT_REPLY, { messageId: "msg-1", onSubmitFeedback });

    await user.click(screen.getByRole("button", { name: "Yes" }));
    await user.click(screen.getByRole("button", { name: "Next" }));
    await user.click(screen.getByRole("button", { name: "every week" }));
    await user.click(screen.getByRole("button", { name: "Next" }));
    await user.click(screen.getByRole("button", { name: "blocking" }));
    await user.click(screen.getByRole("button", { name: "Send" }));

    expect(onSubmitFeedback).toHaveBeenCalledWith("msg-1", { what: "Send the email", frequency: "weekly", importance: "blocking" });
  });

  it("redirect with a feedback offer renders follow-up chips and the card, still one CTA", () => {
    renderReply(
      {
        kind: "redirect",
        answerMarkdown: "I can't create or send emails from Raffa.ai yet, but I can help you write the renewal email. Tell me which contract it is for.",
        actions: [{ label: "Open Portfolio →", href: "/contracts", kind: "primary" }],
        followUps: ["Write the renewal email for Salesforce", "Write the renewal email for DocuSign"],
        gap: { key: "email-draft", title: "Draft and send negotiation emails", language: "en" },
        feedbackOffer: FEEDBACK_OFFER,
      },
      { messageId: "msg-2", onSubmitFeedback: vi.fn() },
    );

    expect(screen.getAllByRole("link")).toHaveLength(1);
    expect(screen.getByRole("button", { name: /Write the renewal email for DocuSign/ })).toBeInTheDocument();
    expect(screen.getByText(FEEDBACK_OFFER.prompt)).toBeInTheDocument();
  });

  it("answer with an external action renders a new-tab anchor with rel noopener, never a router link", () => {
    renderReply({
      kind: "answer",
      answerMarkdown: "Thanks, I opened issue #42 for the Raffa.ai team.",
      citations: [],
      actions: [{ label: "Open issue #42 →", href: "https://github.com/lucalamalfa91/Raffa.ai/issues/42", kind: "primary", external: true }],
      followUps: [],
      feedbackResult: { forMessageId: "msg-1", status: "issue_opened", issueNumber: 42, issueUrl: "https://github.com/lucalamalfa91/Raffa.ai/issues/42" },
    });

    const anchor = screen.getByRole("link", { name: "Open issue #42 →" });
    expect(anchor).toHaveAttribute("href", "https://github.com/lucalamalfa91/Raffa.ai/issues/42");
    expect(anchor).toHaveAttribute("target", "_blank");
    expect(anchor).toHaveAttribute("rel", "noopener noreferrer");
  });
});

describe("ReplyBody web research answer (ADR-030)", () => {
  it("shows the unverified banner and the web section for a web answer", () => {
    const { container } = renderReply({
      kind: "answer",
      answerMarkdown: "Public, unverified: a 5-10% uplift cap is common [1].",
      citations: [{ n: 1, corpus: "web", title: "example.com · SaaS renewals", subtitle: null, snippet: "5-10% uplift cap", href: "https://example.com/a" }],
      actions: [],
      followUps: [],
      unverifiedWeb: true,
    });

    expect(container.querySelector('[data-reply-kind="answer"][data-unverified="true"]')).not.toBeNull();
    expect(screen.getByRole("note")).toHaveTextContent("Public web · not verified.");
    expect(container.querySelector('[data-section="web"]')).not.toBeNull();
  });

  it("says only the web part is unverified when a web-search answer also cites the contracts (ADR-031)", () => {
    const { container } = renderReply({
      kind: "answer",
      answerMarkdown: "**From your contracts and Raffa's data**\n\nYour cap is 5% [1].\n\n**From the public web · unverified**\n\nPublic, unverified: 5-10% is common [2].",
      citations: [
        { n: 1, corpus: "tenant", title: "Salesforce MSA", subtitle: null, snippet: "uplift capped at 5%" },
        { n: 2, corpus: "web", title: "example.com · SaaS renewals", subtitle: null, snippet: "5-10% uplift cap", href: "https://example.com/a" },
      ],
      actions: [],
      followUps: [],
      unverifiedWeb: true,
    });

    expect(screen.getByRole("note")).toHaveTextContent("Includes public web findings · not verified.");
    expect(screen.getByRole("note")).toHaveTextContent("The web part comes from public sources");
    expect(container.querySelector('[data-section="web"]')).not.toBeNull();
  });

  it("shows no banner on an ordinary answer", () => {
    const { container } = renderReply(ANSWER_REPLY);

    expect(container.querySelector(".reply-unverified-banner")).toBeNull();
    expect(container.querySelector("[data-unverified]")).toBeNull();
  });
});
