import { useCallback, useEffect, useState, type FormEvent } from "react";
import type {
  AdminCallResult,
  AdminInvitationBody,
  AdminIssuedInvitationBody,
  AdminOverviewBody,
  AdminUserBody,
  AdminWorkspaceBody,
  ApiClient,
} from "../../api/client";
import "./admin.css";

export interface AdminRouteProps {
  apiClient: ApiClient;
}

type Tab = "overview" | "workspaces" | "invitations" | "users";
const TABS: ReadonlyArray<{ id: Tab; label: string }> = [
  { id: "overview", label: "Overview" },
  { id: "workspaces", label: "Workspaces" },
  { id: "invitations", label: "Invitations" },
  { id: "users", label: "Users" },
];

const formatDate = (iso: string | null): string => (iso ? new Date(iso).toLocaleDateString() : "—");

/** Loads one admin list; `reload` re-runs it. A 404 means the caller is not a platform admin. */
function useAdminData<T>(load: (() => Promise<AdminCallResult<T>>) | undefined) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const reload = useCallback(async () => {
    if (!load) {
      setError("This build has no admin portal.");
      return;
    }
    const result = await load();
    if (result.ok) {
      setData(result.body);
      setError(null);
    } else {
      setError(result.statusCode === 404 ? "You are not a Raffa platform admin." : (result.error ?? "Could not load."));
    }
  }, [load]);
  useEffect(() => {
    void reload();
  }, [reload]);
  return { data, error, reload };
}

/**
 * Portal admin (Raffa staff; route `/admin`). Super Admin only: overview, workspace registry (metadata
 * only, with suspend/reactivate), prospect invitations and the people who accepted one. It never opens a
 * customer workspace and shows no customer content. The server answers 404 to anyone who is not a
 * platform admin, so this screen is only a view over what the API allows.
 */
export default function AdminRoute({ apiClient }: AdminRouteProps) {
  const [tab, setTab] = useState<Tab>("overview");
  return (
    <div className="admin-screen">
      <header className="screen-header">
        <div className="screen-kicker">Raffa staff</div>
        <h1 className="screen-title">Admin portal</h1>
      </header>
      <nav className="admin-tabs" aria-label="Admin sections">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            className={t.id === tab ? "admin-tab is-active" : "admin-tab"}
            aria-pressed={t.id === tab}
            onClick={() => setTab(t.id)}
          >
            {t.label}
          </button>
        ))}
      </nav>
      {tab === "overview" ? <OverviewPanel apiClient={apiClient} /> : null}
      {tab === "workspaces" ? <WorkspacesPanel apiClient={apiClient} /> : null}
      {tab === "invitations" ? <InvitationsPanel apiClient={apiClient} /> : null}
      {tab === "users" ? <UsersPanel apiClient={apiClient} /> : null}
    </div>
  );
}

function Problem({ message }: { message: string }) {
  return (
    <p role="alert" className="admin-error">
      {message}
    </p>
  );
}

function OverviewPanel({ apiClient }: AdminRouteProps) {
  const { data, error } = useAdminData<AdminOverviewBody>(apiClient.getAdminOverview);
  if (error) return <Problem message={error} />;
  if (!data) return <p className="hint">Loading…</p>;
  const cards: ReadonlyArray<[string, number]> = [
    ["Workspaces", data.totalWorkspaces],
    ["Active", data.activeWorkspaces],
    ["Suspended", data.suspendedWorkspaces],
    ["Registered users", data.registeredUsers],
    ["Pending invitations", data.pendingInvitations],
  ];
  return (
    <section className="admin-cards" aria-label="Overview">
      {cards.map(([label, value]) => (
        <div key={label} className="admin-stat">
          <div className="admin-stat-value">{value}</div>
          <div className="admin-stat-label">{label}</div>
        </div>
      ))}
    </section>
  );
}

function WorkspacesPanel({ apiClient }: AdminRouteProps) {
  const { data, error, reload } = useAdminData<{ workspaces: AdminWorkspaceBody[] }>(apiClient.listAdminWorkspaces);
  const [actionError, setActionError] = useState<string | null>(null);

  const change = async (workspace: AdminWorkspaceBody) => {
    const suspend = workspace.status === "active";
    if (suspend && !window.confirm(`Suspend ${workspace.name}? Its members will lose access until you reactivate it.`)) return;
    const result = await apiClient.setAdminWorkspaceStatus?.(workspace.id, suspend ? "suspend" : "reactivate");
    setActionError(result?.ok ? null : (result?.error ?? "The change was not applied."));
    await reload();
  };

  if (error) return <Problem message={error} />;
  if (!data) return <p className="hint">Loading…</p>;
  return (
    <section className="admin-card" aria-label="Workspaces">
      {actionError ? <Problem message={actionError} /> : null}
      {data.workspaces.length === 0 ? (
        <p className="hint">No workspaces yet.</p>
      ) : (
        <table className="table admin-table">
          <thead>
            <tr>
              <th>Company</th>
              <th>Workspace ID</th>
              <th>Administrator</th>
              <th>Created</th>
              <th>Status</th>
              <th>
                <span className="visually-hidden">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {data.workspaces.map((w) => (
              <tr key={w.id}>
                <td>{w.name}</td>
                <td>
                  <code>{w.id}</code>
                </td>
                <td>{w.adminEmail ?? "—"}</td>
                <td>{formatDate(w.createdAt)}</td>
                <td>{w.status}</td>
                <td>
                  <button type="button" className="btn" onClick={() => void change(w)}>
                    {w.status === "active" ? "Suspend" : "Reactivate"}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

function InvitationsPanel({ apiClient }: AdminRouteProps) {
  const { data, error, reload } = useAdminData<{ invitations: AdminInvitationBody[] }>(apiClient.listAdminInvitations);
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [issued, setIssued] = useState<AdminIssuedInvitationBody | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const finish = async (result: AdminCallResult<AdminIssuedInvitationBody> | AdminCallResult<null> | undefined) => {
    if (!result?.ok) {
      setActionError(result?.error ?? "The change was not applied.");
      return;
    }
    setActionError(null);
    await reload();
  };

  const invite = async (event: FormEvent) => {
    event.preventDefault();
    if (!apiClient.createAdminInvitation || email.trim().length === 0) return;
    setBusy(true);
    const result = await apiClient.createAdminInvitation(email.trim());
    setBusy(false);
    if (result.ok && result.body) {
      setIssued(result.body);
      setEmail("");
    }
    await finish(result);
  };

  const regenerate = async (id: string) => {
    const result = await apiClient.regenerateAdminInvitation?.(id);
    if (result?.ok && result.body) setIssued(result.body);
    await finish(result);
  };

  if (error) return <Problem message={error} />;
  return (
    <section className="admin-card" aria-label="Invitations">
      <form onSubmit={(event) => void invite(event)} className="admin-inline-form">
        <label htmlFor="admin-invite-email">Prospect email</label>
        <input
          id="admin-invite-email"
          className="input"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="name@company.com"
        />
        <button type="submit" className="btn btn-primary" disabled={busy || email.trim().length === 0}>
          Create invitation
        </button>
      </form>
      {actionError ? <Problem message={actionError} /> : null}
      {issued ? (
        <div role="status" className="admin-result">
          <p>
            Invitation for <strong>{issued.email}</strong>, valid until {formatDate(issued.expiresAt)}. Copy the link now; it is not shown again.
          </p>
          <label htmlFor="admin-invite-link">Invitation link</label>
          <input id="admin-invite-link" className="input" readOnly value={issued.acceptUrl} onFocus={(e) => e.currentTarget.select()} />
          <button type="button" className="btn" onClick={() => void navigator.clipboard?.writeText(issued.acceptUrl)}>
            Copy link
          </button>
        </div>
      ) : null}
      {!data ? (
        <p className="hint">Loading…</p>
      ) : data.invitations.length === 0 ? (
        <p className="hint">No invitations yet.</p>
      ) : (
        <table className="table admin-table">
          <thead>
            <tr>
              <th>Email</th>
              <th>Status</th>
              <th>Created</th>
              <th>Expires</th>
              <th>
                <span className="visually-hidden">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {data.invitations.map((i) => (
              <tr key={i.id}>
                <td>{i.email}</td>
                <td>{i.status}</td>
                <td>{formatDate(i.createdAt)}</td>
                <td>{formatDate(i.expiresAt)}</td>
                <td>
                  {i.status === "accepted" ? null : (
                    <>
                      <button type="button" className="btn" onClick={() => void regenerate(i.id)}>
                        Regenerate link
                      </button>
                      {i.status === "pending" ? (
                        <button type="button" className="btn" onClick={() => void apiClient.revokeAdminInvitation?.(i.id).then(finish)}>
                          Revoke
                        </button>
                      ) : null}
                    </>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

function UsersPanel({ apiClient }: AdminRouteProps) {
  const { data, error } = useAdminData<{ users: AdminUserBody[] }>(apiClient.listAdminUsers);
  if (error) return <Problem message={error} />;
  if (!data) return <p className="hint">Loading…</p>;
  return (
    <section className="admin-card" aria-label="Users">
      {data.users.length === 0 ? (
        <p className="hint">Nobody has accepted an invitation yet.</p>
      ) : (
        <table className="table admin-table">
          <thead>
            <tr>
              <th>Email</th>
              <th>Joined</th>
              <th>Workspace</th>
            </tr>
          </thead>
          <tbody>
            {data.users.map((u) => (
              <tr key={u.email}>
                <td>{u.email}</td>
                <td>{formatDate(u.joinedAt)}</td>
                <td>{u.hasWorkspace ? "Created" : "Not yet"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
