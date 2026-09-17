import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import CitationCard from "../../../../src/routes/ask/reply/CitationCard";
import type { CitationCorpus } from "../../../../src/routes/ask/reply/replyTypes";

// Task E13/F09/US01/T02's own "Tests required" row: "unit | card variants, actions, layouts per kind".
describe("CitationCard (task E13/F09/US01/T02, AC-3)", () => {
  it.each<{ corpus: CitationCorpus; badgeText: string; tagClass: string }>([
    { corpus: "tenant", badgeText: "Validated contract", tagClass: "tag-neutral" },
    { corpus: "market", badgeText: "Market · representative", tagClass: "tag-outline" },
    { corpus: "raffa", badgeText: "Raffa", tagClass: "tag-accent" },
  ])("renders the $corpus corpus badge", ({ corpus, badgeText, tagClass }) => {
    render(
      <CitationCard
        n={1}
        corpus={corpus}
        title="Salesforce · MSA 2024"
        subtitle="p.12 §8.4"
        snippet="automatically renew for successive twelve (12) month periods"
        onOpen={vi.fn()}
      />,
    );

    const badge = screen.getByText(badgeText);
    expect(badge).toHaveClass("tag", tagClass);
  });

  it("renders the title, subtitle, snippet and index", () => {
    render(
      <CitationCard
        n={2}
        corpus="tenant"
        title="Salesforce · MSA 2024"
        subtitle="p.12 §8.4"
        snippet="automatically renew for successive twelve (12) month periods"
        onOpen={vi.fn()}
      />,
    );

    expect(screen.getByText("Salesforce · MSA 2024")).toBeInTheDocument();
    expect(screen.getByText("p.12 §8.4")).toBeInTheDocument();
    expect(screen.getByText("automatically renew for successive twelve (12) month periods")).toBeInTheDocument();
    expect(screen.getByText("[2]")).toBeInTheDocument();
  });

  it("renders the first-page preview image when previewUrl is given (AC-1)", () => {
    render(
      <CitationCard
        n={1}
        corpus="tenant"
        title="Salesforce · MSA 2024"
        subtitle="p.12 §8.4"
        snippet="automatically renew for successive twelve (12) month periods"
        previewUrl="/api/documents/abc/preview"
        href="/documents/abc/viewer?page=12"
        onOpen={vi.fn()}
      />,
    );

    const img = screen.getByRole("img");
    expect(img).toHaveAttribute("src", "/api/documents/abc/preview");
    expect(screen.getByText("automatically renew for successive twelve (12) month periods")).toBeInTheDocument();
    expect(screen.getByText("Open in document viewer")).toBeInTheDocument();
    expect(screen.queryByText("No page preview available")).not.toBeInTheDocument();
  });

  it.each<{ corpus: CitationCorpus }>([{ corpus: "raffa" }, { corpus: "market" }])(
    "keeps the quote and a CTA, never an empty dashed preview void, for a $corpus citation with no previewUrl (AC-2)",
    ({ corpus }) => {
      render(
        <CitationCard
          n={1}
          corpus={corpus}
          title="Salesforce · MSA 2024"
          subtitle="p.12 §8.4"
          snippet="automatically renew for successive twelve (12) month periods"
          onOpen={vi.fn()}
        />,
      );

      expect(screen.queryByRole("img")).not.toBeInTheDocument();
      expect(screen.queryByText("No page preview available")).not.toBeInTheDocument();
      expect(screen.getByText("automatically renew for successive twelve (12) month periods")).toBeInTheDocument();
      expect(screen.getByText("View source →")).toBeInTheDocument();
      expect(document.querySelector(".citation-card-cta")).toBeNull();
    },
  );

  it("still shows the quote and View source when a tenant citation has no page preview", () => {
    render(
      <CitationCard
        n={1}
        corpus="tenant"
        title="Northwind Traders SA · Msa"
        snippet="This Agreement shall remain in force until 17 September 2027 and shall automatically renew."
        onOpen={vi.fn()}
      />,
    );

    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(
      screen.getByText("This Agreement shall remain in force until 17 September 2027 and shall automatically renew."),
    ).toBeInTheDocument();
    expect(screen.getByText("View source →")).toBeInTheDocument();
    expect(document.querySelector(".citation-card-cta")).toBeNull();
  });

  it("the CTA label is not itself a button or link -- the card keeps exactly one interaction (AC-3)", () => {
    render(
      <CitationCard n={1} corpus="raffa" title="What Ask can do" subtitle="/documents" snippet="…" onOpen={vi.fn()} />,
    );

    // Exactly one `<button>` in the whole card (the outer one); the CTA label is a plain `<span>`.
    expect(screen.getAllByRole("button")).toHaveLength(1);
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("calls onOpen when clicked -- its only interaction", async () => {
    const user = userEvent.setup();
    const onOpen = vi.fn();
    render(
      <CitationCard n={1} corpus="tenant" title="Salesforce · MSA 2024" subtitle="p.12 §8.4" snippet="…" onOpen={onOpen} />,
    );

    await user.click(screen.getByRole("button"));

    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it("never renders a guid anywhere, even when href/documentId-shaped data is passed through", () => {
    const { container } = render(
      <CitationCard
        n={1}
        corpus="tenant"
        title="Salesforce · MSA 2024"
        subtitle="p.12 §8.4"
        snippet="…"
        href="/contracts/3fa85f64-5717-4562-b3fc-2c963f66afa6?clause=8.4"
        onOpen={vi.fn()}
      />,
    );

    expect(container.textContent).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i);
  });
});
