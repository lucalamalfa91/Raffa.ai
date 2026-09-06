import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import RailNav from "../../../src/components/shell/RailNav";

function renderRail(role: "admin" | "procurement", onSignOut = vi.fn()) {
  return render(
    <MemoryRouter>
      <RailNav workspaceName="Acme Procurement" role={role} userLabel="user@example.test" onSignOut={onSignOut} />
    </MemoryRouter>,
  );
}

describe("RailNav", () => {
  it("renders the workspace name and every AC-1 item for a Workspace Admin", () => {
    renderRail("admin");

    expect(screen.getByText("Acme Procurement")).toBeInTheDocument();
    expect(screen.getByText("Workspace & members")).toBeInTheDocument();
    expect(screen.getByText("Workspace Admin")).toBeInTheDocument();
  });

  it("hides Workspace & members for Procurement (AC-2)", () => {
    renderRail("procurement");

    expect(screen.queryByText("Workspace & members")).not.toBeInTheDocument();
    expect(screen.getByText("Portfolio")).toBeInTheDocument();
    expect(screen.getByText("Procurement")).toBeInTheDocument();
  });

  it("calls onSignOut when the footer's Sign out control is clicked", async () => {
    const onSignOut = vi.fn();
    renderRail("admin", onSignOut);

    await userEvent.click(screen.getByRole("button", { name: /sign out/i }));

    expect(onSignOut).toHaveBeenCalledTimes(1);
  });
});
