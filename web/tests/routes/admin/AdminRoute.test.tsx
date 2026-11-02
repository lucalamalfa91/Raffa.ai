import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminCallResult, ApiClient } from "../../../src/api/client";
import AdminRoute from "../../../src/routes/admin";

const ok = <T,>(body: T): AdminCallResult<T> => ({ ok: true, statusCode: 200, body, error: null });

function fakeClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getAdminOverview: vi.fn().mockResolvedValue(
      ok({ totalWorkspaces: 3, activeWorkspaces: 2, suspendedWorkspaces: 1, registeredUsers: 4, pendingInvitations: 2 }),
    ),
    listAdminWorkspaces: vi.fn().mockResolvedValue(
      ok({
        workspaces: [
          { id: "w1", name: "Prada", adminEmail: "ceo@prada.example", createdAt: "2026-10-01T00:00:00Z", status: "active" },
          { id: "w2", name: "Bulgari", adminEmail: null, createdAt: "2026-10-02T00:00:00Z", status: "suspended" },
        ],
      }),
    ),
    setAdminWorkspaceStatus: vi.fn().mockResolvedValue({ ok: true, statusCode: 204, body: null, error: null }),
    listAdminInvitations: vi.fn().mockResolvedValue(
      ok({
        invitations: [
          { id: "i1", email: "cfo@roche.example", status: "pending", createdAt: "2026-10-03T00:00:00Z", expiresAt: "2026-10-10T00:00:00Z", acceptedAt: null, workspaceId: null },
          { id: "i2", email: "old@x.example", status: "expired", createdAt: "2026-09-01T00:00:00Z", expiresAt: "2026-09-08T00:00:00Z", acceptedAt: null, workspaceId: null },
        ],
      }),
    ),
    createAdminInvitation: vi.fn().mockResolvedValue(
      ok({ id: "i3", email: "new@acme.example", expiresAt: "2026-11-09T00:00:00Z", acceptUrl: "https://app.example/prospect/accept#secret", deliveryOutcome: "sent", identityProvisioned: true }),
    ),
    regenerateAdminInvitation: vi.fn(),
    revokeAdminInvitation: vi.fn().mockResolvedValue({ ok: true, statusCode: 204, body: null, error: null }),
    listAdminUsers: vi.fn().mockResolvedValue(ok({ users: [{ email: "ceo@prada.example", joinedAt: "2026-10-01T00:00:00Z", workspaceId: "w1", hasWorkspace: true }] })),
    ...overrides,
  } as unknown as ApiClient;
}

describe("AdminRoute", () => {
  it("shows the overview counts", async () => {
    render(<AdminRoute apiClient={fakeClient()} />);
    expect(await screen.findByText("Registered users")).toBeInTheDocument();
    expect(screen.getByText("4")).toBeInTheDocument();
  });

  it("says plainly when the caller is not a platform admin", async () => {
    const apiClient = fakeClient({
      getAdminOverview: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, body: null, error: "x" }),
    });
    render(<AdminRoute apiClient={apiClient} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("You are not a Raffa platform admin.");
  });

  it("lists workspaces as metadata and suspends or reactivates them", async () => {
    const user = userEvent.setup();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const apiClient = fakeClient();
    render(<AdminRoute apiClient={apiClient} />);

    await user.click(screen.getByRole("button", { name: "Workspaces" }));
    expect(await screen.findByText("Prada")).toBeInTheDocument();
    expect(screen.getByText("ceo@prada.example")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Suspend" }));
    await waitFor(() => expect(apiClient.setAdminWorkspaceStatus).toHaveBeenCalledWith("w1", "suspend"));
    await user.click(screen.getByRole("button", { name: "Reactivate" }));
    await waitFor(() => expect(apiClient.setAdminWorkspaceStatus).toHaveBeenCalledWith("w2", "reactivate"));
  });

  it("creates an invitation and shows its link once, with revoke only on pending ones", async () => {
    const user = userEvent.setup();
    const apiClient = fakeClient();
    render(<AdminRoute apiClient={apiClient} />);

    await user.click(screen.getByRole("button", { name: "Invitations" }));
    expect(await screen.findByText("cfo@roche.example")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Revoke" })).toHaveLength(1);

    await user.type(screen.getByLabelText("Prospect email"), "new@acme.example");
    await user.click(screen.getByRole("button", { name: "Create invitation" }));

    await waitFor(() => expect(apiClient.createAdminInvitation).toHaveBeenCalledWith("new@acme.example"));
    expect(await screen.findByLabelText(/Invitation link/)).toHaveValue("https://app.example/prospect/accept#secret");
    expect(screen.getByText(/An email with the invitation link was sent/)).toBeInTheDocument();

    await user.click(screen.getAllByRole("button", { name: "Revoke" })[0]!);
    await waitFor(() => expect(apiClient.revokeAdminInvitation).toHaveBeenCalledWith("i1"));
  });

  it("lists the people who accepted an invitation", async () => {
    const user = userEvent.setup();
    render(<AdminRoute apiClient={fakeClient()} />);
    await user.click(screen.getByRole("button", { name: "Users" }));
    expect(await screen.findByText("ceo@prada.example")).toBeInTheDocument();
    expect(screen.getByText("Created")).toBeInTheDocument();
  });

  it("explains when the directory would not prepare the prospect's sign-in, and creates nothing", async () => {
    const user = userEvent.setup();
    const apiClient = fakeClient({
      createAdminInvitation: vi.fn().mockResolvedValue({ ok: false, statusCode: 502, body: null, error: '{"failureReason":"consent_missing"}' }),
    });
    render(<AdminRoute apiClient={apiClient} />);

    await user.click(screen.getByRole("button", { name: "Invitations" }));
    await user.type(await screen.findByLabelText("Prospect email"), "anyone@hotmail.it");
    await user.click(screen.getByRole("button", { name: "Create invitation" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/admin consent is missing/i);
    expect(screen.queryByLabelText(/Invitation link/)).not.toBeInTheDocument();
  });
});
