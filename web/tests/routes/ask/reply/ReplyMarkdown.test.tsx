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
    expect(splitMarkdownBlocks("- one\n- two\n* three")).toEqual([{ type: "list", items: ["one", "two", "three"] }]);
  });

  it("does not treat a mixed prose+bullet block as a list", () => {
    expect(splitMarkdownBlocks("Some prose\n- not a list line on its own")).toEqual([
      { type: "paragraph", text: "Some prose\n- not a list line on its own" },
    ]);
  });

  it("returns no blocks for empty text", () => {
    expect(splitMarkdownBlocks("")).toEqual([]);
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

  it("renders a matching [n] as a link that opens the exact same citation object a card would", async () => {
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

    const link = screen.getByRole("link", { name: "[2]" });
    await user.click(link);

    expect(onOpenCitation).toHaveBeenCalledTimes(1);
    expect(onOpenCitation).toHaveBeenCalledWith(salesforceCitation);
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
});
