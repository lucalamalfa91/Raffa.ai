import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import RailNav from "../../../src/components/shell/RailNav";
import { rememberDocument } from "../../../src/routes/documents/documentStore";

function renderRail(
  overrides: Partial<{
    role: "admin" | "procurement";
    kbReady: boolean;
    validatedContractCount: number;
    onSignOut: () => void;
  }> = {},
) {
  const { role = "admin", kbReady = false, validatedContractCount = 0, onSignOut = vi.fn() } = overrides;
  return render(
    <MemoryRouter>
      <RailNav
        workspaceName="Acme Procurement"
        role={role}
        userLabel="user@example.test"
        kbReady={kbReady}
        validatedContractCount={validatedContractCount}
        onSignOut={onSignOut}
      />
    </MemoryRouter>,
  );
}

describe("RailNav (V2 two-tier rail, ADR-024 amendment; task E13/F09/US01/T01, gap G-IA-V2)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it("renders the workspace name and the two tiers in order: Ask Contigo, Documents, then 'From your contracts' / Portfolio, Renewals, Quote check", () => {
    const { container } = renderRail();

    expect(screen.getByText("Acme Procurement")).toBeInTheDocument();

    const labels = Array.from(container.querySelectorAll(".shell-rail-item-label")).map((node) => node.textContent);
    expect(labels).toEqual(["Ask Contigo", "Documents", "Portfolio", "Renewals", "Quote check"]);
  });

  it("has no Home or Review queue item anywhere in the rail (V2 removes both)", () => {
    renderRail();

    expect(screen.queryByText("Home")).not.toBeInTheDocument();
    expect(screen.queryByText("Review queue")).not.toBeInTheDocument();
  });

  it("Ask Contigo carries the ⌘K badge and an empty conversation slot with '+ New chat'", () => {
    renderRail();

    const askLink = screen.getByText("Ask Contigo").closest("a")!;
    expect(askLink).toHaveTextContent("⌘K");

    const newChat = screen.getByRole("link", { name: "+ New chat" });
    expect(newChat).toHaveAttribute("href", "/ask");
  });

  describe("Documents badge (`app.jsx`: needReview -> 'N to review' (attention) else docs.length -> 'N docs')", () => {
    it("shows no badge when this browser has not tracked any document yet", () => {
      renderRail();

      const documentsLink = screen.getByText("Documents").closest("a")!;
      expect(documentsLink).toHaveTextContent(/^Documents$/);
    });

    it("shows a muted 'N docs' count once documents are tracked, none needing review", () => {
      rememberDocument({
        id: "doc-1",
        contractId: "contract-1",
        fileName: "msa.pdf",
        documentType: "Msa",
        processingStatus: "Completed",
        createdAt: "2026-09-01T00:00:00Z",
      });

      renderRail();

      const documentsLink = screen.getByText("Documents").closest("a")!;
      expect(documentsLink).toHaveTextContent("1 docs");
    });

    it("shows an attention-toned 'N to review' once at least one tracked document needs review", () => {
      rememberDocument({
        id: "doc-1",
        contractId: "contract-1",
        fileName: "msa.pdf",
        documentType: "Msa",
        processingStatus: "NeedsReview",
        createdAt: "2026-09-01T00:00:00Z",
      });

      renderRail();

      const documentsLink = screen.getByText("Documents").closest("a")!;
      expect(documentsLink).toHaveTextContent("1 to review");
      expect(documentsLink.querySelector(".shell-rail-badge")).toHaveClass("is-attention");
    });
  });

  describe("greyed secondary tier (kbReady)", () => {
    it("greys Portfolio, Renewals and Quote check and shows no count badge with 0 validated contracts", () => {
      renderRail({ kbReady: false, validatedContractCount: 0 });

      for (const label of ["Portfolio", "Renewals", "Quote check"]) {
        const link = screen.getByText(label).closest("a")!;
        expect(link).toHaveClass("is-greyed");
      }
      const portfolioLink = screen.getByText("Portfolio").closest("a")!;
      expect(portfolioLink).not.toHaveTextContent(/\d/);
    });

    it("Quote check still shows 'optional' even while greyed", () => {
      renderRail({ kbReady: false });

      expect(screen.getByText("Quote check").closest("a")).toHaveTextContent("optional");
    });

    it("un-greys the tier and badges Portfolio/Renewals with the validated count once kbReady", () => {
      renderRail({ kbReady: true, validatedContractCount: 3 });

      for (const label of ["Portfolio", "Renewals", "Quote check"]) {
        const link = screen.getByText(label).closest("a")!;
        expect(link).not.toHaveClass("is-greyed");
      }
      expect(screen.getByText("Portfolio").closest("a")).toHaveTextContent("3");
      expect(screen.getByText("Renewals").closest("a")).toHaveTextContent("3");
      expect(screen.getByText("Quote check").closest("a")).toHaveTextContent("optional");
    });
  });

  describe("footer", () => {
    it("shows Workspace & members, the role label, and Sign out for a Workspace Admin", () => {
      renderRail({ role: "admin" });

      expect(screen.getByText("Workspace & members")).toBeInTheDocument();
      expect(screen.getByText("Workspace Admin")).toBeInTheDocument();
    });

    it("hides Workspace & members for Procurement (AC-2)", () => {
      renderRail({ role: "procurement" });

      expect(screen.queryByText("Workspace & members")).not.toBeInTheDocument();
      expect(screen.getByText("Portfolio")).toBeInTheDocument();
      expect(screen.getByText("Procurement")).toBeInTheDocument();
    });

    it("calls onSignOut when the footer's Sign out control is clicked", async () => {
      const onSignOut = vi.fn();
      renderRail({ onSignOut });

      await userEvent.click(screen.getByRole("button", { name: /sign out/i }));

      expect(onSignOut).toHaveBeenCalledTimes(1);
    });
  });
});
