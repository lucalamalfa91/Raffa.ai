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
  it("answer: renders markdown, citation cards (per-corpus badge), actions and follow-ups", () => {
    renderReply(ANSWER_REPLY);

    expect(screen.getByText("Validated contract")).toBeInTheDocument();
    expect(screen.getByText("Market · representative")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Contract 360 →" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Track it in Renewals" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Where can I push on the Salesforce renewal\?/ })).toBeInTheDocument();
  });

  it("answer: clicking a citation card calls onOpenCitation with that exact citation", async () => {
    const user = userEvent.setup();
    const { onOpenCitation } = renderReply(ANSWER_REPLY);

    await user.click(screen.getByRole("button", { name: /Sales Cloud Enterprise/ }));

    expect(onOpenCitation).toHaveBeenCalledTimes(1);
    expect(onOpenCitation).toHaveBeenCalledWith(ANSWER_REPLY.citations[1]);
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

  it("abstain: renders exactly one accent-left block with the reason -- the only place that block appears", () => {
    const { container } = renderReply({
      kind: "abstain",
      reason: "Salesforce is not validated yet. Finish its review in Documents and ask again.",
    });

    const blocks = container.querySelectorAll(".abstain-block");
    expect(blocks).toHaveLength(1);
    expect(blocks[0].textContent).toContain("Cannot determine reliably.");
    expect(blocks[0].textContent).toContain("Salesforce is not validated yet.");
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
