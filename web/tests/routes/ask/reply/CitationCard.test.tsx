import type { ReactElement } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import CitationCard from "../../../../src/routes/ask/reply/CitationCard";
import type { CitationCorpus } from "../../../../src/routes/ask/reply/replyTypes";

// The two-CTA branch (task E28/F03/US02/T01) reuses `ActionRow`, which renders real
// `react-router-dom` `<Link>`s -- every render needs a Router context, the same
// `ActionRow.test.tsx` convention this file now follows too. Harmless for the single-button
// branch, which never touches routing.
function renderCard(element: ReactElement) {
  return render(<MemoryRouter>{element}</MemoryRouter>);
}

// Task E13/F09/US01/T02's own "Tests required" row: "unit | card variants, actions, layouts per kind".
describe("CitationCard (task E13/F09/US01/T02, AC-3)", () => {
  it.each<{ corpus: CitationCorpus; badgeText: string; tagClass: string }>([
    { corpus: "tenant", badgeText: "Validated contract", tagClass: "tag-neutral" },
    { corpus: "market", badgeText: "Market · representative", tagClass: "tag-outline" },
    { corpus: "raffa", badgeText: "Raffa", tagClass: "tag-accent" },
  ])("renders the $corpus corpus badge", ({ corpus, badgeText, tagClass }) => {
    renderCard(
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
    renderCard(
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
    renderCard(
      <CitationCard
        n={1}
        corpus="tenant"
        title="Salesforce · MSA 2024"
        subtitle="p.12 §8.4"
        snippet="…"
        previewUrl="/api/documents/abc/preview"
        onOpen={vi.fn()}
      />,
    );

    const img = screen.getByRole("img");
    expect(img).toHaveAttribute("src", "/api/documents/abc/preview");
    expect(screen.queryByText("No page preview available")).not.toBeInTheDocument();
    expect(screen.queryByText("View source →")).not.toBeInTheDocument();
  });

  // Task E25/F02/US02/T01 (us-02-citation-card-web AC-2, closing NW-55): a `raffa`/`market`
  // citation never carries a `previewUrl` (ADR-024 §2) and used to fall through to this same
  // component's old "No page preview available" placeholder -- it now renders a `.btn`-styled CTA
  // card instead, and the placeholder text is gone for good, not just for these two corpora.
  it.each<{ corpus: CitationCorpus }>([{ corpus: "raffa" }, { corpus: "market" }])(
    "renders a CTA card, never the old placeholder, for a $corpus citation with no previewUrl (AC-2)",
    ({ corpus }) => {
      renderCard(
        <CitationCard
          n={1}
          corpus={corpus}
          title="Salesforce · MSA 2024"
          subtitle="p.12 §8.4"
          snippet="…"
          onOpen={vi.fn()}
        />,
      );

      expect(screen.queryByRole("img")).not.toBeInTheDocument();
      expect(screen.queryByText("No page preview available")).not.toBeInTheDocument();
      expect(screen.getByText("View source →")).toBeInTheDocument();
    },
  );

  it("the CTA label is not itself a button or link -- the card keeps exactly one interaction (AC-3)", () => {
    renderCard(
      <CitationCard n={1} corpus="raffa" title="What Ask can do" subtitle="/documents" snippet="…" onOpen={vi.fn()} />,
    );

    // Exactly one `<button>` in the whole card (the outer one); the CTA label is a plain `<span>`.
    expect(screen.getAllByRole("button")).toHaveLength(1);
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("calls onOpen when clicked -- its only interaction", async () => {
    const user = userEvent.setup();
    const onOpen = vi.fn();
    renderCard(
      <CitationCard n={1} corpus="tenant" title="Salesforce · MSA 2024" subtitle="p.12 §8.4" snippet="…" onOpen={onOpen} />,
    );

    await user.click(screen.getByRole("button"));

    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it("never renders a guid anywhere, even when href/documentId-shaped data is passed through", () => {
    const { container } = renderCard(
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

  // Task E28/F03/US02/T01 (NW-83/NW-93; parent story us-02-citation-two-cta AC-1/AC-2/AC-3;
  // screens-v2.md §2 citation card actions): once a citation resolves to a real document
  // page/span (its own `href` is already the W18 viewer deep-link), the card offers both the 360
  // CTA and the viewer-at-span CTA instead of the single-button fallback above.
  describe("two-CTA card (NW-83/NW-93)", () => {
    it("renders both CTAs, the viewer href carrying ?page= and &clause=, when the citation carries a clause/span id", () => {
      renderCard(
        <CitationCard
          n={1}
          corpus="tenant"
          title="Salesforce · MSA 2024"
          subtitle="p.12 §8.4"
          snippet="the notice period is twelve (12) months"
          href="/documents/doc-1/viewer?page=12&clause=clause-1"
          contractId="contract-1"
          onOpen={vi.fn()}
        />,
      );

      const primary = screen.getByRole("link", { name: "Open contract" });
      expect(primary).toHaveClass("btn", "btn-primary");
      expect(primary).toHaveAttribute("href", "/contracts/contract-1");

      const secondary = screen.getByRole("link", { name: "Open at this span" });
      expect(secondary).toHaveClass("btn", "btn-secondary");
      const viewerHref = secondary.getAttribute("href");
      expect(viewerHref).toBe("/documents/doc-1/viewer?page=12&clause=clause-1");
      expect(viewerHref).toMatch(/\?page=/);
      expect(viewerHref).toMatch(/&clause=/);
    });

    it("keeps the preview image alongside the two actions -- never previewUrl-only", () => {
      renderCard(
        <CitationCard
          n={1}
          corpus="tenant"
          title="Salesforce · MSA 2024"
          subtitle="p.12 §8.4"
          snippet="…"
          previewUrl="/api/documents/doc-1/preview"
          href="/documents/doc-1/viewer?page=12&clause=clause-1"
          contractId="contract-1"
          onOpen={vi.fn()}
        />,
      );

      expect(screen.getByRole("img")).toHaveAttribute("src", "/api/documents/doc-1/preview");
      expect(screen.getAllByRole("link")).toHaveLength(2);
    });

    it("never nests the two actions inside a native button (never two nested buttons)", () => {
      renderCard(
        <CitationCard
          n={1}
          corpus="tenant"
          title="Salesforce · MSA 2024"
          subtitle="p.12 §8.4"
          snippet="…"
          href="/documents/doc-1/viewer?page=12&clause=clause-1"
          contractId="contract-1"
          onOpen={vi.fn()}
        />,
      );

      expect(screen.queryByRole("button")).not.toBeInTheDocument();
      expect(screen.getAllByRole("link")).toHaveLength(2);
    });

    it("falls back to the single 360 CTA when no clause/span resolves -- the 360 ?clause=/?page= half, never viewer-only", () => {
      renderCard(
        <CitationCard
          n={1}
          corpus="tenant"
          title="Salesforce · MSA 2024"
          subtitle="p.12"
          snippet="…"
          href="/contracts/contract-1?page=3"
          contractId="contract-1"
          onOpen={vi.fn()}
        />,
      );

      expect(screen.queryByRole("link")).not.toBeInTheDocument();
      expect(screen.getAllByRole("button")).toHaveLength(1);
    });

    it("falls back to the single CTA when a viewer href resolves with no known contract (never a dangling 'Open contract' link)", () => {
      renderCard(
        <CitationCard
          n={1}
          corpus="tenant"
          title="Document excerpt"
          subtitle="p.4"
          snippet="…"
          href="/documents/doc-2/viewer?page=4"
          onOpen={vi.fn()}
        />,
      );

      expect(screen.queryByRole("link")).not.toBeInTheDocument();
      expect(screen.getAllByRole("button")).toHaveLength(1);
    });

    it("never renders a guid as visible text in the two-CTA card either", () => {
      const { container } = renderCard(
        <CitationCard
          n={1}
          corpus="tenant"
          title="Salesforce · MSA 2024"
          subtitle="p.12 §8.4"
          snippet="…"
          href="/documents/3fa85f64-5717-4562-b3fc-2c963f66afa6/viewer?page=12&clause=8c9e6b1a-2222-4444-8888-abcdefabcdef"
          contractId="3fa85f64-5717-4562-b3fc-2c963f66afa6"
          onOpen={vi.fn()}
        />,
      );

      expect(container.textContent).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i);
    });
  });
});
