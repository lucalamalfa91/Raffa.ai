import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import MembersRoute from "../../../../src/routes/workspace/members";
import type { WorkspaceRole } from "../../../../src/components/shell/navItems";
import type { ShellOutletContext } from "../../../../src/components/shell/shellContext";
import type { ApiClient, GetWorkspaceMembersResult, InviteWorkspaceMemberResult, WorkspaceMemberBody } from "../../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const USER_LABEL = "admin@acme.example";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    listWorkspaces: vi.fn(),
    getWorkspaceMembers: vi.fn(),
    revokeInvitation: vi.fn(),
    removeMember: vi.fn(),
    getInvitation: vi.fn(),
    acceptInvitation: vi.fn(),
    acceptPendingInvitation: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    deleteAllDocuments: vi.fn(),
    prioritiseDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn(),
    getContractStrategy: vi.fn(),
    validateDocument: vi.fn(),
    postRenewalAction: vi.fn(),
    getQuote: vi.fn(),
    getNegotiationSteps: vi.fn(),
    putNegotiationSteps: vi.fn(),
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
    deleteConversation: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    getQuoteBenchmarkHistory: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web): this suite never reaches the Renewals screen -- bare
    // vi.fn() is enough, same convention as the other unexercised calls above.
    getRenewalNegotiationTodos: vi.fn(),
    tickRenewalNegotiationTodo: vi.fn(),
    ...overrides,
  };
}

// Fix 2026-09-15: `id` (the roster row's own stable WorkspaceUser id) and `membershipId`/
// `invitationId` (the action ids DELETE/revoke actually take) are deliberately DIFFERENT default
// values here -- not the same string reused for both, which is exactly the fixture shape that let
// MembersTable.tsx send `member.id` as the action id for months without a single test catching it
// (confirmed live on dev). Any future regression back to `member.id` now fails every test below
// that asserts what removeMember/revokeInvitation were called with.
function activeMember(overrides: Partial<WorkspaceMemberBody> = {}): WorkspaceMemberBody {
  return {
    id: "admin-user-1",
    membershipId: "admin-membership-1",
    email: USER_LABEL,
    role: "Admin",
    status: "Active",
    ...overrides,
  };
}

function invitedMember(overrides: Partial<WorkspaceMemberBody> = {}): WorkspaceMemberBody {
  return {
    id: "invite-user-1",
    invitationId: "invite-1",
    email: "invitee@acme.example",
    role: "Procurement",
    status: "Invited",
    ...overrides,
  };
}

function membersOk(members: WorkspaceMemberBody[]): GetWorkspaceMembersResult {
  return { ok: true, statusCode: 200, members, error: null };
}

function invited(overrides: Partial<NonNullable<InviteWorkspaceMemberResult["member"]>> = {}): InviteWorkspaceMemberResult {
  return {
    ok: true,
    statusCode: 201,
    member: {
      id: "new-invite-1",
      email: "buyer@acme.example",
      role: "Procurement",
      expiresAt: "2099-01-01T00:00:00Z",
      acceptUrl: "/invite/accept#00000000000000000000000000000000.fake-secret",
      // Task E17/F02/US01/T01: `deliveryOutcome` is the discriminant; `mailDelivered` agrees with it
      // by the server's own biconditional and is never read by the pane.
      deliveryOutcome: "no_transport",
      mailDelivered: false,
      identityProvisioned: false,
      ...overrides,
    },
    failureReason: null,
    error: null,
  };
}

interface RenderOptions {
  shell?: ShellOutletContext;
  role?: WorkspaceRole;
  workspaceId?: string;
  workspaceName?: string;
}

/** `shell` renders the route under the app shell's own `<Outlet context>` (see `AppShell.tsx`); omitted = outside the shell, as before. `workspaceId` omitted (not defaulted) is the one way to exercise the pre-merge "prop not threaded yet" state (this file's own `index.tsx` header comment) without a default parameter silently papering over it. */
function renderMembers(apiClient: ApiClient, opts: RenderOptions = {}) {
  const role = opts.role ?? "admin";
  const route = (
    <Route
      path="/workspace/members"
      element={<MembersRoute apiClient={apiClient} userLabel={USER_LABEL} workspaceId={opts.workspaceId} workspaceName={opts.workspaceName} role={role} />}
    />
  );
  return render(
    <MemoryRouter initialEntries={["/workspace/members"]}>
      <Routes>{opts.shell ? <Route element={<Outlet context={opts.shell} />}>{route}</Route> : route}</Routes>
    </MemoryRouter>,
  );
}

describe("MembersRoute (V2, ADR-020/ADR-025/ADR-026 w14 footers; screens-v2.md #10)", () => {
  it("renders the no-workspace state and calls neither the roster nor the invite API when workspaceId is not yet threaded", () => {
    const getWorkspaceMembers = vi.fn();
    const inviteWorkspaceMember = vi.fn();
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(getWorkspaceMembers).not.toHaveBeenCalled();
    expect(inviteWorkspaceMember).not.toHaveBeenCalled();
  });

  it("renders the header and the roster from getWorkspaceMembers (AC-1) -- never an optimistic local guess", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    renderMembers(mockApiClient({ getWorkspaceMembers }), { workspaceId: WORKSPACE_ID });

    expect(screen.getByText("Setup")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Workspace & members" })).toBeInTheDocument();
    expect(screen.getByText(`tenant ${WORKSPACE_ID}`)).toBeInTheDocument();

    const table = await screen.findByRole("table");
    await waitFor(() => expect(getWorkspaceMembers).toHaveBeenCalledWith(WORKSPACE_ID));
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual(["Member", "Role", "Status", "Actions"]);
    expect(within(table).getByText(USER_LABEL)).toBeInTheDocument();
    expect(within(table).getByText("You")).toBeInTheDocument();
    expect(within(table).getByRole("cell", { name: "Workspace Admin" })).toBeInTheDocument();
    expect(within(table).getByText("Active")).toHaveClass("tag-neutral");
  });

  it("uses the workspace name in the header when the sibling also threads one, and degrades to the tenant id alone otherwise", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    renderMembers(mockApiClient({ getWorkspaceMembers }), { workspaceId: WORKSPACE_ID, workspaceName: "Acme Procurement" });
    expect(screen.getByText(`Acme Procurement · tenant ${WORKSPACE_ID}`)).toBeInTheDocument();
  });

  it("shows a loading skeleton, then an error state with Retry that never falls back to a stale roster (ADR-018 :112-119)", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValueOnce({ ok: false, statusCode: 503, members: null, error: "Raffa.ai is temporarily unavailable." });
    renderMembers(mockApiClient({ getWorkspaceMembers }), { workspaceId: WORKSPACE_ID });

    expect(screen.getByText("Loading members…")).toBeInTheDocument();

    expect(await screen.findByRole("heading", { name: "Members unavailable" })).toBeInTheDocument();
    expect(screen.getByText(/temporarily unavailable/i)).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();

    getWorkspaceMembers.mockResolvedValueOnce(membersOk([activeMember()]));
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));

    expect(await screen.findByRole("table")).toBeInTheDocument();
    expect(getWorkspaceMembers).toHaveBeenCalledTimes(2);
  });

  it("shows the invite tip only while the knowledge base has no validated contract yet", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    const { unmount } = renderMembers(mockApiClient({ getWorkspaceMembers }), {
      workspaceId: WORKSPACE_ID,
      shell: { kbReady: false, validatedContractCount: 0 },
    });
    await screen.findByRole("table");
    expect(screen.getByRole("note")).toHaveTextContent(
      "Tip: invite the team once the first contract is validated — there is nothing for them to ask before that.",
    );
    unmount();

    renderMembers(mockApiClient({ getWorkspaceMembers }), { workspaceId: WORKSPACE_ID, shell: { kbReady: true, validatedContractCount: 3 } });
    await screen.findByRole("table");
    expect(screen.queryByRole("note")).not.toBeInTheDocument();
  });

  it("invite pane: Work email with the tenant-domain placeholder, Procurement (default) then Workspace Admin radios with D8 summaries, Send invitation", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    renderMembers(mockApiClient({ getWorkspaceMembers }), { workspaceId: WORKSPACE_ID });
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

  it("a cross-domain address renders a non-blocking warning and leaves Send invitation enabled; a malformed address still blocks (AC-5)", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited({ email: "buyer@gmail.com", mailDelivered: true }));
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }), { workspaceId: WORKSPACE_ID });
    await screen.findByRole("table");

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@gmail.com" } });
    expect(screen.getByText("buyer@gmail.com is outside acme.example. They will get full Procurement access to this workspace.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Send invitation" })).not.toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));
    await waitFor(() => expect(inviteWorkspaceMember).toHaveBeenCalledWith(WORKSPACE_ID, { email: "buyer@gmail.com", role: "Procurement" }));

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "not-an-email" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Enter a valid email address.");
    expect(inviteWorkspaceMember).toHaveBeenCalledTimes(1);
  });

  it("a successful invite re-reads the roster instead of appending a row locally, and renders 'Invitation sent to {email}.' on deliveryOutcome 'sent' with the copyable link", async () => {
    const getWorkspaceMembers = vi
      .fn()
      .mockResolvedValueOnce(membersOk([activeMember()]))
      .mockResolvedValueOnce(membersOk([activeMember(), invitedMember({ email: "buyer@acme.example" })]));
    const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited({ email: "buyer@acme.example", deliveryOutcome: "sent", mailDelivered: true }));
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }), { workspaceId: WORKSPACE_ID });
    await screen.findByRole("table");

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

    await waitFor(() => expect(getWorkspaceMembers).toHaveBeenCalledTimes(2));
    expect(await screen.findByText("buyer@acme.example")).toBeInTheDocument();
    expect(screen.getByText("Invitation sent to buyer@acme.example.")).toBeInTheDocument();
    expect(screen.getByLabelText("Work email")).toHaveValue("");
    expect(screen.getByLabelText("Invitation link")).toBeInTheDocument();
  });

  it("an empty second click keeps the last accept link instead of wiping it", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited({ email: "buyer@acme.example", deliveryOutcome: "no_transport" }));
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }), { workspaceId: WORKSPACE_ID });
    await screen.findByRole("table");

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));
    expect(await screen.findByLabelText("Invitation link")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));
    expect(screen.getByRole("alert")).toHaveTextContent("An email is required.");
    expect(screen.getByLabelText("Invitation link")).toBeInTheDocument();
    expect(inviteWorkspaceMember).toHaveBeenCalledTimes(1);
  });

  // Task E17/F02/US01/T01 (ADR-020 w15 §3.3/§3.4): the second outcome -- the mail failed -- keeps
  // the link block as the remedy and offers no "Try sending again".
  it("deliveryOutcome 'mail_failed' renders 'Invitation created, but the email could not be sent.' with the copyable link, and no retry affordance", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited({ email: "buyer@acme.example", deliveryOutcome: "mail_failed", mailDelivered: false }));
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }), { workspaceId: WORKSPACE_ID });
    await screen.findByRole("table");

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

    expect(await screen.findByText("Invitation created, but the email could not be sent.")).toBeInTheDocument();
    expect(screen.getByLabelText("Invitation link")).toBeInTheDocument();
    expect(screen.queryByText(/try sending again/i)).not.toBeInTheDocument();
  });

  // ADR-020 w15 §3.6: the identity sentence is keyed to the 201's own `identityProvisioned`, on
  // every outcome, and never appears while provisioning is not configured.
  it("renders the one-time-code sentence only while identityProvisioned is true, identically for a created and an already-present guest", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    const inviteWorkspaceMember = vi
      .fn()
      .mockResolvedValueOnce(invited({ email: "buyer@acme.example", deliveryOutcome: "sent", mailDelivered: true, identityProvisioned: true }))
      .mockResolvedValueOnce(invited({ email: "other@acme.example", deliveryOutcome: "no_transport", identityProvisioned: false }));
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }), { workspaceId: WORKSPACE_ID });
    await screen.findByRole("table");

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));
    expect(await screen.findByText("They will get a one-time code from Microsoft the first time they sign in.")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "other@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));
    expect(await screen.findByText("Invitation ready for other@acme.example.")).toBeInTheDocument();
    expect(screen.queryByText(/one-time code/i)).not.toBeInTheDocument();
  });

  // ADR-020 w15 §3.5: each 502 reason renders its designed copy with "No invitation was created.";
  // an unknown value hits the catch-all and never shows a raw enum; no link renders.
  it.each([
    ["consent_missing", "Raffa.ai is not allowed to add guests to your company directory yet. A tenant administrator has to approve that permission."],
    ["provisioning_failed", "Your company directory would not add buyer@acme.example. Check the address, or ask a tenant administrator."],
    ["directory_unavailable", "Your company directory could not be reached. Try again in a few minutes."],
    ["some_future_reason", "Raffa.ai could not create this invitation."],
  ])("a 502 with failureReason %s renders its designed copy and 'No invitation was created.'", async (failureReason, copy) => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    const inviteWorkspaceMember = vi.fn().mockResolvedValue({ ok: false, statusCode: 502, member: null, failureReason, error: null });
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }), { workspaceId: WORKSPACE_ID });
    await screen.findByRole("table");

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(copy);
    expect(alert).toHaveTextContent("No invitation was created.");
    expect(alert).not.toHaveTextContent(failureReason);
    expect(screen.queryByLabelText("Invitation link")).not.toBeInTheDocument();
    expect(screen.queryByText(/invitation (sent|ready|created)/i)).not.toBeInTheDocument();
  });

  it("deliveryOutcome 'no_transport' renders 'Invitation ready for {email}.' with the copyable link and expiry -- the word 'sent' appears nowhere", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited({ email: "buyer@acme.example", deliveryOutcome: "no_transport", mailDelivered: false }));
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }), { workspaceId: WORKSPACE_ID });
    await screen.findByRole("table");

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

    expect(await screen.findByText("Invitation ready for buyer@acme.example.")).toBeInTheDocument();
    expect(screen.queryByText(/\bsent\b/i)).not.toBeInTheDocument();

    const expectedLink = new URL("/invite/accept#00000000000000000000000000000000.fake-secret", window.location.origin).toString();
    expect(screen.getByLabelText("Invitation link")).toHaveValue(expectedLink);
    expect(screen.getByText("It expires 01/01/2099, can be used once, and is not shown again.")).toBeInTheDocument();

    const clipboardSpy = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: clipboardSpy } });
    fireEvent.click(screen.getByRole("button", { name: "Copy link" }));
    await waitFor(() => expect(clipboardSpy).toHaveBeenCalledWith(expectedLink));
    await waitFor(() => expect(screen.getByRole("button", { name: "Copied" })).toBeInTheDocument());
  });

  it("a failed invite request never claims a mail either way (index.tsx:63's own defect this task closes)", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    const inviteWorkspaceMember = vi.fn().mockResolvedValue({ ok: false, statusCode: 500, member: null, failureReason: null, error: null });
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }), { workspaceId: WORKSPACE_ID });
    await screen.findByRole("table");

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The invitation could not be created.");
    expect(screen.queryByText(/could not be sent/i)).not.toBeInTheDocument();
  });

  it("surfaces a 409 from the invite API inline, with no success outcome shown", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    const inviteWorkspaceMember = vi.fn().mockResolvedValue({
      ok: false,
      statusCode: 409,
      member: null,
      failureReason: null,
      error: "buyer@acme.example already holds the Procurement role in this workspace.",
    });
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }), { workspaceId: WORKSPACE_ID });
    await screen.findByRole("table");

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/already holds the Procurement role/i);
    expect(screen.queryByText(/^Invitation (sent|ready)/)).not.toBeInTheDocument();
  });

  it("revoke (Invited) and remove (Active) confirm inline with different consequence copy, and neither call fires before the confirm click (AC-6/AC-7)", async () => {
    const admin = activeMember();
    const invitee = invitedMember({ invitationId: "invite-9", email: "invitee@acme.example" });
    const activeOther = activeMember({ membershipId: "member-9", email: "other@acme.example", role: "Procurement" });
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([admin, invitee, activeOther]));
    const revokeInvitation = vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null });
    const removeMember = vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null });
    renderMembers(mockApiClient({ getWorkspaceMembers, revokeInvitation, removeMember }), { workspaceId: WORKSPACE_ID });

    const table = await screen.findByRole("table");
    const inviteeRow = within(table).getByText("invitee@acme.example").closest("tr");
    if (!inviteeRow) throw new Error("invitee row not found");
    fireEvent.click(within(inviteeRow).getByRole("button", { name: "Revoke" }));
    expect(within(inviteeRow).getByText("Revoke the invitation for invitee@acme.example?")).toBeInTheDocument();
    expect(within(inviteeRow).getByText("Their link stops working. They never had access to this workspace.")).toBeInTheDocument();
    expect(revokeInvitation).not.toHaveBeenCalled();

    fireEvent.click(within(inviteeRow).getByRole("button", { name: "Yes, revoke" }));
    await waitFor(() => expect(revokeInvitation).toHaveBeenCalledWith(WORKSPACE_ID, "invite-9"));
    await waitFor(() => expect(getWorkspaceMembers).toHaveBeenCalledTimes(2));

    const tableAfterRevoke = await screen.findByRole("table");
    const otherRow = within(tableAfterRevoke).getByText("other@acme.example").closest("tr");
    if (!otherRow) throw new Error("other row not found");
    fireEvent.click(within(otherRow).getByRole("button", { name: "Remove" }));
    expect(within(otherRow).getByText("Remove other@acme.example?")).toBeInTheDocument();
    expect(within(otherRow).getByText("They lose access immediately. To bring them back you will need to send a new invitation.")).toBeInTheDocument();
    expect(removeMember).not.toHaveBeenCalled();

    fireEvent.click(within(otherRow).getByRole("button", { name: "Yes, remove" }));
    await waitFor(() => expect(removeMember).toHaveBeenCalledWith(WORKSPACE_ID, "member-9"));
  });

  it("Send a new invitation on an Invited row re-issues by replacement and shows the new copyable link", async () => {
    const admin = activeMember();
    const invitee = invitedMember({ invitationId: "invite-9", email: "invitee@acme.example", role: "Procurement" });
    const getWorkspaceMembers = vi
      .fn()
      .mockResolvedValueOnce(membersOk([admin, invitee]))
      .mockResolvedValueOnce(membersOk([admin, invitee]));
    const inviteWorkspaceMember = vi.fn().mockResolvedValue(
      invited({
        email: "invitee@acme.example",
        deliveryOutcome: "no_transport",
        acceptUrl: "/invite/accept#aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.reissued-secret",
      }),
    );
    renderMembers(mockApiClient({ getWorkspaceMembers, inviteWorkspaceMember }), { workspaceId: WORKSPACE_ID });

    const table = await screen.findByRole("table");
    const inviteeRow = within(table).getByText("invitee@acme.example").closest("tr");
    if (!inviteeRow) throw new Error("invitee row not found");
    fireEvent.click(within(inviteeRow).getByRole("button", { name: "Send a new invitation" }));
    expect(within(inviteeRow).getByText("Send a new invitation to invitee@acme.example?")).toBeInTheDocument();
    expect(within(inviteeRow).getByText("The link you already shared stops working.")).toBeInTheDocument();
    expect(inviteWorkspaceMember).not.toHaveBeenCalled();

    fireEvent.click(within(inviteeRow).getByRole("button", { name: "Yes, send a new invitation" }));
    await waitFor(() =>
      expect(inviteWorkspaceMember).toHaveBeenCalledWith(WORKSPACE_ID, { email: "invitee@acme.example", role: "Procurement" }),
    );
    expect(await screen.findByText("Invitation ready for invitee@acme.example.")).toBeInTheDocument();
    expect(screen.getByLabelText("Invitation link")).toHaveValue(
      new URL("/invite/accept#aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.reissued-secret", window.location.origin).toString(),
    );
  });

  it("the last Admin's Remove is a disabled control with a .hint, never hidden and never a click that can 409 (AC-8)", async () => {
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([activeMember()]));
    const removeMember = vi.fn();
    renderMembers(mockApiClient({ getWorkspaceMembers, removeMember }), { workspaceId: WORKSPACE_ID });

    const table = await screen.findByRole("table");
    const adminRow = within(table).getByText(USER_LABEL).closest("tr");
    if (!adminRow) throw new Error("admin row not found");
    const removeButton = within(adminRow).getByRole("button", { name: "Remove" });
    expect(removeButton).toBeDisabled();
    expect(within(adminRow).getByText("This is the last Workspace Admin. Invite another Workspace Admin first.")).toHaveClass("hint");

    fireEvent.click(removeButton);
    expect(removeMember).not.toHaveBeenCalled();
  });

  it("a Procurement member sees the roster read-only, no Actions column, no invite pane, and a real mailto Request access (AC-9)", async () => {
    const admin = activeMember({ id: "admin-1", email: "boss@acme.example" });
    const self = activeMember({ id: "self-1", email: USER_LABEL, role: "Procurement" });
    const getWorkspaceMembers = vi.fn().mockResolvedValue(membersOk([admin, self]));
    renderMembers(mockApiClient({ getWorkspaceMembers }), { workspaceId: WORKSPACE_ID, role: "procurement" });

    const table = await screen.findByRole("table");
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual(["Member", "Role", "Status"]);
    expect(within(table).getByText("You")).toBeInTheDocument();
    expect(screen.queryByRole("complementary", { name: "Invite a colleague" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Send invitation" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remove" })).not.toBeInTheDocument();

    const requestAccess = screen.getByRole("link", { name: "Request access" });
    expect(requestAccess.getAttribute("href")).toContain("mailto:boss@acme.example");
  });
});
