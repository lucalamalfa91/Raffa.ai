import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ApiClient } from "../../../src/api/client";
import AppShell from "../../../src/components/shell/AppShell";
import { useGuide, useNavBadge } from "../../../src/components/shell/guide";
import { FixtureSessionContext, type FixtureSession } from "../../../src/fixtures/FixtureSessionContext";
import { useEffect } from "react";

/** AskRaffa 4.0 shell: top nav (5 tabs, active tab, Oggi badge), Guida Raffa (fallback copy,
 * screen copy via useGuide, Nascondi / toggle) and the demo bar in fixture mode only. */
function apiClient(): ApiClient {
  return {
    listDocuments: vi.fn().mockResolvedValue({ ok: false, statusCode: null, page: null, error: "x" }),
    getPortfolio: vi.fn().mockResolvedValue({ ok: false, statusCode: null, page: null, error: "x" }),
    listWorkspaces: vi.fn().mockResolvedValue({ ok: false, statusCode: null, workspaces: null, error: "x" }),
    listConversations: vi.fn().mockResolvedValue({ ok: false, statusCode: null, conversations: null, error: "x" }),
  } as unknown as ApiClient;
}

function GuidedScreen() {
  const setBadge = useNavBadge();
  useEffect(() => setBadge(3), [setBadge]);
  useGuide({ where: "Rinnovo · DocuSign", now: "Approva la bozza", why: "Serve la tua approvazione.", next: "Poi la invii.", cta: { label: "Apri il passo", onClick: () => undefined } });
  return <p>guided screen</p>;
}

function renderShell(path: string, fixture: FixtureSession | null = null) {
  return render(
    <FixtureSessionContext.Provider value={fixture}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route element={<AppShell workspaceId="w" workspaceName="Helvetia Foods AG" role="admin" userLabel="Luca Lamalfa" onSignOut={vi.fn()} apiClient={apiClient()} />}>
            <Route path="today" element={<p>today stub</p>} />
            <Route path="renewals/:id" element={<GuidedScreen />} />
            <Route path="contracts" element={<p>contracts stub</p>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </FixtureSessionContext.Provider>,
  );
}

const fixtureSession = (): FixtureSession => ({
  scenario: "zero",
  viewer: "luca",
  now: "2026-09-08",
  tenantName: "Helvetia Foods AG",
  setScenario: vi.fn().mockResolvedValue(undefined),
  setViewer: vi.fn(),
  reset: vi.fn().mockResolvedValue(undefined),
});

describe("AppShell (AskRaffa 4.0)", () => {
  beforeEach(() => {
    window.localStorage.clear();
    Object.defineProperty(window, "innerWidth", { value: 1440, configurable: true });
  });

  it("starts with the guide closed below 1100px (DESIGN-GAPS A1)", () => {
    Object.defineProperty(window, "innerWidth", { value: 1024, configurable: true });
    renderShell("/today");
    expect(screen.queryByRole("complementary", { name: "Raffa guide" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Raffa guide" })).toHaveAttribute("aria-pressed", "false");
  });

  it("renders the header nav first, with the six tabs and the active one marked", () => {
    renderShell("/contracts");

    const banner = screen.getByRole("banner");
    expect(within(banner).getByText("Raffa.ai")).toBeInTheDocument();
    const nav = within(banner).getByRole("navigation", { name: "Primary" });
    expect(within(nav).getAllByRole("link").map((a) => a.getAttribute("href"))).toEqual(["/today", "/renewals", "/contracts", "/savings", "/ask", "/settings"]);
    expect(within(nav).getByRole("link", { name: "Contracts" })).toHaveAttribute("aria-current", "page");
    expect(within(nav).getByRole("link", { name: "Today" })).not.toHaveAttribute("aria-current");
  });

  it("shows the screen's fallback guide copy, and the screen's own copy and badge when it states them", () => {
    const { unmount } = renderShell("/contracts");
    const guide = screen.getByRole("complementary", { name: "Raffa guide" });
    expect(guide).toHaveAttribute("data-screen-label", "Guida Raffa");
    expect(within(guide).getByText("Validated contracts")).toBeInTheDocument();
    expect(within(guide).getByRole("button", { name: "Upload a contract" })).toBeInTheDocument();
    unmount();

    renderShell("/renewals/c1");
    const guided = screen.getByRole("complementary", { name: "Raffa guide" });
    expect(within(guided).getByText("Approva la bozza")).toBeInTheDocument();
    expect(within(guided).getByRole("button", { name: "Apri il passo" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^Today\s*3$/ })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Renewals" })).toHaveAttribute("aria-current", "page");
  });

  it("hides the guide with Nascondi and brings it back with the toggle", async () => {
    const user = userEvent.setup();
    renderShell("/today");
    const toggle = screen.getByRole("button", { name: "Raffa guide" });
    expect(toggle).toHaveAttribute("aria-pressed", "true");

    await user.click(screen.getByRole("button", { name: "Hide" }));
    expect(screen.queryByRole("complementary", { name: "Raffa guide" })).not.toBeInTheDocument();
    expect(toggle).toHaveAttribute("aria-pressed", "false");

    await user.click(toggle);
    expect(screen.getByRole("complementary", { name: "Raffa guide" })).toBeInTheDocument();
  });

  it("renders no demo bar outside fixture mode", () => {
    const { container } = renderShell("/today");
    expect(container.querySelector("[data-no-i18n]")).toBeNull();
  });

  it("in fixture mode: Italian by default, demo bar above the nav, switches call the session", async () => {
    const user = userEvent.setup();
    const session = fixtureSession();
    const { container } = renderShell("/today", session);

    const bar = container.querySelector("[data-no-i18n]") as HTMLElement;
    expect(bar).not.toBeNull();
    expect(bar.compareDocumentPosition(screen.getByRole("banner")) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(within(bar).getByText(/Orologio di scenario 08 Set 2026 · Helvetia Foods AG/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Oggi" })).toBeInTheDocument();
    expect(screen.getByText("Guida Raffa", { selector: ".v4-guide-title" })).toBeInTheDocument();

    await user.click(within(bar).getByRole("button", { name: "Marta · Buyer" }));
    expect(session.setViewer).toHaveBeenCalledWith("marta");
    await user.click(within(bar).getByRole("button", { name: "Con dati" }));
    expect(session.setScenario).toHaveBeenCalledWith("full");
    await user.click(within(bar).getByRole("button", { name: "Reimposta" }));
    expect(session.reset).toHaveBeenCalled();

    await user.click(within(bar).getByRole("button", { name: "EN" }));
    expect(screen.getByRole("link", { name: "Today" })).toBeInTheDocument();
    expect(window.localStorage.getItem("raffa4.lang")).toBe("en");
  });
});
