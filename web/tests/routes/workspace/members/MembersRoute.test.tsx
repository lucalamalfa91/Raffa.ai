import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import MembersRoute from "../../../../src/routes/workspace/members";
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
    // Task E13/F09/US01/T03 (web-documents-v2): this suite does not exercise Documents -- bare
    // vi.fn() is enough, same convention as getPortfolio below.
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
    postRenewalAction: vi.fn(),
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askContigo: vi.fn(),
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
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

function renderMembers(apiClient: ApiClient) {
  return render(
    <MemoryRouter initialEntries={["/workspace/members"]}>
      <Routes>
        <Route path="/workspace/members" element={<MembersRoute apiClient={apiClient} userLabel={USER_LABEL} />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("MembersRoute", () => {
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

  it("AC-1: shows the members table with the current Admin as an Active row", () => {
    renderMembers(mockApiClient());

    expect(screen.getByRole("heading", { name: "Workspace & members" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Member" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Role" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Status" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Last active" })).toBeInTheDocument();
    expect(screen.getByText(USER_LABEL)).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Workspace Admin" })).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("AC-2: invite pane has email, Admin vs Procurement radios, permission summaries, and Send", () => {
    renderMembers(mockApiClient());

    expect(screen.getByLabelText("Email")).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: /workspace admin/i })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: /procurement/i })).toBeChecked();
    expect(screen.getByText(/including member management and invites/i)).toBeInTheDocument();
    expect(screen.getByText(/cannot invite or change roles/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /send invitation/i })).toBeInTheDocument();
  });

  it("shows the non-tenant-domain validation error without calling the API", () => {
    const inviteWorkspaceMember = vi.fn();
    renderMembers(mockApiClient({ inviteWorkspaceMember }));

    fireEvent.change(screen.getByLabelText("Email"), { target: { value: "buyer@gmail.com" } });
    fireEvent.click(screen.getByRole("button", { name: /send invitation/i }));

    expect(screen.getByRole("alert")).toHaveTextContent(/workspace's domain \(@acme\.example\)/i);
    expect(inviteWorkspaceMember).not.toHaveBeenCalled();
  });

  it("appends an Invited row after a successful invite (screens.md #2 sent state)", async () => {
    const inviteWorkspaceMember = vi.fn().mockResolvedValue(invited());
    renderMembers(mockApiClient({ inviteWorkspaceMember }));

    fireEvent.change(screen.getByLabelText("Email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: /send invitation/i }));

    await waitFor(() => {
      expect(screen.getByText("buyer@acme.example")).toBeInTheDocument();
    });
    expect(screen.getByText("Invited")).toBeInTheDocument();
    expect(inviteWorkspaceMember).toHaveBeenCalledWith(WORKSPACE_ID, {
      email: "buyer@acme.example",
      role: "Procurement",
    });
    expect(screen.getByLabelText("Email")).toHaveValue("");
  });

  it("surfaces a 400 from the invite API inline", async () => {
    const inviteWorkspaceMember = vi.fn().mockResolvedValue({
      ok: false,
      statusCode: 400,
      member: null,
      error: "buyer@acme.example already holds the Procurement role in this workspace.",
    });
    renderMembers(mockApiClient({ inviteWorkspaceMember }));

    fireEvent.change(screen.getByLabelText("Email"), { target: { value: "buyer@acme.example" } });
    fireEvent.click(screen.getByRole("button", { name: /send invitation/i }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent(/already holds the Procurement role/i);
    });
  });
});
