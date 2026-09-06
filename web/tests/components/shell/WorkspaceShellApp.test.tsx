import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ShellRoutes } from "../../../src/components/shell/WorkspaceShellApp";
import type { WorkspaceRole } from "../../../src/components/shell/navItems";

function renderShell(role: WorkspaceRole, initialPath = "/") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <ShellRoutes workspaceName="Acme Procurement" role={role} userLabel="user@example.test" onSignOut={vi.fn()} />
    </MemoryRouter>,
  );
}

describe("ShellRoutes", () => {
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
});
