import { useState, type FormEvent } from "react";
import type { ApiClient } from "../../api/client";
import { SignInStatementPanel } from "./SignInScreen";
import {
  clearCurrentWorkspace,
  loadCurrentWorkspace,
  loadKnownWorkspaces,
  rememberWorkspace,
  selectCurrentWorkspace,
  type CurrentWorkspace,
  type WorkspaceSummary,
} from "./workspaceStore";

export interface WorkspacePickerScreenProps {
  apiClient: ApiClient;
  /** MSAL `AccountInfo.homeAccountId` -- namespaces the known-workspaces cache per signed-in identity (see workspaceStore.ts). */
  accountKey: string;
  /** MSAL `AccountInfo.username`, shown so the picker confirms which identity is signed in. */
  accountLabel: string;
  onSignOut: () => void;
}

// Screen 1's third state (inputs/design/prototypes/screens.md #1): "...
// redirect state -> workspace list (name, contract count, currency/region,
// role tag) + 'Create a new workspace'." AC-1 of us-01-signin-workspace-
// picker. The workspace-row layout (name + meta line + role tag, one row per
// button, 1px divider) is quoted from the compiled prototype
// (inputs/design/prototypes/day1-demo.html); see workspaceStore.ts for why
// the list itself is a client-side cache rather than a server query.
export default function WorkspacePickerScreen({
  apiClient,
  accountKey,
  accountLabel,
  onSignOut,
}: WorkspacePickerScreenProps) {
  const [workspaces, setWorkspaces] = useState<WorkspaceSummary[]>(() => loadKnownWorkspaces(accountKey));
  const [current, setCurrent] = useState<CurrentWorkspace | null>(() => loadCurrentWorkspace());
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [newName, setNewName] = useState("");
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  const pickWorkspace = (workspace: CurrentWorkspace) => {
    selectCurrentWorkspace(workspace);
    setCurrent(workspace);
  };

  const handleSwitchWorkspace = () => {
    clearCurrentWorkspace();
    setCurrent(null);
  };

  const handleSignOut = () => {
    // A new sign-in should re-pick a workspace explicitly, not silently
    // resume whichever tenant the previous session left selected.
    clearCurrentWorkspace();
    onSignOut();
  };

  const handleCreate = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmedName = newName.trim();
    if (!trimmedName) {
      setCreateError("A workspace name is required.");
      return;
    }

    setCreating(true);
    setCreateError(null);
    const result = await apiClient.createWorkspace({ name: trimmedName });
    setCreating(false);

    if (!result.ok || !result.workspace) {
      setCreateError(result.error ?? "Could not create the workspace. Try again.");
      return;
    }

    const created: WorkspaceSummary = {
      id: result.workspace.id,
      name: result.workspace.name,
      createdAt: result.workspace.createdAt,
      contractCount: 0,
      roleLabel: "Workspace Admin",
    };
    setWorkspaces(rememberWorkspace(accountKey, created));
    setNewName("");
    setShowCreateForm(false);
    // Land the creator straight in the workspace they just made (parent
    // story: "so I land in the right tenant") rather than making them pick
    // their own brand-new, only workspace from a list of one.
    pickWorkspace({ id: created.id, name: created.name });
  };

  const cancelCreate = () => {
    setShowCreateForm(false);
    setCreateError(null);
    setNewName("");
  };

  if (current) {
    return (
      // Task E06/F06/US01/T01 (full-bleed-layout): shares SignInScreen's
      // canvas (SignInStatementPanel + .signin-action) instead of the old
      // standalone `.workspace-picker` narrow card -- see signin.css's
      // header comment.
      <main className="signin-screen">
        <SignInStatementPanel />
        <section className="signin-action">
          <div className="card">
            <p className="screen-kicker">Contigo</p>
            <h1 className="screen-title">You&apos;re in {current.name}</h1>
            <p className="micro-meta">Signed in as {accountLabel}.</p>
            <div className="workspace-actions">
              {/* Task E06/F03/US02/T01 (navigation-shell): the only way into
                  the app shell that task introduces (src/components/shell/).
                  A plain hard navigation, not a client-side router <Link> --
                  no router is mounted anywhere above this component. The
                  fresh page load re-runs src/App.tsx's account+workspace
                  check from scratch and mounts the shell; both MSAL's own
                  sessionStorage-cached account (src/auth/msalConfig.ts) and
                  this screen's own sessionStorage "current workspace" (this
                  file's selectCurrentWorkspace, above) survive a same-tab
                  navigation, so nothing needs to be re-entered. */}
              <a className="btn btn-primary" href="/">
                Continue to {current.name} →
              </a>
              <button type="button" className="btn btn-secondary" onClick={handleSwitchWorkspace}>
                Switch workspace
              </button>
              <button type="button" className="btn btn-ghost" onClick={handleSignOut}>
                Sign out
              </button>
            </div>
          </div>
        </section>
      </main>
    );
  }

  return (
    <main className="signin-screen">
      <SignInStatementPanel />
      <section className="signin-action">
        <h1 className="screen-title">Choose a workspace</h1>
        <p className="micro-meta">Signed in as {accountLabel}.</p>

        {workspaces.length === 0 && !showCreateForm && (
          <div className="empty-state">
            <h3>No workspaces yet</h3>
            <p>Create a workspace to start uploading contracts.</p>
            <button type="button" className="btn btn-primary" onClick={() => setShowCreateForm(true)}>
              + Create a new workspace
            </button>
          </div>
        )}

        {workspaces.length > 0 && (
          <div className="workspace-list">
            {workspaces.map((workspace) => (
              <button
                key={workspace.id}
                type="button"
                className="workspace-row"
                onClick={() => pickWorkspace({ id: workspace.id, name: workspace.name })}
              >
                <div>
                  <div className="workspace-row-name">{workspace.name}</div>
                  <div className="workspace-row-meta">
                    {workspace.contractCount} contracts
                    {workspace.currencyRegion ? ` · ${workspace.currencyRegion}` : ""}
                  </div>
                </div>
                <span className="tag tag-accent">{workspace.roleLabel}</span>
              </button>
            ))}
          </div>
        )}

        {workspaces.length > 0 && !showCreateForm && (
          <button type="button" className="btn btn-ghost workspace-create-cta" onClick={() => setShowCreateForm(true)}>
            + Create a new workspace
          </button>
        )}

        {showCreateForm && (
          <form onSubmit={(event) => void handleCreate(event)}>
            <div className="field">
              <label htmlFor="signin-new-workspace-name">Workspace name</label>
              <input
                id="signin-new-workspace-name"
                className="input"
                value={newName}
                onChange={(event) => setNewName(event.target.value)}
                disabled={creating}
                autoFocus
              />
            </div>
            {createError && <p className="signin-form-error">{createError}</p>}
            <div className="workspace-form-actions">
              <button type="submit" className="btn btn-primary" disabled={creating}>
                {creating ? "Creating…" : "Create workspace"}
              </button>
              <button type="button" className="btn btn-ghost" disabled={creating} onClick={cancelCreate}>
                Cancel
              </button>
            </div>
          </form>
        )}

        <button type="button" className="btn btn-ghost workspace-signout" onClick={handleSignOut}>
          Sign out
        </button>
      </section>
    </main>
  );
}
