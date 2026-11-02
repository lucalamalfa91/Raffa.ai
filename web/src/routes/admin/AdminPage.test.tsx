import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { InteractionStatus } from "@azure/msal-browser";
import type { ApiClient } from "../../api/client";
import AdminPage from "./AdminPage";

const msal = vi.hoisted(() => ({
  state: { accounts: [] as Array<{ username: string }>, inProgress: "none", cached: [] as Array<{ username: string }> },
}));

vi.mock("@azure/msal-react", () => ({
  useMsal: () => ({
    accounts: msal.state.accounts,
    inProgress: msal.state.inProgress,
    instance: { getAllAccounts: () => msal.state.cached, logoutRedirect: vi.fn() },
  }),
}));

vi.mock("./index", () => ({ default: () => <div>Admin panel body</div> }));

function renderAt() {
  return render(
    <MemoryRouter initialEntries={["/admin"]}>
      <Routes>
        <Route path="/admin" element={<AdminPage apiClient={{} as ApiClient} />} />
        <Route path="/" element={<div>Workspace picker</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("AdminPage", () => {
  it("waits while MSAL is still starting instead of bouncing a signed-in admin to the picker", () => {
    msal.state = { accounts: [], inProgress: InteractionStatus.Startup, cached: [] };
    renderAt();
    expect(screen.queryByText("Workspace picker")).not.toBeInTheDocument();
  });

  it("shows the panel when the account is only in MSAL's cache", () => {
    msal.state = { accounts: [], inProgress: InteractionStatus.Startup, cached: [{ username: "pietro@x.example" }] };
    renderAt();
    expect(screen.getByText("Admin panel body")).toBeInTheDocument();
    expect(screen.getByText("pietro@x.example")).toBeInTheDocument();
  });

  it("goes back to sign-in only once MSAL is idle and there is no account", () => {
    msal.state = { accounts: [], inProgress: InteractionStatus.None, cached: [] };
    renderAt();
    expect(screen.getByText("Workspace picker")).toBeInTheDocument();
  });
});
