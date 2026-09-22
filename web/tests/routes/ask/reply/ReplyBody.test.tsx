import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import ReplyBody from "../../../../src/routes/ask/reply/ReplyBody";
import type { Reply } from "../../../../src/routes/ask/reply/replyTypes";

function renderReply(reply: Reply) {
  const onOpenCitation = vi.fn();
  const onFollowUp = vi.fn();
  const { container } = render(
    <MemoryRouter>
      <ReplyBody reply={reply} onOpenCitation={onOpenCitation} onFollowUp={onFollowUp} />
    </MemoryRouter>,
  );
  return { container, onOpenCitation, onFollowUp };
}

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

    await user.click(screen.getByRole("button", { name: "Open source 2" }));

    expect(onOpenCitation).toHaveBeenCalledTimes(1);
    expect(onOpenCitation).toHaveBeenCalledWith(ANSWER_REPLY.citations[1]);
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

  it("shows no banner on an ordinary answer", () => {
    const { container } = renderReply(ANSWER_REPLY);

    expect(container.querySelector(".reply-unverified-banner")).toBeNull();
    expect(container.querySelector("[data-unverified]")).toBeNull();
  });
});
