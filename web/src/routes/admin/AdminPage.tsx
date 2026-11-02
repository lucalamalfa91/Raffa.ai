import { Navigate } from "react-router-dom";
import { useMsal } from "@azure/msal-react";
import { InteractionStatus } from "@azure/msal-browser";
import type { ApiClient } from "../../api/client";
import { clearCurrentWorkspace } from "../signin/workspaceStore";
import AdminRoute from ".";
import "./admin.css";

/**
 * `/admin` outside any workspace: the Super Admin portal opens from the workspace picker (and from the
 * top nav), so it must not need a workspace to be selected. Signed out, it sends the visitor back to the
 * normal sign-in at `/`. The portal itself shows platform metadata only; the API answers 404 to anyone
 * who is not a platform admin.
 */
export default function AdminPage({ apiClient }: { apiClient: ApiClient }) {
  const { instance, accounts, inProgress } = useMsal();
  // After a full page load MSAL is still starting up (`inProgress` is not `None`) and `accounts` can be
  // empty for a moment: redirecting then sent a signed-in admin straight back to the workspace picker.
  // Only conclude "signed out" once MSAL is idle, and read the cache directly as a second source.
  const account = accounts[0] ?? instance.getAllAccounts()[0];
  if (!account) {
    return inProgress === InteractionStatus.None ? <Navigate to="/" replace /> : <main className="app-resolving" aria-busy="true" />;
  }

  return (
    <div className="admin-page">
      <header className="admin-page-bar">
        <a href="/" className="admin-page-back">
          ← Back to workspaces
        </a>
        <span className="admin-page-user">{account.username}</span>
        <button
          type="button"
          className="btn btn-ghost"
          onClick={() => {
            clearCurrentWorkspace();
            void instance.logoutRedirect();
          }}
        >
          Sign out
        </button>
      </header>
      <AdminRoute apiClient={apiClient} />
    </div>
  );
}
