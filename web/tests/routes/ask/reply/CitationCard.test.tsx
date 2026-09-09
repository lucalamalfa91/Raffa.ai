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
    { corpus: "contigo", badgeText: "Contigo", tagClass: "tag-accent" },
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

  it("renders the first-page preview image when previewUrl is given", () => {
    render(
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
  });

  it("renders the honest placeholder block when there is no previewUrl", () => {
    render(
      <CitationCard
        n={1}
        corpus="tenant"
        title="Salesforce · MSA 2024"
        subtitle="p.12 §8.4"
        snippet="…"
        onOpen={vi.fn()}
      />,
    );

    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(screen.getByText("No page preview available")).toBeInTheDocument();
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
