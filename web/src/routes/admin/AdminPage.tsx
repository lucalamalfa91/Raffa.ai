import { Navigate } from "react-router-dom";
import { useMsal } from "@azure/msal-react";
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
  const { instance, accounts } = useMsal();
  if (!accounts[0]) return <Navigate to="/" replace />;

  return (
    <div className="admin-page">
      <header className="admin-page-bar">
        <a href="/" className="admin-page-back">
          ← Back to workspaces
        </a>
        <span className="admin-page-user">{accounts[0].username}</span>
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
