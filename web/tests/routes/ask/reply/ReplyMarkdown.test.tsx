import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ReplyMarkdown, { splitMarkdownBlocks } from "../../../../src/routes/ask/reply/ReplyMarkdown";
import type { ReplyCitation } from "../../../../src/routes/ask/reply/replyTypes";

function citation(overrides: Partial<ReplyCitation> = {}): ReplyCitation {
  return {
    n: 1,
    corpus: "tenant",
    title: "Salesforce · MSA 2024",
    subtitle: "p.12 §8.4",
    snippet: "automatically renew for successive twelve (12) month periods",
    ...overrides,
  };
}

// Task E13/F09/US01/T02's own "Tests required" row: "unit | markdown safety + [n] links".
describe("splitMarkdownBlocks", () => {
  it("splits blank-line-separated text into paragraph blocks", () => {
    expect(splitMarkdownBlocks("First paragraph.\n\nSecond paragraph.")).toEqual([
      { type: "paragraph", text: "First paragraph." },
      { type: "paragraph", text: "Second paragraph." },
    ]);
  });

  it("treats a block where every line starts with - or * as a list", () => {
    expect(splitMarkdownBlocks("- one\n- two\n* three")).toEqual([{ type: "list", ordered: false, items: ["one", "two", "three"] }]);
  });

  it("does not treat a mixed prose+bullet block as a list", () => {
    expect(splitMarkdownBlocks("Some prose\n- not a list line on its own")).toEqual([
      { type: "paragraph", text: "Some prose\n- not a list line on its own" },
    ]);
  });

  it("treats a block where every line starts with 1) or 1. as an ordered list", () => {
    expect(splitMarkdownBlocks("1) one\n2. two")).toEqual([{ type: "list", ordered: true, items: ["one", "two"] }]);
  });
});

describe("ReplyMarkdown (task E13/F09/US01/T02, AC-3)", () => {
  it("renders blank-line-separated text as separate paragraphs", () => {
    const { container } = render(
      <ReplyMarkdown text={"First paragraph.\n\nSecond paragraph."} citations={[]} onOpenCitation={vi.fn()} />,
    );

    const paragraphs = container.querySelectorAll("p");
    expect(paragraphs).toHaveLength(2);
    expect(paragraphs[0].textContent).toBe("First paragraph.");
    expect(paragraphs[1].textContent).toBe("Second paragraph.");
  });

  it("renders **bold** as <strong>", () => {
    const { container } = render(
      <ReplyMarkdown text="Salesforce ends on **15 January 2027**." citations={[]} onOpenCitation={vi.fn()} />,
    );

    const strong = container.querySelector("strong");
    expect(strong).not.toBeNull();
    expect(strong?.textContent).toBe("15 January 2027");
  });

  it("renders a short list (every line starting with - or *)", () => {
    const { container } = render(
      <ReplyMarkdown text={"- Notice: 90 days\n- Auto-renews: yes"} citations={[]} onOpenCitation={vi.fn()} />,
    );

    const items = container.querySelectorAll("li");
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toBe("Notice: 90 days");
    expect(items[1].textContent).toBe("Auto-renews: yes");
  });

  it("renders a matching [n] as a citation chip that opens the exact same citation object a card would", async () => {
    const user = userEvent.setup();
    const onOpenCitation = vi.fn();
    const salesforceCitation = citation({ n: 2, title: "Sales Cloud Enterprise" });

    render(
      <ReplyMarkdown
        text="Salesforce ends on 15 January 2027 [2]."
        citations={[citation({ n: 1 }), salesforceCitation]}
        onOpenCitation={onOpenCitation}
      />,
    );

    const chip = screen.getByRole("button", { name: "Source 2: Sales Cloud Enterprise" });
    expect(chip).toHaveTextContent(/^2$/);
    await user.click(chip);

    expect(onOpenCitation).toHaveBeenCalledTimes(1);
    expect(onOpenCitation).toHaveBeenCalledWith(salesforceCitation);
  });

  it("previews a chip's source on hover and focus, and Escape dismisses it", async () => {
    const user = userEvent.setup();
    render(<ReplyMarkdown text="Salesforce renews automatically [1]." citations={[citation()]} onOpenCitation={vi.fn()} />);

    // Nothing is duplicated into the transcript until the reader asks for it.
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();

    const chip = screen.getByRole("button", { name: "Source 1: Salesforce · MSA 2024" });
    await user.hover(chip);
    const preview = screen.getByRole("tooltip");
    expect(preview).toHaveTextContent("Validated contract");
    expect(preview).toHaveTextContent("Salesforce · MSA 2024");
    expect(preview).toHaveTextContent("p.12 §8.4");
    expect(preview).toHaveTextContent("automatically renew for successive twelve (12) month periods");
    await user.unhover(chip);
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();

    await user.tab();
    expect(chip).toHaveFocus();
    expect(chip).toHaveAccessibleDescription(/automatically renew/);
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
  });

  it("quotes at most the first 160 characters of a long passage in the preview", async () => {
    const user = userEvent.setup();
    const long = "a".repeat(200);
    render(<ReplyMarkdown text="See [1]." citations={[citation({ snippet: long })]} onOpenCitation={vi.fn()} />);

    await user.hover(screen.getByRole("button", { name: /^Source 1/ }));

    expect(screen.getByRole("tooltip")).toHaveTextContent(`${"a".repeat(160)}…`);
  });

  it("renders an [n] with no matching citation as plain text, never a dangling link", () => {
    const { container } = render(
      <ReplyMarkdown text="See clause [9]." citations={[citation({ n: 1 })]} onOpenCitation={vi.fn()} />,
    );

    expect(screen.queryByRole("link", { name: "[9]" })).not.toBeInTheDocument();
    expect(container.textContent).toContain("[9]");
  });

  it("escapes a raw <script> tag instead of passing it through as HTML (no raw HTML pass-through)", () => {
    const { container } = render(
      <ReplyMarkdown
        text={"<script>alert(1)</script> is not a valid clause."}
        citations={[]}
        onOpenCitation={vi.fn()}
      />,
    );

    expect(container.querySelector("script")).toBeNull();
    expect(container.textContent).toContain("<script>alert(1)</script> is not a valid clause.");
  });

  it("renders 1) numbered lists as an ordered list", () => {
    const { container } = render(
      <ReplyMarkdown text={"1) Notice: 90 days\n2) Auto-renews: yes"} citations={[]} onOpenCitation={vi.fn()} />,
    );

    expect(container.querySelector("ol")).not.toBeNull();
    const items = container.querySelectorAll("li");
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toBe("Notice: 90 days");
  });

  it("never renders a GUID or calc placeholder in the reply prose", () => {
    render(
      <ReplyMarkdown
        text="Score is {calc:criticality[aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa]}."
        citations={[citation()]}
        onOpenCitation={vi.fn()}
      />,
    );

    expect(screen.getByText(/Salesforce · MSA 2024/)).toBeInTheDocument();
    expect(screen.queryByText(/aaaaaaaa-aaaa/)).not.toBeInTheDocument();
    expect(screen.queryByText(/calc:/)).not.toBeInTheDocument();
  });
});
