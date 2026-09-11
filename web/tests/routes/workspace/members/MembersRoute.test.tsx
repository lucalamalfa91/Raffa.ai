import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import MembersRoute from "../../../../src/routes/workspace/members";
import type { ShellOutletContext } from "../../../../src/components/shell/shellContext";
import type { WorkspaceRole } from "../../../../src/components/shell/navItems";
import type { ApiClient, GetWorkspaceMembersResult, InviteWorkspaceMemberResult, WorkspaceMemberBody } from "../../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const USER_LABEL = "admin@acme.example";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    listWorkspaces: vi.fn(),
    getWorkspaceMembers: vi.fn(() => new Promise<never>(() => {})),
    revokeInvitation: vi.fn(),
    removeMember: vi.fn(),
    getInvitation: vi.fn(),
    acceptInvitation: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn(),
    validateDocument: vi.fn(),
    postRenewalAction: vi.fn(),
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askRaffa: vi.fn(),
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    ...overrides,
  };
}

function member(overrides: Partial<WorkspaceMemberBody> = {}): WorkspaceMemberBody {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    email: USER_LABEL,
    role: "Admin",
    status: "Active",
    ...overrides,
  };
}

function roster(members: WorkspaceMemberBody[]): GetWorkspaceMembersResult {
  return { ok: true, statusCode: 200, members, error: null };
}

function invited(overrides: Partial<NonNullable<InviteWorkspaceMemberResult["member"]>> = {}): InviteWorkspaceMemberResult {
  return {
    ok: true,
    statusCode: 201,
    member: {
      id: "22222222-2222-2222-2222-222222222222",
      email: "buyer@acme.example",
      role: "Procurement",
      expiresAt: "2026-09-18T12:00:00Z",
      acceptUrl: "/invite/accept#11111111111111111111111111111111.fake-secret",
      mailDelivered: false,
      ...overrides,
    },
    error: null,
  };
}

/** `shell` renders the route under the app shell's own `<Outlet context>` (see `AppShell.tsx`); omitted = outside the shell, as before. `role` defaults to Admin -- the Procurement variant is its own test group below. */
function renderMembers(apiClient: ApiClient, options: { role?: WorkspaceRole; shell?: ShellOutletContext } = {}) {
  const role = options.role ?? "admin";
  const route = (
    <Route
      path="/workspace/members"
      element={<MembersRoute apiClient={apiClient} userLabel={USER_LABEL} workspaceId={WORKSPACE_ID} role={role} />}
    />
  );
  return render(
    <MemoryRouter initialEntries={["/workspace/members"]}>
      <Routes>{options.shell ? <Route element={<Outlet context={options.shell} />}>{route}</Route> : route}</Routes>
    </MemoryRouter>,
  );
}

describe("MembersRoute (V2, ADR-020 screen 10)", () => {
  it("renders the V2 header (Setup kicker, title, tenant line) and the three-column members table with the Admin as an Active row", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(roster([member()]));
    renderMembers(mockApiClient({ getWorkspaceMembers }));

    expect(screen.getByText("Setup")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Workspace & members" })).toBeInTheDocument();
    expect(screen.getByText(`tenant ${WORKSPACE_ID}`)).toBeInTheDocument();

    const table = await screen.findByRole("table");
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Member",
      "Role",
      "Status",
      "Actions",
    ]);
    expect(within(table).getByText(USER_LABEL)).toBeInTheDocument();
    expect(within(table).getByText("You")).toBeInTheDocument();
    expect(within(table).getByRole("cell", { name: "Workspace Admin" })).toBeInTheDocument();
    expect(within(table).getByText("Active")).toHaveClass("tag-neutral");
    expect(getWorkspaceMembers).toHaveBeenCalledWith(WORKSPACE_ID);
  });

  it("shows the invite tip only while the knowledge base has no validated contract yet", async () => {
    const { unmount } = renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn().mockResolvedValue(roster([member()])) }), {
      shell: { kbReady: false, validatedContractCount: 0 },
    });
    expect(screen.getByRole("note")).toHaveTextContent(
      "Tip: invite the team once the first contract is validated — there is nothing for them to ask before that.",
    );
    await screen.findByRole("table");
    unmount();

    renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn().mockResolvedValue(roster([member()])) }), {
      shell: { kbReady: true, validatedContractCount: 3 },
    });
    await screen.findByRole("table");
    expect(
      screen.queryByText("Tip: invite the team once the first contract is validated — there is nothing for them to ask before that."),
    ).not.toBeInTheDocument();
  });

  it("invite pane: Work email with the tenant-domain placeholder, Procurement (default) then Workspace Admin radios with D8 summaries, Send invitation", async () => {
    renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn().mockResolvedValue(roster([member()])) }));
    await screen.findByRole("table");

    const pane = screen.getByRole("complementary", { name: "Invite a colleague" });
    expect(within(pane).getByRole("heading", { level: 4, name: "Invite a colleague" })).toBeInTheDocument();
    expect(within(pane).getByLabelText("Work email")).toHaveAttribute("placeholder", "name@acme.example");

    const radios = within(pane).getAllByRole("radio");
    expect(radios.map((radio) => radio.getAttribute("value"))).toEqual(["Procurement", "Admin"]);
    expect(within(pane).getByRole("radio", { name: /procurement/i })).toBeChecked();
    expect(within(pane).getByText("Asks, uploads, reviews, triages renewals")).toBeInTheDocument();
    expect(within(pane).getByText("Also deletes documents and manages members")).toBeInTheDocument();
    expect(within(pane).getByRole("button", { name: "Send invitation" })).toHaveClass("btn-block");
  });

  describe("AC-1 / AC-3 / AC-4 -- the roster is a server read, and the invite result is the server's own fact", () => {
    it("the roster renders from getWorkspaceMembers; a successful invite re-reads it instead of appending a row locally", async () => {
      const getWorkspaceMembers = vi
        .fn()
        .mockResolvedValueOnce(roster([member()]))
        .mockResolvedValueOnce(
          roster([member(), member({ id: "u2", email: "buyer@acme.example", role: "Admin", status: "Invited" })]),
        );
      const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited({ role: "Admin" }));
      renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }));

      await screen.findByRole("table");
      fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
      fireEvent.click(screen.getByRole("radio", { name: /workspace admin/i }));
      fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

      expect(inviteWorkspaceMember).toHaveBeenCalledWith(WORKSPACE_ID, { email: "buyer@acme.example", role: "Admin" });
      await waitFor(() => expect(getWorkspaceMembers).toHaveBeenCalledTimes(2));
      await waitFor(() => expect(screen.getByText("buyer@acme.example")).toBeInTheDocument());
      expect(within(screen.getByRole("table")).getByText("Invited")).toHaveClass("tag-accent");
      expect(screen.getByLabelText("Work email")).toHaveValue("");
    });

    it("mailDelivered: true renders 'Invitation sent to {email}.'", async () => {
      const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited({ mailDelivered: true, email: "buyer@acme.example" }));
      renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn().mockResolvedValue(roster([member()])), inviteWorkspaceMember }));

      await screen.findByRole("table");
      fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
      fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

      expect(await screen.findByRole("status")).toHaveTextContent("Invitation sent to buyer@acme.example.");
      expect(screen.queryByRole("button", { name: "Copy link" })).not.toBeInTheDocument();
    });

    it("mailDelivered: false renders 'Invitation ready for {email}.' with the copyable link, its expiry and Copy link -- and 'sent' appears nowhere", async () => {
      const inviteWorkspaceMember = vi.fn().mockResolvedValue(
        invited({
          mailDelivered: false,
          email: "buyer@acme.example",
          acceptUrl: "/invite/accept#11111111111111111111111111111111.fake-secret",
          expiresAt: "2026-09-18T12:00:00Z",
        }),
      );
      renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn().mockResolvedValue(roster([member()])), inviteWorkspaceMember }));

      await screen.findByRole("table");
      fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
      fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

      expect(await screen.findByText("Invitation ready for buyer@acme.example.")).toBeInTheDocument();
      expect(screen.getByText("It expires 18/09/2026, can be used once, and is not shown again.")).toBeInTheDocument();
      const linkField = screen.getByLabelText("Invitation link") as HTMLInputElement;
      expect(linkField.value).toContain("/invite/accept#11111111111111111111111111111111.fake-secret");
      expect(screen.getByRole("button", { name: "Copy link" })).toBeInTheDocument();

      const pane = screen.getByRole("complementary", { name: "Invite a colleague" });
      expect(pane.textContent?.toLowerCase()).not.toMatch(/\bsent\b/);
    });

    it("the failure path stops claiming a mail too", async () => {
      const inviteWorkspaceMember = vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 400,
        member: null,
        error: "buyer@acme.example already holds the Procurement role in this workspace.",
      });
      renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn().mockResolvedValue(roster([member()])), inviteWorkspaceMember }));

      await screen.findByRole("table");
      fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
      fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

      await waitFor(() => {
        expect(screen.getByRole("alert")).toHaveTextContent(/already holds the Procurement role/i);
      });
      expect(screen.queryByText("Invitation sent to buyer@acme.example.")).not.toBeInTheDocument();
      expect(screen.queryByText("Invitation ready for buyer@acme.example.")).not.toBeInTheDocument();
    });
  });

  describe("AC-5 -- the domain rule is a warning, never a block", () => {
    it("a cross-domain address warns and leaves submit enabled, and the request still fires", async () => {
      const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited());
      renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn().mockResolvedValue(roster([member()])), inviteWorkspaceMember }));

      await screen.findByRole("table");
      fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@gmail.com" } });
      fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

      expect(
        await screen.findByText("buyer@gmail.com is outside acme.example. They will get full Procurement access to this workspace."),
      ).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Send invitation" })).not.toBeDisabled();
      await waitFor(() =>
        expect(inviteWorkspaceMember).toHaveBeenCalledWith(WORKSPACE_ID, { email: "buyer@gmail.com", role: "Procurement" }),
      );
    });

    it("a malformed address still blocks, with no API call", async () => {
      const inviteWorkspaceMember = vi.fn();
      renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn().mockResolvedValue(roster([member()])), inviteWorkspaceMember }));

      await screen.findByRole("table");
      fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "not-an-email" } });
      fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

      expect(screen.getByRole("alert")).toHaveTextContent("Enter a valid email address.");
      expect(inviteWorkspaceMember).not.toHaveBeenCalled();
    });
  });

  describe("AC-6 / AC-7 -- revoke and remove", () => {
    it("render different consequence copy, confirm inline, and never remove a row optimistically", async () => {
      const self = member({ id: "self-1", email: USER_LABEL, role: "Admin", status: "Active" });
      const otherAdmin = member({ id: "admin-2", email: "other-admin@acme.example", role: "Admin", status: "Active" });
      const invitedRow = member({ id: "inv-1", email: "pending@acme.example", role: "Procurement", status: "Invited" });
      const getWorkspaceMembers = vi.fn().mockResolvedValue(roster([self, otherAdmin, invitedRow]));
      const revokeInvitation = vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null });
      const removeMember = vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null });
      renderMembers(mockApiClient({ getWorkspaceMembers, revokeInvitation, removeMember }));

      const table = await screen.findByRole("table");

      const invitedRowEl = within(table).getByText("pending@acme.example").closest("tr");
      if (!invitedRowEl) throw new Error("invited row not found");
      fireEvent.click(within(invitedRowEl).getByRole("button", { name: "Revoke" }));
      expect(
        within(invitedRowEl).getByText("Their link stops working. They never had access to this workspace."),
      ).toBeInTheDocument();
      expect(within(invitedRowEl).queryByText(/they lose access/i)).not.toBeInTheDocument();

      fireEvent.click(within(invitedRowEl).getByRole("button", { name: "Confirm revoke" }));
      // No optimistic removal: the row is still rendered the instant the confirm click returns,
      // before the mocked revoke call has resolved.
      expect(within(screen.getByRole("table")).getByText("pending@acme.example")).toBeInTheDocument();
      await waitFor(() => expect(revokeInvitation).toHaveBeenCalledWith(WORKSPACE_ID, "inv-1"));
      await waitFor(() => expect(getWorkspaceMembers).toHaveBeenCalledTimes(2));

      const activeTable = screen.getByRole("table");
      const activeRowEl = within(activeTable).getByText("other-admin@acme.example").closest("tr");
      if (!activeRowEl) throw new Error("active row not found");
      fireEvent.click(within(activeRowEl).getByRole("button", { name: "Remove" }));
      expect(
        within(activeRowEl).getByText("They lose access immediately. To bring them back you will need to send a new invitation."),
      ).toBeInTheDocument();

      fireEvent.click(within(activeRowEl).getByRole("button", { name: "Confirm remove" }));
      expect(within(screen.getByRole("table")).getByText("other-admin@acme.example")).toBeInTheDocument();
      await waitFor(() => expect(removeMember).toHaveBeenCalledWith(WORKSPACE_ID, "admin-2"));
      await waitFor(() => expect(getWorkspaceMembers).toHaveBeenCalledTimes(3));
    });

    it("a failed revoke/remove renders inline and does not silently no-op", async () => {
      const invitedRow = member({ id: "inv-1", email: "pending@acme.example", role: "Procurement", status: "Invited" });
      const admin2 = member({ id: "admin-2", email: USER_LABEL, role: "Admin", status: "Active" });
      const getWorkspaceMembers = vi.fn().mockResolvedValue(roster([admin2, invitedRow]));
      const revokeInvitation = vi.fn().mockResolvedValue({ ok: false, statusCode: 404, error: "No pending invitation found for id inv-1." });
      renderMembers(mockApiClient({ getWorkspaceMembers, revokeInvitation }));

      const table = await screen.findByRole("table");
      const invitedRowEl = within(table).getByText("pending@acme.example").closest("tr");
      if (!invitedRowEl) throw new Error("invited row not found");
      fireEvent.click(within(invitedRowEl).getByRole("button", { name: "Revoke" }));
      fireEvent.click(within(invitedRowEl).getByRole("button", { name: "Confirm revoke" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("No pending invitation found for id inv-1.");
      expect(getWorkspaceMembers).toHaveBeenCalledTimes(1);
    });
  });

  describe("AC-8 -- the last Admin's Remove", () => {
    it("is a visibly disabled control with a .hint, never hidden and never clickable", async () => {
      const soleAdmin = member({ id: "solo-admin", email: USER_LABEL, role: "Admin", status: "Active" });
      const proc = member({ id: "proc-1", email: "buyer@acme.example", role: "Procurement", status: "Active" });
      renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn().mockResolvedValue(roster([soleAdmin, proc])) }));

      const table = await screen.findByRole("table");
      const adminRow = within(table).getByText(USER_LABEL).closest("tr");
      if (!adminRow) throw new Error("admin row not found");
      const removeButton = within(adminRow).getByRole("button", { name: "Remove" });
      expect(removeButton).toBeDisabled();
      expect(
        within(adminRow).getByText("This is the last Workspace Admin. Invite another Workspace Admin first."),
      ).toBeInTheDocument();

      const procRow = within(table).getByText("buyer@acme.example").closest("tr");
      if (!procRow) throw new Error("procurement row not found");
      expect(within(procRow).queryByRole("button", { name: "Remove" })).not.toBeInTheDocument();
    });
  });

  describe("AC-9 -- the Procurement variant", () => {
    it("sees the roster read-only: no Actions column, no invite pane, and a real mailto: Request access", async () => {
      const admin = member({ id: "admin-1", email: "boss@acme.example", role: "Admin", status: "Active" });
      const secondAdmin = member({ id: "admin-2", email: "second-admin@acme.example", role: "Admin", status: "Active" });
      const me = member({ id: "me-1", email: USER_LABEL, role: "Procurement", status: "Active" });
      renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn().mockResolvedValue(roster([admin, secondAdmin, me])) }), {
        role: "procurement",
      });

      const table = await screen.findByRole("table");
      expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual(["Member", "Role", "Status"]);
      expect(screen.queryByRole("complementary", { name: "Invite a colleague" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /^(Revoke|Remove)$/ })).not.toBeInTheDocument();

      const requestAccess = screen.getByRole("link", { name: "Request access" });
      expect(requestAccess).toHaveAttribute("href", "mailto:boss@acme.example,second-admin@acme.example");
    });
  });

  describe("AC-10 -- the roster is a list surface", () => {
    it("shows a skeleton while loading", () => {
      renderMembers(mockApiClient({ getWorkspaceMembers: vi.fn(() => new Promise<never>(() => {})) }));

      expect(screen.getByText("Loading roster…")).toBeInTheDocument();
      expect(screen.queryByRole("table")).not.toBeInTheDocument();
    });

    it("a failed re-read shows Retry and never falls back to the last known roster", async () => {
      const getWorkspaceMembers = vi
        .fn()
        .mockResolvedValueOnce(roster([member()]))
        .mockResolvedValueOnce({ ok: false, statusCode: 500, members: null, error: "Request failed with HTTP 500 Internal Server Error." });
      const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited());
      renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }));

      await screen.findByRole("table");
      fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
      fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

      await waitFor(() => expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument());
      expect(screen.getByText("Request failed with HTTP 500 Internal Server Error.")).toBeInTheDocument();
      expect(screen.queryByRole("table")).not.toBeInTheDocument();

      getWorkspaceMembers.mockResolvedValueOnce(roster([member()]));
      fireEvent.click(screen.getByRole("button", { name: "Retry" }));
      await screen.findByRole("table");
      expect(getWorkspaceMembers).toHaveBeenCalledTimes(3);
    });
  });
});
