import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import MembersRoute from "../../../../src/routes/workspace/members";
import type { ShellOutletContext } from "../../../../src/components/shell/shellContext";
import type { ApiClient, InviteWorkspaceMemberResult } from "../../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const USER_LABEL = "admin@acme.example";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
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
    askContigo: vi.fn(),
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

function invited(overrides: Partial<NonNullable<InviteWorkspaceMemberResult["member"]>> = {}): InviteWorkspaceMemberResult {
  return {
    ok: true,
    statusCode: 201,
    member: {
      id: "22222222-2222-2222-2222-222222222222",
      email: "buyer@acme.example",
      role: "Procurement",
      ...overrides,
    },
    error: null,
  };
}

/** `shell` renders the route under the app shell's own `<Outlet context>` (see `AppShell.tsx`); omitted = outside the shell, as before. */
function renderMembers(apiClient: ApiClient, shell?: ShellOutletContext) {
  const route = <Route path="/workspace/members" element={<MembersRoute apiClient={apiClient} userLabel={USER_LABEL} />} />;
  return render(
    <MemoryRouter initialEntries={["/workspace/members"]}>
      <Routes>{shell ? <Route element={<Outlet context={shell} />}>{route}</Route> : route}</Routes>
    </MemoryRouter>,
  );
}

describe("MembersRoute (V2, ADR-024 / screens-v2.md #10)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }),
    );
  });

  it("guards on no current workspace instead of sending an undefined tenant id", () => {
    window.sessionStorage.clear();
    const inviteWorkspaceMember = vi.fn();
    renderMembers(mockApiClient({ inviteWorkspaceMember }));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(inviteWorkspaceMember).not.toHaveBeenCalled();
  });

  it("renders the V2 header (Setup kicker, title, workspace · tenant line) and the three-column members table with the Admin as an Active row", () => {
    renderMembers(mockApiClient());

    expect(screen.getByText("Setup")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Workspace & members" })).toBeInTheDocument();
    expect(screen.getByText(`Acme Procurement · tenant ${WORKSPACE_ID}`)).toBeInTheDocument();

    const table = screen.getByRole("table");
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual(["Member", "Role", "Status"]);
    expect(within(table).getByText(USER_LABEL)).toBeInTheDocument();
    expect(within(table).getByText("You")).toBeInTheDocument();
    expect(within(table).getByRole("cell", { name: "Workspace Admin" })).toBeInTheDocument();
    expect(within(table).getByText("Active")).toHaveClass("tag-neutral");
  });

  it("shows the invite tip only while the knowledge base has no validated contract yet", () => {
    const { unmount } = renderMembers(mockApiClient(), { kbReady: false, validatedContractCount: 0 });
    expect(screen.getByRole("note")).toHaveTextContent(
      "Tip: invite the team once the first contract is validated — there is nothing for them to ask before that.",
    );
    unmount();

    renderMembers(mockApiClient(), { kbReady: true, validatedContractCount: 3 });
    expect(screen.queryByRole("note")).not.toBeInTheDocument();
  });

  it("invite pane: Work email with the tenant-domain placeholder, Procurement (default) then Workspace Admin radios with D8 summaries, Send invitation", () => {
    renderMembers(mockApiClient());

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

  it("shows the domain error, in the prototype's own words, without calling the API", () => {
    const inviteWorkspaceMember = vi.fn();
    renderMembers(mockApiClient({ inviteWorkspaceMember }));

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@gmail.com" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

    expect(screen.getByRole("alert")).toHaveTextContent("Use an @acme.example address.");
    expect(inviteWorkspaceMember).not.toHaveBeenCalled();
  });

  it("a successful invite posts the real request, appends an Invited row, clears the field and says 'Invitation sent.'", async () => {
    const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited({ role: "Admin" }));
    renderMembers(mockApiClient({ inviteWorkspaceMember }));

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("radio", { name: /workspace admin/i }));
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

    await waitFor(() => {
      expect(screen.getByText("buyer@acme.example")).toBeInTheDocument();
    });
    expect(inviteWorkspaceMember).toHaveBeenCalledWith(WORKSPACE_ID, { email: "buyer@acme.example", role: "Admin" });

    const table = screen.getByRole("table");
    expect(within(table).getByText("Invited")).toHaveClass("tag-accent");
    expect(within(table).getAllByRole("cell", { name: "Workspace Admin" })).toHaveLength(2);
    expect(screen.getByLabelText("Work email")).toHaveValue("");
    expect(screen.getByRole("status")).toHaveTextContent("Invitation sent.");

    // Editing the field again clears the confirmation.
    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "x" } });
    expect(screen.queryByText("Invitation sent.")).not.toBeInTheDocument();
  });

  it("surfaces a 400 from the invite API inline, with no 'Invitation sent.'", async () => {
    const inviteWorkspaceMember = vi.fn().mockResolvedValue({
      ok: false,
      statusCode: 400,
      member: null,
      error: "buyer@acme.example already holds the Procurement role in this workspace.",
    });
    renderMembers(mockApiClient({ inviteWorkspaceMember }));

    fireEvent.change(screen.getByLabelText("Work email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent(/already holds the Procurement role/i);
    });
    expect(screen.queryByText("Invitation sent.")).not.toBeInTheDocument();
  });
});
