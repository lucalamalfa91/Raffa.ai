import { useCallback, useEffect, useState, type FormEvent } from "react";
import type { ApiClient, CreateCustomerWorkspaceBody, WorkspaceSummaryBody } from "../../api/client";
import { selectCurrentWorkspace } from "../signin/workspaceStore";
import "./admin.css";

export interface AdminRouteProps {
  apiClient: ApiClient;
  /** The workspace the shell is showing now, so its row can say so instead of offering to open it. */
  currentWorkspaceId?: string;
  /** Switch into a workspace. Defaults to remembering it for this tab and reloading into the app. */
  openWorkspace?: (workspace: { id: string; name: string }) => void;
}

const COUNTRIES = [
  { code: "", label: "Not set" },
  { code: "CH", label: "Switzerland" },
  { code: "IT", label: "Italy" },
  { code: "DE", label: "Germany" },
  { code: "AT", label: "Austria" },
];

const DELIVERY_COPY: Record<string, string> = {
  sent: "An invitation email was sent.",
  mail_failed: "The email could not be sent. Share the link below with the customer.",
  no_transport: "This environment sends no email. Share the link below with the customer.",
};

function defaultOpenWorkspace(workspace: { id: string; name: string }): void {
  selectCurrentWorkspace(workspace);
  window.location.assign("/today");
}

/**
 * Portal admin (Raffa staff; route `/admin`). Onboards an external customer: creates their workspace,
 * invites their first Admin, and lets the staff member open the new, empty workspace to try it the way
 * the customer will -- upload contracts and build everything from zero. The staff member is an Admin of
 * every workspace created here (the server's normal creator rule), which is why the list below is just
 * their own workspaces. The server answers 404 to anyone who is not a platform admin.
 */
export default function AdminRoute({ apiClient, currentWorkspaceId, openWorkspace = defaultOpenWorkspace }: AdminRouteProps) {
  const [name, setName] = useState("");
  const [customerEmail, setCustomerEmail] = useState("");
  const [industry, setIndustry] = useState("");
  const [country, setCountry] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreateCustomerWorkspaceBody | null>(null);
  const [workspaces, setWorkspaces] = useState<readonly WorkspaceSummaryBody[] | null>(null);
  const [listError, setListError] = useState<string | null>(null);

  const loadWorkspaces = useCallback(async () => {
    const result = await apiClient.listWorkspaces();
    if (result.ok && result.workspaces) {
      setWorkspaces(result.workspaces);
      setListError(null);
    } else {
      setListError(result.error ?? "The workspace list is unavailable.");
    }
  }, [apiClient]);

  useEffect(() => {
    void loadWorkspaces();
  }, [loadWorkspaces]);

  const canSubmit = name.trim().length > 0 && customerEmail.trim().length > 0 && !submitting;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);
    setCreated(null);
    if (!apiClient.createCustomerWorkspace) {
      setSubmitting(false);
      setError("This build cannot create customer workspaces.");
      return;
    }
    const result = await apiClient.createCustomerWorkspace({
      name: name.trim(),
      customerEmail: customerEmail.trim(),
      ...(industry.trim() ? { industry: industry.trim() } : {}),
      ...(country ? { country } : {}),
    });
    setSubmitting(false);
    if (!result.ok || !result.result) {
      setError(result.statusCode === 404 ? "You are not a Raffa platform admin." : (result.error ?? "The workspace could not be created."));
      return;
    }
    setCreated(result.result);
    setName("");
    setCustomerEmail("");
    setIndustry("");
    setCountry("");
    void loadWorkspaces();
  };

  return (
    <div className="admin-screen">
      <header className="screen-header">
        <div className="screen-kicker">Raffa staff</div>
        <h1 className="screen-title">Admin portal</h1>
        <p className="admin-lede">
          Set up a customer: create their workspace, invite their first Admin, then open the workspace to try it.
          It starts empty, so the customer uploads their own contracts and builds everything from zero.
        </p>
      </header>

      <section className="admin-card" aria-labelledby="admin-onboard">
        <h2 id="admin-onboard">Onboard a customer</h2>
        <form onSubmit={(event) => void submit(event)} className="admin-form">
          <div className="field">
            <label htmlFor="admin-name">Workspace name</label>
            <input id="admin-name" className="input" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} />
          </div>
          <div className="field">
            <label htmlFor="admin-email">Customer admin email</label>
            <input
              id="admin-email"
              className="input"
              type="email"
              value={customerEmail}
              onChange={(e) => setCustomerEmail(e.target.value)}
              placeholder="name@customer.com"
            />
          </div>
          <div className="field">
            <label htmlFor="admin-industry">Industry (optional)</label>
            <input id="admin-industry" className="input" value={industry} onChange={(e) => setIndustry(e.target.value)} maxLength={120} />
          </div>
          <div className="field">
            <label htmlFor="admin-country">Country (optional)</label>
            <select id="admin-country" className="input" value={country} onChange={(e) => setCountry(e.target.value)}>
              {COUNTRIES.map((c) => (
                <option key={c.code} value={c.code}>
                  {c.label}
                </option>
              ))}
            </select>
          </div>
          <button type="submit" className="btn btn-primary" disabled={!canSubmit}>
            {submitting ? "Creating…" : "Create workspace and invite"}
          </button>
        </form>

        {error ? (
          <p role="alert" className="admin-error">
            {error}
          </p>
        ) : null}

        {created ? (
          <div role="status" className="admin-result">
            <p>
              <strong>{created.workspace.name}</strong> was created.
            </p>
            {created.invitation ? (
              <>
                <p>
                  {created.invitation.email} is invited as Admin. {DELIVERY_COPY[created.invitation.deliveryOutcome] ?? ""}
                </p>
                <label htmlFor="admin-link">Invitation link</label>
                <input id="admin-link" className="input" readOnly value={created.invitation.acceptUrl} onFocus={(e) => e.currentTarget.select()} />
              </>
            ) : (
              <p className="admin-error">
                The workspace exists but the invitation was not issued: {created.invitationError}
              </p>
            )}
            <button
              type="button"
              className="btn"
              onClick={() => openWorkspace({ id: created.workspace.id, name: created.workspace.name })}
            >
              Open this workspace
            </button>
          </div>
        ) : null}
      </section>

      <section className="admin-card" aria-labelledby="admin-workspaces">
        <h2 id="admin-workspaces">Your workspaces</h2>
        {listError ? (
          <p role="alert" className="admin-error">
            {listError}
          </p>
        ) : workspaces === null ? (
          <p className="hint">Loading…</p>
        ) : workspaces.length === 0 ? (
          <p className="hint">No workspaces yet.</p>
        ) : (
          <table className="table admin-table">
            <thead>
              <tr>
                <th>Workspace</th>
                <th>Role</th>
                <th>Contracts</th>
                <th>
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {workspaces.map((workspace) => (
                <tr key={workspace.id}>
                  <td>{workspace.name}</td>
                  <td>{workspace.role}</td>
                  <td>{workspace.contractCount}</td>
                  <td>
                    {workspace.id === currentWorkspaceId ? (
                      <span className="hint">Open now</span>
                    ) : (
                      <button type="button" className="btn" onClick={() => openWorkspace({ id: workspace.id, name: workspace.name })}>
                        Open
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}
