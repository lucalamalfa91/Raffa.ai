import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ShellRoutes } from "../../../src/components/shell/WorkspaceShellApp";
import type { WorkspaceRole } from "../../../src/components/shell/navItems";
import type { ApiClient } from "../../../src/api/client";

// Task E06/F05/US01/T01 (document-upload) added the `documents` route's real
// content, which needs an ApiClient -- this suite only proves routing/guards
// (see tests/routes/documents/*.test.tsx for that screen's own coverage), so
// a plain stub is enough here, the same convention tests/App.test.tsx uses.
function mockApiClient(): ApiClient {
  return { getHealth: vi.fn(), createWorkspace: vi.fn(), uploadDocument: vi.fn() };
}

function renderShell(role: WorkspaceRole, initialPath = "/") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <ShellRoutes
        workspaceName="Acme Procurement"
        role={role}
        userLabel="user@example.test"
        onSignOut={vi.fn()}
        apiClient={mockApiClient()}
      />
    </MemoryRouter>,
  );
}

describe("ShellRoutes", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it("renders the rail alongside the Home placeholder at /", () => {
    renderShell("admin");

    expect(screen.getByRole("navigation", { name: /primary/i })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Home" })).toBeInTheDocument();
  });

  it("renders the global Ask bar on a routed screen (AC-3, every app screen)", () => {
    renderShell("admin", "/contracts");

    expect(screen.getByRole("heading", { name: "Portfolio" })).toBeInTheDocument();
    expect(screen.getByRole("search")).toBeInTheDocument();
  });

  it("gates /workspace/members behind the request-access state for Procurement", () => {
    renderShell("procurement", "/workspace/members");

    expect(screen.getByRole("heading", { name: /you don.t manage this workspace/i })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Workspace & members" })).not.toBeInTheDocument();
  });

  it("renders the real placeholder screen for an Admin at /workspace/members", () => {
    renderShell("admin", "/workspace/members");

    expect(screen.getByRole("heading", { name: "Workspace & members" })).toBeInTheDocument();
  });

  it("redirects an unknown path back to Home", () => {
    renderShell("admin", "/this-route-does-not-exist");

    expect(screen.getByRole("heading", { name: "Home" })).toBeInTheDocument();
  });

  it("renders the real Documents screen instead of a scaffold placeholder (task E06/F05/US01/T01)", () => {
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "Acme Procurement" }),
    );

    renderShell("admin", "/documents");

    expect(screen.getByRole("heading", { name: "Documents" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /choose from computer/i })).toBeInTheDocument();
    expect(screen.queryByText(/ships in epic-06\/feature-05-document-upload-ui/i)).not.toBeInTheDocument();
  });
});
