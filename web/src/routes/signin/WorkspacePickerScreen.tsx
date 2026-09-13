import { useState, type FormEvent } from "react";
import type { ApiClient, CreateWorkspaceRequest, WorkspaceSummaryBody } from "../../api/client";
import { workspaceRoleLabel } from "../../components/shell/workspaceRole";
import { SignInStatementPanel } from "./SignInScreen";
import { clearCurrentWorkspace, type CurrentWorkspace } from "./workspaceStore";

/**
 * Task E14/F03/US02/T01 (wave w14 "workspace is real"; ADR-012/ADR-018/ADR-020
 * w14 footers). Screen 1's pick/create surface, rebuilt on a real
 * `GET /api/workspaces` (ADR-026 §D1) instead of a per-browser cache -- see
 * `workspaceStore.ts`'s own header comment for what died with that cache.
 *
 * This screen no longer owns the fetch, and that is deliberate: `App.tsx`
 * resolves the caller's workspace list once, on mount and whenever the
 * account changes, *before* deciding whether this screen is reachable at
 * all -- "exactly one row -> enter it, no picker" (AC-2) can only hold if the
 * picker never mounts for that caller in the first place. This component
 * renders whatever `App.tsx`'s resolution settled on (`WorkspacePickerState`
 * below) and reports the two things a signed-in caller can still *do* here --
 * retry a failed read, or enter a workspace (by picking a row or creating
 * one) -- through `onRetry`/`onEnter`. `resolveWorkspaceSelection` is the
 * pure resolution-order rule itself, exported so it is unit-testable without
 * mounting anything.
 *
 * **Named divergence, resolved**: `WorkspacePickerScreen.tsx:100-137` used to
 * render a "You're in {name}" interstitial (Continue / Switch workspace /
 * Sign out) between every successful pick and the shell. Nothing resembling
 * it exists in the V2 export (`screens-v2.md` lists screen 1's states as
 * "idle · signing · create · pick"; `app.jsx`'s `enterWs` goes straight to
 * `go('ask')`) and the ADR-018/ADR-020 w14 footers hand the routing call to
 * this task ("it must not stand between 'one membership' and entering," for
 * the auto-entered case specifically). This build removes the interstitial
 * for every path -- auto-entered, hint-matched, manually picked, and
 * freshly created all land in the shell with no intermediate screen -- which
 * is also why the stale `:117-123` comment ("no router is mounted anywhere
 * above this component") and its hard `<a href="/">` are gone rather than
 * edited: `App.tsx` now hoists a real `BrowserRouter`, and there is no
 * interstitial control left for that comment to justify.
 */

export type WorkspaceResolution =
  | { kind: "empty" }
  | { kind: "enter"; workspace: WorkspaceSummaryBody }
  | { kind: "pick"; workspaces: readonly WorkspaceSummaryBody[] };

/**
 * The resolution order, AC-2/AC-3 verbatim: empty list -> create form;
 * exactly one row -> enter it; >=2 rows with the session hint present in the
 * list -> enter *that* row (the server's own data for it, never the bare
 * `{id, name}` hint); >=2 rows with the hint absent or not found -> hand back
 * to the picker. The caller (`App.tsx`) is responsible for discarding a hint
 * this function did not use to enter -- `hint ∉ list ⇒ discard the hint`
 * needs no endpoint, no polling and no cache invalidation, only this rule
 * plus a revalidating GET.
 */
export function resolveWorkspaceSelection(
  workspaces: readonly WorkspaceSummaryBody[],
  hint: CurrentWorkspace | null,
): WorkspaceResolution {
  if (workspaces.length === 0) {
    return { kind: "empty" };
  }
  if (workspaces.length === 1) {
    return { kind: "enter", workspace: workspaces[0] };
  }
  if (hint !== null) {
    const matched = workspaces.find((workspace) => workspace.id === hint.id);
    if (matched !== undefined) {
      return { kind: "enter", workspace: matched };
    }
  }
  return { kind: "pick", workspaces };
}

/** What `App.tsx`'s resolution settled on, for every outcome this screen can still be reached for
 * (the "enter" outcome above skips this screen entirely). `"resolving"` is reachable only via a
 * Retry from `"error"` -- the very first resolution after sign-in is gated above this component so
 * it never mounts mid-flight (AC-8: "must not flash the sign-in screen"). */
export type WorkspacePickerState =
  | { phase: "resolving" }
  | { phase: "error"; message: string }
  | { phase: "empty" }
  | { phase: "pick"; workspaces: readonly WorkspaceSummaryBody[] };

export interface WorkspacePickerScreenProps {
  apiClient: ApiClient;
  /** MSAL `AccountInfo.username`, shown so the screen confirms which identity is signed in. */
  accountLabel: string;
  onSignOut: () => void;
  state: WorkspacePickerState;
  /** Re-runs the same resolution `App.tsx` ran on mount (the `"error"` state's Retry button). */
  onRetry: () => void;
  /** A definite workspace was chosen -- by row click or by a successful create. `App.tsx` persists
   * the session hint and swaps to the shell; this screen does not navigate itself. */
  onEnter: (workspace: WorkspaceSummaryBody) => void;
}

/** `markup.html:55`, verbatim, `Other` last -- the "not specified" case, not a separate blank
 * sentinel option (a task adding one would contradict this same anchor). */
const INDUSTRY_OPTIONS = [
  "Food & beverage",
  "Manufacturing",
  "Financial services",
  "Software",
  "Other",
] as const;

/** `markup.html:56`, verbatim and in the export's own order. The **label** is the country's name;
 * the **value** the form submits is the ISO 3166-1 alpha-2 code the API validates and stores
 * (`WorkspaceProvisioningService.CurrencyByCountry`: exactly `AT`/`CH`/`DE`/`IT`, ADR-003 w14
 * footer clause 2). Submitting the label instead answered 400 "'Switzerland' is not a supported
 * workspace country" on the first `dev` walk of W14-A2 (2026-09-13) -- the create form and the
 * API had each been proven against their own reading of the contract. Exactly these four: the
 * currency and name derivations below are total over this list by construction (`Record`s keyed
 * on the same literal union), so there is no "unknown country" branch to write and none to test. */
const COUNTRY_OPTIONS = [
  { code: "CH", name: "Switzerland" },
  { code: "IT", name: "Italy" },
  { code: "DE", name: "Germany" },
  { code: "AT", name: "Austria" },
] as const;
type CountryCode = (typeof COUNTRY_OPTIONS)[number]["code"];

/** The display echo of the derivation the server performs when it stores a workspace's currency
 * (ADR-003 w14 footer; `WorkspaceProvisioningService.CurrencyByCountry`, mirrored key for key).
 * Never sent on the wire -- the request carries `country` only; this map exists so the form can
 * show "Amounts are shown in {currency}." *before* the workspace exists. */
const COUNTRY_CURRENCY: Record<CountryCode, string> = {
  CH: "CHF",
  IT: "EUR",
  DE: "EUR",
  AT: "EUR",
};

/** Code -> the same display name the select shows, for the pick row's third segment. */
const COUNTRY_NAME: Record<CountryCode, string> = {
  CH: "Switzerland",
  IT: "Italy",
  DE: "Germany",
  AT: "Austria",
};

/**
 * The business country **name** for a stored code (the server stores and returns `CH`, never
 * "Switzerland"). A code outside the four -- nothing writes one today, but the column is a free
 * `varchar(2)` -- renders as the server holds it rather than being dropped or invented.
 */
export function formatWorkspaceCountry(country: string): string {
  const code = country.trim().toUpperCase();
  return Object.hasOwn(COUNTRY_NAME, code) ? COUNTRY_NAME[code as CountryCode] : country;
}

function formatValidatedContractsSegment(count: number): string {
  if (count === 0) return "No validated contracts yet";
  if (count === 1) return "1 validated contract";
  return `${count} validated contracts`;
}

function isPresentSegment(value: string | null | undefined): value is string {
  return value !== null && value !== undefined && value.trim() !== "";
}

/**
 * The pick-row meta line: up to three segments joined by " · ", a missing one dropped rather than
 * rendered as a gap, a dash or a placeholder (AC-5). Segment 1 (the validated-contract count) is
 * never null; segment 2 (currency) comes straight off the server row (`WorkspaceSummaryBody.currency`,
 * already the ISO 4217 display string); segment 3 is the business country **name** looked up from
 * the ISO 3166-1 alpha-2 code the server stores and returns in `.country` (`CH`, never
 * "Switzerland" -- `formatWorkspaceCountry`). Deliberately the country's own name, never the V2
 * export's `eu-west` cloud-region slug -- printing a region slug under "Contracts uploaded here
 * never leave it." would read as a data-residency promise this product has not made.
 */
export function buildWorkspaceRowMeta(workspace: WorkspaceSummaryBody): string {
  return [
    formatValidatedContractsSegment(workspace.contractCount),
    isPresentSegment(workspace.currency) ? workspace.currency : null,
    isPresentSegment(workspace.country) ? formatWorkspaceCountry(workspace.country) : null,
  ]
    .filter(isPresentSegment)
    .join(" · ");
}

interface CreateWorkspaceFormProps {
  apiClient: ApiClient;
  onCreated: (workspace: WorkspaceSummaryBody) => void;
  /** `null` on screen 1's empty state (there is nothing to cancel back to); a real callback when
   * this form is the picker's own "+ Create a new workspace" toggle. */
  onCancel: (() => void) | null;
}

/**
 * `markup.html:52-57`, adopted whole and in order: heading, the tenancy sentence, Company ·
 * Industry · Country, the submit button. The label is "Company"; the request/state field is
 * `name` (ADR-001/ADR-003 w14 footers) -- the two are deliberately not the same word.
 */
function CreateWorkspaceForm({ apiClient, onCreated, onCancel }: CreateWorkspaceFormProps) {
  const [name, setName] = useState("");
  const [industry, setIndustry] = useState<string>(INDUSTRY_OPTIONS[0]);
  const [country, setCountry] = useState<CountryCode>(COUNTRY_OPTIONS[0].code);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const currency = COUNTRY_CURRENCY[country];

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmedName = name.trim();
    if (!trimmedName) {
      setError("A company name is required.");
      return;
    }

    setCreating(true);
    setError(null);
    const request: CreateWorkspaceRequest = { name: trimmedName, industry, country };
    void apiClient.createWorkspace(request).then((result) => {
      setCreating(false);
      if (!result.ok || !result.workspace) {
        setError(result.error ?? "Could not create the workspace. Try again.");
        return;
      }

      // A workspace this form just created has ingested nothing yet -- zero is the true count for
      // it, not a placeholder (contrast the deleted per-browser cache's own hard-coded zero, which
      // was "true" only by coincidence and stayed wrong forever after the first upload).
      const noContractsYet = 0;
      onCreated({
        id: result.workspace.id,
        name: result.workspace.name,
        createdAt: result.workspace.createdAt,
        role: result.workspace.role,
        contractCount: noContractsYet,
        country,
        currency,
      });
    });
  };

  return (
    <form onSubmit={handleSubmit}>
      <h2 className="screen-title">Create your workspace</h2>
      <p className="micro-meta">A workspace is your tenant. Contracts uploaded here never leave it.</p>
      <div className="field">
        <label htmlFor="signin-workspace-company">Company</label>
        <input
          id="signin-workspace-company"
          className="input"
          value={name}
          onChange={(event) => setName(event.target.value)}
          disabled={creating}
          autoFocus
        />
      </div>
      <div className="field">
        <label htmlFor="signin-workspace-industry">Industry</label>
        <select
          id="signin-workspace-industry"
          className="input"
          value={industry}
          onChange={(event) => setIndustry(event.target.value)}
          disabled={creating}
        >
          {INDUSTRY_OPTIONS.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor="signin-workspace-country">Country</label>
        <select
          id="signin-workspace-country"
          className="input"
          value={country}
          onChange={(event) => setCountry(event.target.value as CountryCode)}
          disabled={creating}
        >
          {COUNTRY_OPTIONS.map((option) => (
            <option key={option.code} value={option.code}>
              {option.name}
            </option>
          ))}
        </select>
        {/* A wrong guess is catchable before the workspace exists -- the *stored* value is always
            the server's own derivation; this is a display echo of the same total, four-entry map. */}
        <p className="micro-meta">Amounts are shown in {currency}.</p>
      </div>
      {error && <p className="signin-form-error">{error}</p>}
      <div className="workspace-form-actions">
        <button type="submit" className="btn btn-primary" disabled={creating}>
          {creating ? "Creating…" : "Create workspace"}
        </button>
        {onCancel && (
          <button type="button" className="btn btn-ghost" disabled={creating} onClick={onCancel}>
            Cancel
          </button>
        )}
      </div>
    </form>
  );
}

export default function WorkspacePickerScreen({
  apiClient,
  accountLabel,
  onSignOut,
  state,
  onRetry,
  onEnter,
}: WorkspacePickerScreenProps) {
  const [showCreateForm, setShowCreateForm] = useState(false);

  const handleSignOut = () => {
    // A new sign-in should re-pick a workspace explicitly, not silently resume whichever tenant
    // this session left selected.
    clearCurrentWorkspace();
    onSignOut();
  };

  return (
    // Task E06/F06/US01/T01 (full-bleed-layout): shares SignInScreen's canvas
    // (SignInStatementPanel + .signin-action), the same two-column page every state of screen 1
    // renders on -- see signin.css's header comment.
    <main className="signin-screen">
      <SignInStatementPanel />
      <section className="signin-action">
        {state.phase === "empty" ? (
          <>
            {/* Task E11/F02/US01/T01 (signin-1to1): the accent h6 kicker sits above the heading in
                every one of this screen's states, `markup.html:51,60`'s own idiom. */}
            <h6 className="signin-account-kicker">Signed in as {accountLabel}</h6>
            <CreateWorkspaceForm apiClient={apiClient} onCreated={onEnter} onCancel={null} />
            {/* ADR-012 w14 footer clause 3 (copy corrected by ADR-020's own second w14 footer):
                unconditional -- this screen must not know whether an invitation exists, so it
                cannot say "again" (that presumes a history only the accept screen has earned) and
                names the channel honestly ("shared with you", not "sent") because w14 ships no mail
                transport. */}
            <p className="micro-meta">
              Invited to a workspace? Open the invitation link your workspace admin shared with you.
            </p>
          </>
        ) : (
          <>
            <h6 className="signin-account-kicker">Signed in as {accountLabel}</h6>
            <h2 className="screen-title">Choose a workspace</h2>

            {state.phase === "resolving" && (
              <div className="workspace-list-skeleton" role="status" aria-live="polite">
                <p className="micro-meta">Loading your workspaces…</p>
                {Array.from({ length: 3 }, (_, index) => (
                  <div key={index} className="skeleton workspace-list-skeleton-row" />
                ))}
              </div>
            )}

            {state.phase === "error" && (
              // ADR-018 `:112-119`: 2px accent left rule + h4 + the plain endpoint name + secondary
              // Retry -- never a cached list, which is exactly the defect this screen used to have.
              <div className="error-state" role="alert">
                <h4>Workspaces unavailable</h4>
                <p className="micro-meta">{state.message}</p>
                <button type="button" className="btn btn-secondary" onClick={onRetry}>
                  Retry
                </button>
              </div>
            )}

            {state.phase === "pick" && (
              <>
                <div className="workspace-list">
                  {state.workspaces.map((workspace) => (
                    <button
                      key={workspace.id}
                      type="button"
                      className="workspace-row"
                      onClick={() => onEnter(workspace)}
                    >
                      <div>
                        <div className="workspace-row-name">{workspace.name}</div>
                        <div className="workspace-row-meta">{buildWorkspaceRowMeta(workspace)}</div>
                      </div>
                      {/* The server's own role, pass-through for a role this app does not model
                          (ADR-019 w14 footer clause 5) -- never the frozen "Workspace Admin" every
                          row used to hard-code regardless of who could actually see it. */}
                      <span className="tag tag-accent">{workspaceRoleLabel(workspace.role)}</span>
                    </button>
                  ))}
                </div>

                {!showCreateForm && (
                  <button
                    type="button"
                    className="btn btn-ghost workspace-create-cta"
                    onClick={() => setShowCreateForm(true)}
                  >
                    + Create a new workspace
                  </button>
                )}

                {showCreateForm && (
                  <CreateWorkspaceForm
                    apiClient={apiClient}
                    onCreated={onEnter}
                    onCancel={() => setShowCreateForm(false)}
                  />
                )}
              </>
            )}
          </>
        )}

        <button type="button" className="btn btn-ghost workspace-signout" onClick={handleSignOut}>
          Sign out
        </button>
      </section>
    </main>
  );
}
