import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ApiClient, CreateCustomerWorkspaceResult } from "../../../src/api/client";
import AdminRoute from "../../../src/routes/admin";

const WORKSPACE = { id: "11111111-1111-1111-1111-111111111111", name: "Staff Home", role: "Admin", contractCount: 0, createdAt: "2026-09-01T00:00:00Z" };

function fakeClient(create: CreateCustomerWorkspaceResult): ApiClient {
  return {
    listWorkspaces: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, workspaces: [WORKSPACE], pendingInvitations: [], error: null }),
    createCustomerWorkspace: vi.fn().mockResolvedValue(create),
  } as unknown as ApiClient;
}

const created = (invitation: boolean): CreateCustomerWorkspaceResult => ({
  ok: true,
  statusCode: 201,
  error: null,
  result: {
    workspace: { id: "22222222-2222-2222-2222-222222222222", name: "Acme Demo", createdAt: "2026-11-02T00:00:00Z" },
    invitation: invitation
      ? {
          id: "33333333-3333-3333-3333-333333333333",
          email: "cfo@acme.example",
          role: "Admin",
          expiresAt: "2026-11-09T00:00:00Z",
          acceptUrl: "https://app.example/invite/accept#abc.def",
          deliveryOutcome: "no_transport",
          identityProvisioned: false,
        }
      : null,
    invitationError: invitation ? null : "The customer's sign-in identity could not be prepared (consent_missing).",
  },
});

describe("AdminRoute", () => {
  it("lists the staff member's workspaces and keeps Create disabled until name and email are filled", async () => {
    render(<AdminRoute apiClient={fakeClient(created(true))} currentWorkspaceId={WORKSPACE.id} />);

    expect(await screen.findByText("Staff Home")).toBeInTheDocument();
    expect(screen.getByText("Open now")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /create workspace and invite/i })).toBeDisabled();
  });

  it("creates the customer workspace, shows the invitation link and opens it on request", async () => {
    const user = userEvent.setup();
    const apiClient = fakeClient(created(true));
    const openWorkspace = vi.fn();
    render(<AdminRoute apiClient={apiClient} openWorkspace={openWorkspace} />);

    await user.type(screen.getByLabelText("Workspace name"), " Acme Demo ");
    await user.type(screen.getByLabelText("Customer admin email"), "cfo@acme.example");
    await user.selectOptions(screen.getByLabelText(/country/i), "CH");
    await user.click(screen.getByRole("button", { name: /create workspace and invite/i }));

    await waitFor(() =>
      expect(apiClient.createCustomerWorkspace).toHaveBeenCalledWith({ name: "Acme Demo", customerEmail: "cfo@acme.example", country: "CH" }),
    );
    expect(await screen.findByText(/cfo@acme.example is invited as Admin/i)).toBeInTheDocument();
    expect(screen.getByLabelText("Invitation link")).toHaveValue("https://app.example/invite/accept#abc.def");

    await user.click(screen.getByRole("button", { name: /open this workspace/i }));
    expect(openWorkspace).toHaveBeenCalledWith({ id: "22222222-2222-2222-2222-222222222222", name: "Acme Demo" });
    expect(screen.getByLabelText("Workspace name")).toHaveValue("");
  });

  it("says plainly when the workspace exists but the invitation was not issued", async () => {
    const user = userEvent.setup();
    render(<AdminRoute apiClient={fakeClient(created(false))} />);

    await user.type(screen.getByLabelText("Workspace name"), "Acme Demo");
    await user.type(screen.getByLabelText("Customer admin email"), "cfo@acme.example");
    await user.click(screen.getByRole("button", { name: /create workspace and invite/i }));

    expect(await screen.findByText(/invitation was not issued: .*consent_missing/i)).toBeInTheDocument();
    expect(screen.queryByLabelText("Invitation link")).not.toBeInTheDocument();
  });

  it("shows a refusal instead of a result when the caller is not a platform admin", async () => {
    const user = userEvent.setup();
    render(<AdminRoute apiClient={fakeClient({ ok: false, statusCode: 404, result: null, error: "Request failed with HTTP 404 Not Found." })} />);

    await user.type(screen.getByLabelText("Workspace name"), "Acme Demo");
    await user.type(screen.getByLabelText("Customer admin email"), "cfo@acme.example");
    await user.click(screen.getByRole("button", { name: /create workspace and invite/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("You are not a Raffa platform admin.");
  });
});
