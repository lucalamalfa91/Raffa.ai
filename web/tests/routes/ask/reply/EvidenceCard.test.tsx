import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import EvidenceCard, { SNIPPET_COLLAPSE_LENGTH } from "../../../../src/routes/ask/reply/EvidenceCard";
import type { ReplyAction, ReplyCitation } from "../../../../src/routes/ask/reply/replyTypes";

const SALESFORCE = "11111111-1111-1111-1111-111111111111";
const MICROSOFT = "22222222-2222-2222-2222-222222222222";

function citation(overrides: Partial<ReplyCitation> & Pick<ReplyCitation, "n" | "title">): ReplyCitation {
  return {
    corpus: "tenant",
    subtitle: "",
    snippet: `${overrides.title} ends on 2027-01-09, auto-renews unless notice is given.`,
    href: null,
    contractId: null,
    ...overrides,
  };
}

const TWO_CONTRACTS: ReplyCitation[] = [
  citation({ n: 1, title: "Salesforce · MSA", contractId: SALESFORCE, href: `/contracts/${SALESFORCE}` }),
  citation({ n: 2, title: "Microsoft · OrderForm", contractId: MICROSOFT, href: `/contracts/${MICROSOFT}` }),
  citation({ n: 3, title: "Contracts matching your renewal window", corpus: "calc", snippet: "2 contract(s) auto-renew inside the window." }),
];

const BACKEND_ACTIONS: ReplyAction[] = [
  { label: "Portfolio →", href: "/contracts", kind: "primary" },
  { label: "Renewals →", href: "/renewals", kind: "secondary" },
];

/** Renders the card and, unless `collapsed`, opens its sources row -- most tests are about the
 * detail behind it. */
function renderCard(citations: readonly ReplyCitation[], actions: readonly ReplyAction[] = BACKEND_ACTIONS, onOpen = vi.fn(), collapsed = false) {
  const view = render(
    <MemoryRouter>
      <EvidenceCard citations={citations} actions={actions} onOpenCitation={onOpen} />
    </MemoryRouter>,
  );
  const toggle = view.container.querySelector<HTMLButtonElement>(".evidence-toggle");
  if (toggle && !collapsed) fireEvent.click(toggle);
  return { ...view, onOpen };
}

describe("EvidenceCard", () => {
  it("starts collapsed to one sources row; the actions stay visible under it", async () => {
    const user = userEvent.setup();
    const { container } = renderCard(TWO_CONTRACTS, BACKEND_ACTIONS, vi.fn(), true);

    const toggle = screen.getByRole("button", { name: "3 sources: Salesforce, Microsoft · 2 contracts · 1 Raffa item" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    const body = container.querySelector(".evidence-body");
    expect(body).not.toBeVisible();
    expect(screen.queryByRole("button", { name: "Open source 1" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Portfolio · these 2 contracts →" })).toBeVisible();

    await user.click(toggle);

    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(body).toBeVisible();
    expect(screen.getByRole("button", { name: "Open source 1" })).toBeInTheDocument();
  });

  it("counts a single source in the singular", () => {
    renderCard([TWO_CONTRACTS[0]], BACKEND_ACTIONS, vi.fn(), true);

    expect(screen.getByRole("button", { name: "1 source: Salesforce · 1 contract" })).toBeInTheDocument();
  });

  it("renders exactly one card that names every supplier the answer cites", () => {
    const { container } = renderCard(TWO_CONTRACTS);

    expect(container.querySelectorAll(".citation-card")).toHaveLength(1);
    expect(container.querySelector(".citation-card-title")).toHaveTextContent("Salesforce, Microsoft");
    expect(container.querySelector(".citation-card-subtitle")).toHaveTextContent("2 contracts · 1 Raffa item");
    expect(screen.getByText("Salesforce")).toBeInTheDocument();
    expect(screen.getByText("Microsoft")).toBeInTheDocument();
    // The row adds only what the supplier heading does not already say.
    expect(screen.getByText("MSA")).toBeInTheDocument();
    expect(screen.getByText("OrderForm")).toBeInTheDocument();
  });

  it("puts the aggregate under Raffa without a dead 'View source' control", () => {
    const { container } = renderCard(TWO_CONTRACTS);

    const raffa = container.querySelector('[data-section="raffa"]');
    expect(raffa).not.toBeNull();
    expect(within(raffa as HTMLElement).getByText("Contracts matching your renewal window")).toBeInTheDocument();
    expect(within(raffa as HTMLElement).queryByRole("button")).toBeNull();
    expect(screen.queryByText("View source →")).toBeNull();
  });

  it("offers one action row: Portfolio filtered to these contracts first, then the backend's, three at most", () => {
    const { container } = renderCard(TWO_CONTRACTS, [...BACKEND_ACTIONS, { label: "Savings →", href: "/savings", kind: "secondary" }, { label: "Quote check →", href: "/quotes", kind: "secondary" }]);

    const links = Array.from(container.querySelectorAll(".reply-actions a")).map((node) => ({
      text: node.textContent,
      href: node.getAttribute("href"),
    }));
    expect(links).toEqual([
      { text: "Portfolio · these 2 contracts →", href: `/contracts?ids=${SALESFORCE},${MICROSOFT}` },
      { text: "Renewals →", href: "/renewals" },
      { text: "Savings →", href: "/savings" },
    ]);
  });

  it("links each supplier group straight to its Contract 360 when several contracts are cited", () => {
    renderCard(TWO_CONTRACTS);

    const links = screen.getAllByRole("link", { name: "Contract 360 →" });
    expect(links.map((link) => link.getAttribute("href"))).toEqual([`/contracts/${SALESFORCE}`, `/contracts/${MICROSOFT}`]);
  });

  it("with a single contract, the footer's primary action is that contract's 360 and the heading carries no duplicate link", () => {
    const { container } = renderCard([TWO_CONTRACTS[0]], BACKEND_ACTIONS);

    expect(screen.queryByRole("link", { name: "Contract 360 →" })).toBeNull();
    const primary = container.querySelector(".reply-actions a.btn-primary");
    expect(primary).toHaveTextContent("Salesforce · Contract 360 →");
    expect(primary).toHaveAttribute("href", `/contracts/${SALESFORCE}`);
  });

  it("opens a row through the same callback the inline [n] markers use", async () => {
    const user = userEvent.setup();
    const { onOpen } = renderCard(TWO_CONTRACTS);

    await user.click(screen.getByRole("button", { name: "Open source 2" }));

    expect(onOpen).toHaveBeenCalledTimes(1);
    expect(onOpen.mock.calls[0][0]).toMatchObject({ n: 2, contractId: MICROSOFT });
  });

  it("collapses a long snippet and expands it on More", async () => {
    const user = userEvent.setup();
    const long = "x".repeat(SNIPPET_COLLAPSE_LENGTH + 1);
    const { container } = renderCard([citation({ n: 1, title: "Salesforce · Liability clause", snippet: long, contractId: SALESFORCE, href: `/contracts/${SALESFORCE}` })]);

    const snippet = container.querySelector(".evidence-row-snippet");
    expect(snippet).toHaveAttribute("data-collapsed", "true");
    await user.click(screen.getByRole("button", { name: "More" }));
    expect(snippet).toHaveAttribute("data-collapsed", "false");
    expect(screen.getByRole("button", { name: "Less" })).toBeInTheDocument();
  });

  it("offers 'Open at this span' for a clause with a viewer deep-link", () => {
    renderCard([
      citation({
        n: 1,
        title: "Salesforce · Liability clause",
        subtitle: "p.12 §8.4",
        contractId: SALESFORCE,
        href: `/documents/44444444-4444-4444-4444-444444444444/viewer?page=12`,
      }),
    ]);

    expect(screen.getByRole("link", { name: "Open at this span" })).toHaveAttribute(
      "href",
      "/documents/44444444-4444-4444-4444-444444444444/viewer?page=12",
    );
    expect(screen.getByText("p.12 §8.4")).toBeInTheDocument();
  });

  it("never prints a contract id as text", () => {
    const { container } = renderCard(TWO_CONTRACTS);

    expect(container.textContent).not.toContain(SALESFORCE);
    expect(container.textContent).not.toContain(MICROSOFT);
  });

  it("renders nothing for an answer with no citations", () => {
    const { container } = renderCard([]);

    expect(container.querySelector(".citation-card")).toBeNull();
  });
});

describe("EvidenceCard web sources (ADR-030)", () => {
  const WEB: ReplyCitation[] = [
    citation({ n: 1, title: "example.com · SaaS renewal benchmarks", corpus: "web", subtitle: null, snippet: "Typical renewals close with a 5-10% uplift cap.", href: "https://example.com/procurement/saas-renewals" }),
    citation({ n: 2, title: "example.org · Negotiation levers", corpus: "web", subtitle: "2026-09-01", snippet: "Multi-year commitments are the lever buyers cite most.", href: "https://example.org/negotiation/levers" }),
  ];

  it("files web sources under their own unverified section with a new-tab link per row", () => {
    const { container } = renderCard(WEB, []);

    const web = container.querySelector('[data-section="web"]');
    expect(web).not.toBeNull();
    expect(within(web as HTMLElement).getByText("Web · unverified")).toBeInTheDocument();
    expect(container.querySelector('[data-section="contracts"]')).toBeNull();
    expect(container.querySelector(".citation-card-title")).toHaveTextContent("Public web");
    expect(container.querySelector(".citation-card-subtitle")).toHaveTextContent("2 web sources");

    const links = within(web as HTMLElement).getAllByRole("link");
    expect(links.map((link) => link.textContent)).toEqual(["example.com ↗", "example.org ↗"]);
    for (const link of links) {
      expect(link).toHaveAttribute("target", "_blank");
      expect(link).toHaveAttribute("rel", "noopener noreferrer");
      expect(link.getAttribute("href")).toMatch(/^https:\/\//);
    }
    // The full URL is never printed as prose -- only the host.
    expect(container.textContent).not.toContain("/procurement/saas-renewals");
  });

  it("never derives a footer action from a web source", () => {
    const { container } = renderCard(WEB, []);

    expect(container.querySelector(".citation-card-footer")).toBeNull();
  });

  it("opens a web row through the same callback as every other row", async () => {
    const user = userEvent.setup();
    const { onOpen } = renderCard(WEB, []);

    await user.click(screen.getByRole("button", { name: "Open source 2" }));

    expect(onOpen.mock.calls[0][0]).toMatchObject({ n: 2, corpus: "web" });
  });
});
