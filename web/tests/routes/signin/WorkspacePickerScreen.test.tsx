import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import WorkspacePickerScreen from "../../../src/routes/signin/WorkspacePickerScreen";
import type { ApiClient, CreateWorkspaceResult } from "../../../src/api/client";

function mockApiClient(createWorkspace: ApiClient["createWorkspace"] = vi.fn()): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace,
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    getPortfolio: vi.fn(),
    // Task E07/F02/US01/T01 (contract-360): this suite never reaches Contract 360 -- bare vi.fn().
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    // Task E07/F03/US01/T01 (field-review-correction): this suite never reaches the Review screen --
    // bare vi.fn() is enough, same convention as getContract360 above.
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    // Task E08/F01/US01/T01 (renewal-pipeline): this suite never reaches the Renewals screen --
    // bare vi.fn() is enough, same convention as getContract360 above.
    postRenewalAction: vi.fn(),
    // Task E08/F03/US01/T01 (quote-check-ui): this suite never reaches the Quote Check screen --
    // bare vi.fn() is enough, same convention as getCorrectionHistory above.
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askContigo: vi.fn(),
    // Task E08/F02/US01/T01 (savings-home): this suite never reaches Home's own fetch-outcome
    // matrix -- bare vi.fn() is enough, same convention as getContract360 above.
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
  };
}

const ACCOUNT_KEY = "test-home-account-id";

describe("WorkspacePickerScreen", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it("shows the empty state and no list when this account has no known workspaces", () => {
    render(
      <WorkspacePickerScreen
        apiClient={mockApiClient()}
        accountKey={ACCOUNT_KEY}
        accountLabel="user@example.test"
        onSignOut={vi.fn()}
      />,
    );

    expect(screen.getByText(/no workspaces yet/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /\+ create a new workspace/i })).toBeInTheDocument();
  });

  it("renders the same full-bleed statement-panel canvas as SignInScreen, not a standalone narrow card (E06/F06/US01/T01)", () => {
    const { container } = render(
      <WorkspacePickerScreen
        apiClient={mockApiClient()}
        accountKey={ACCOUNT_KEY}
        accountLabel="user@example.test"
        onSignOut={vi.fn()}
      />,
    );

    // Parent story AC: "Workspace picker: same canvas as the prototype, not
    // a narrow article." The statement panel (north-star sentence + 4 V1
    // jobs) is SignInScreen's own left column, shared via
    // SignInStatementPanel -- its presence here proves the picker no longer
    // falls back to the old standalone `.workspace-picker` card.
    expect(screen.getByText(/what we bought/i)).toBeInTheDocument();
    expect(container.querySelector("main.signin-screen")).toBeInTheDocument();
    expect(container.querySelector(".workspace-picker")).not.toBeInTheDocument();
  });

  it("creates a workspace, remembers it, and lands the user in it", async () => {
    const created: CreateWorkspaceResult = {
      ok: true,
      statusCode: 201,
      workspace: { id: "w-1", name: "Acme Procurement", createdAt: "2026-09-06T08:00:00Z" },
      error: null,
    };
    const createWorkspace = vi.fn().mockResolvedValue(created);

    render(
      <WorkspacePickerScreen
        apiClient={mockApiClient(createWorkspace)}
        accountKey={ACCOUNT_KEY}
        accountLabel="user@example.test"
        onSignOut={vi.fn()}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: /\+ create a new workspace/i }));
    await userEvent.type(screen.getByLabelText(/workspace name/i), "Acme Procurement");
    await userEvent.click(screen.getByRole("button", { name: /^create workspace$/i }));

    expect(createWorkspace).toHaveBeenCalledWith({ name: "Acme Procurement" });
    expect(await screen.findByRole("heading", { name: /you.re in acme procurement/i })).toBeInTheDocument();

    // Persisted for next time (workspaceStore.ts), not just held in memory.
    expect(window.localStorage.getItem(`contigo.signin.knownWorkspaces.${ACCOUNT_KEY}`)).toContain(
      "Acme Procurement",
    );
  });

  it("shows the API's validation error inline and keeps the form open on failure", async () => {
    const failed: CreateWorkspaceResult = {
      ok: false,
      statusCode: 400,
      workspace: null,
      error: "A workspace 'name' is required.",
    };
    const createWorkspace = vi.fn().mockResolvedValue(failed);

    render(
      <WorkspacePickerScreen
        apiClient={mockApiClient(createWorkspace)}
        accountKey={ACCOUNT_KEY}
        accountLabel="user@example.test"
        onSignOut={vi.fn()}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: /\+ create a new workspace/i }));
    await userEvent.type(screen.getByLabelText(/workspace name/i), "x");
    await userEvent.click(screen.getByRole("button", { name: /^create workspace$/i }));

    expect(await screen.findByText("A workspace 'name' is required.")).toBeInTheDocument();
    expect(screen.getByLabelText(/workspace name/i)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /you.re in/i })).not.toBeInTheDocument();
  });

  it("lists a previously remembered workspace and selects it on click", async () => {
    window.localStorage.setItem(
      `contigo.signin.knownWorkspaces.${ACCOUNT_KEY}`,
      JSON.stringify([
        {
          id: "w-2",
          name: "Globex Sandbox",
          createdAt: "2026-09-06T08:00:00Z",
          contractCount: 0,
          roleLabel: "Workspace Admin",
        },
      ]),
    );

    render(
      <WorkspacePickerScreen
        apiClient={mockApiClient()}
        accountKey={ACCOUNT_KEY}
        accountLabel="user@example.test"
        onSignOut={vi.fn()}
      />,
    );

    expect(screen.getByText("Globex Sandbox")).toBeInTheDocument();
    expect(screen.getByText(/0 contracts/i)).toBeInTheDocument();
    expect(screen.getByText("Workspace Admin")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /globex sandbox/i }));

    expect(await screen.findByRole("heading", { name: /you.re in globex sandbox/i })).toBeInTheDocument();
    expect(JSON.parse(window.sessionStorage.getItem("contigo.signin.currentWorkspace") ?? "null")).toEqual({
      id: "w-2",
      name: "Globex Sandbox",
    });
  });

  it("offers a Continue link into the app shell once a workspace is current (task E06/F03/US02/T01)", () => {
    window.sessionStorage.setItem("contigo.signin.currentWorkspace", JSON.stringify({ id: "w-2", name: "Globex Sandbox" }));

    render(
      <WorkspacePickerScreen
        apiClient={mockApiClient()}
        accountKey={ACCOUNT_KEY}
        accountLabel="user@example.test"
        onSignOut={vi.fn()}
      />,
    );

    const continueLink = screen.getByRole("link", { name: /continue to globex sandbox/i });
    expect(continueLink).toHaveAttribute("href", "/");
  });

  it("returns to the list from the confirmation panel via Switch workspace", async () => {
    window.sessionStorage.setItem("contigo.signin.currentWorkspace", JSON.stringify({ id: "w-2", name: "Globex Sandbox" }));

    render(
      <WorkspacePickerScreen
        apiClient={mockApiClient()}
        accountKey={ACCOUNT_KEY}
        accountLabel="user@example.test"
        onSignOut={vi.fn()}
      />,
    );

    expect(screen.getByRole("heading", { name: /you.re in globex sandbox/i })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /switch workspace/i }));

    expect(screen.getByRole("heading", { name: /choose a workspace/i })).toBeInTheDocument();
    expect(window.sessionStorage.getItem("contigo.signin.currentWorkspace")).toBeNull();
  });

  it("clears the current workspace and calls onSignOut", async () => {
    window.sessionStorage.setItem("contigo.signin.currentWorkspace", JSON.stringify({ id: "w-2", name: "Globex Sandbox" }));
    const onSignOut = vi.fn();

    render(
      <WorkspacePickerScreen
        apiClient={mockApiClient()}
        accountKey={ACCOUNT_KEY}
        accountLabel="user@example.test"
        onSignOut={onSignOut}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: /sign out/i }));

    expect(onSignOut).toHaveBeenCalledTimes(1);
    expect(window.sessionStorage.getItem("contigo.signin.currentWorkspace")).toBeNull();
  });
});
