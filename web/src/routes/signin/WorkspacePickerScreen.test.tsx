import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ApiClient, WorkspaceSummaryBody } from "../../api/client";
import WorkspacePickerScreen, {
  buildWorkspaceRowMeta,
  resolveWorkspaceSelection,
  type WorkspacePickerState,
} from "./WorkspacePickerScreen";

function row(overrides: Partial<WorkspaceSummaryBody> = {}): WorkspaceSummaryBody {
  return {
    id: "w-1",
    name: "Acme Procurement",
    createdAt: "2026-01-01T00:00:00Z",
    role: "Admin",
    contractCount: 0,
    ...overrides,
  };
}

/**
 * Task E14/F03/US02/T01 (wave w14 "workspace is real"; AC-2/AC-3/AC-5). `resolveWorkspaceSelection`
 * and `buildWorkspaceRowMeta` are pure functions -- tested directly and exhaustively here, without
 * mounting anything, exactly as `App.tsx`'s own resolution and this screen's own rendering both
 * consume them.
 */
describe("resolveWorkspaceSelection", () => {
  it("empty list -> create form", () => {
    expect(resolveWorkspaceSelection([], null)).toEqual({ kind: "empty" });
  });

  it("exactly one row -> enter it, no picker, even with an absent hint", () => {
    const only = row();
    expect(resolveWorkspaceSelection([only], null)).toEqual({ kind: "enter", workspace: only });
  });

  it("exactly one row -> enter it even when the hint names a different, non-existent workspace", () => {
    const only = row();
    expect(resolveWorkspaceSelection([only], { id: "stale", name: "Gone" })).toEqual({
      kind: "enter",
      workspace: only,
    });
  });

  it(">=2 rows with the hint present in the list -> enter the hint's own server row", () => {
    const a = row({ id: "w-1", name: "A" });
    const b = row({ id: "w-2", name: "B", contractCount: 3 });
    expect(resolveWorkspaceSelection([a, b], { id: "w-2", name: "B (stale name)" })).toEqual({
      kind: "enter",
      workspace: b,
    });
  });

  it(">=2 rows with the hint absent -> picker, discarding is the caller's job", () => {
    const a = row({ id: "w-1" });
    const b = row({ id: "w-2" });
    expect(resolveWorkspaceSelection([a, b], null)).toEqual({ kind: "pick", workspaces: [a, b] });
  });

  it(">=2 rows with the hint not found in the list -> picker (hint ∉ list)", () => {
    const a = row({ id: "w-1" });
    const b = row({ id: "w-2" });
    expect(resolveWorkspaceSelection([a, b], { id: "not-in-list", name: "Nope" })).toEqual({
      kind: "pick",
      workspaces: [a, b],
    });
  });
});

describe("buildWorkspaceRowMeta", () => {
  it("zero validated contracts -> the zero form, with no other segments when currency/country are absent", () => {
    expect(buildWorkspaceRowMeta(row({ contractCount: 0 }))).toBe("No validated contracts yet");
  });

  it("one validated contract -> the singular form", () => {
    expect(buildWorkspaceRowMeta(row({ contractCount: 1 }))).toBe("1 validated contract");
  });

  it("several validated contracts -> the plural form", () => {
    expect(buildWorkspaceRowMeta(row({ contractCount: 7 }))).toBe("7 validated contracts");
  });

  it("joins up to three segments with the count first, currency second, country name third", () => {
    expect(buildWorkspaceRowMeta(row({ contractCount: 2, currency: "CHF", country: "Switzerland" }))).toBe(
      "2 validated contracts · CHF · Switzerland",
    );
  });

  it("drops a null currency segment without leaving a gap, a dash, or a placeholder", () => {
    expect(buildWorkspaceRowMeta(row({ contractCount: 2, currency: null, country: "Switzerland" }))).toBe(
      "2 validated contracts · Switzerland",
    );
  });

  it("drops a null country segment without leaving a gap, a dash, or a placeholder", () => {
    expect(buildWorkspaceRowMeta(row({ contractCount: 2, currency: "CHF", country: null }))).toBe(
      "2 validated contracts · CHF",
    );
  });

  it("renders the business country name, never a cloud-region slug", () => {
    const meta = buildWorkspaceRowMeta(row({ contractCount: 1, currency: "CHF", country: "Switzerland" }));
    expect(meta).toContain("Switzerland");
    expect(meta).not.toContain("eu-west");
  });
});

/**
 * Rendering: the four `WorkspacePickerState` phases, and the segment/role-tag contract on an
 * actual rendered row. This screen no longer performs its own fetch (see its own header comment) --
 * `apiClient` here is exercised only for `createWorkspace`, the one call this component still makes.
 */
function noopApiClient(): ApiClient {
  return { createWorkspace: vi.fn() } as unknown as ApiClient;
}

function renderScreen(state: WorkspacePickerState, apiClient: ApiClient = noopApiClient()) {
  return render(
    <WorkspacePickerScreen
      apiClient={apiClient}
      accountLabel="user@example.test"
      onSignOut={vi.fn()}
      state={state}
      onRetry={vi.fn()}
      onEnter={vi.fn()}
    />,
  );
}

describe("WorkspacePickerScreen states", () => {
  it("resolving -> a loading skeleton, not the sign-in prompt and not a stale list", () => {
    renderScreen({ phase: "resolving" });
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /create your workspace/i })).not.toBeInTheDocument();
  });

  it("error -> ADR-018's error treatment, with a Retry that never falls back to a cached list", async () => {
    const onRetry = vi.fn();
    render(
      <WorkspacePickerScreen
        apiClient={noopApiClient()}
        accountLabel="user@example.test"
        onSignOut={vi.fn()}
        state={{ phase: "error", message: "Request failed with HTTP 503 Service Unavailable." }}
        onRetry={onRetry}
        onEnter={vi.fn()}
      />,
    );

    expect(screen.getByRole("alert")).toHaveTextContent(/503/);
    // Never a cached list -- there is nothing here that could even render one.
    expect(screen.queryByText(/validated contract/i)).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /retry/i }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("empty -> the create form is the empty state, with no separate 'no workspaces yet' screen", () => {
    renderScreen({ phase: "empty" });
    expect(screen.getByRole("heading", { name: /create your workspace/i })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /no workspaces yet/i })).not.toBeInTheDocument();
    expect(screen.getByLabelText(/^company$/i)).toBeInTheDocument();
    // ADR-012/ADR-020 w14 footers: the unconditional pointer back to a held invitation link.
    expect(screen.getByText(/open the invitation link your workspace admin shared with you/i)).toBeInTheDocument();
  });

  it("pick -> renders every row's segment meta and the server's own role tag, pass-through included", () => {
    const admin = row({ id: "w-1", name: "Admin Co", contractCount: 0, role: "Admin", currency: "CHF", country: "Switzerland" });
    const legal = row({ id: "w-2", name: "Legal Co", contractCount: 1, role: "Legal", currency: "EUR", country: "Italy" });
    renderScreen({ phase: "pick", workspaces: [admin, legal] });

    expect(screen.getByRole("heading", { name: /choose a workspace/i })).toBeInTheDocument();
    expect(screen.getByText("No validated contracts yet · CHF · Switzerland")).toBeInTheDocument();
    expect(screen.getByText("1 validated contract · EUR · Italy")).toBeInTheDocument();
    expect(screen.getByText("Workspace Admin")).toBeInTheDocument();
    // The server's role is shown verbatim for a role this app's nav does not model -- never
    // relabelled "Procurement" (ADR-019 w14 footer clause 5).
    expect(screen.getByText("Legal")).toBeInTheDocument();
  });

  it("pick -> clicking a row calls onEnter with that row's own server data", async () => {
    const onEnter = vi.fn();
    const only = row({ id: "w-2", name: "Second Co" });
    render(
      <WorkspacePickerScreen
        apiClient={noopApiClient()}
        accountLabel="user@example.test"
        onSignOut={vi.fn()}
        state={{ phase: "pick", workspaces: [row({ id: "w-1", name: "First Co" }), only] }}
        onRetry={vi.fn()}
        onEnter={onEnter}
      />,
    );

    await userEvent.click(screen.getByText("Second Co"));
    expect(onEnter).toHaveBeenCalledWith(only);
  });
});
