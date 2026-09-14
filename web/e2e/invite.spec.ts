import { test, expect, type BrowserContext, type Page } from "@playwright/test";

/**
 * N3b — the two-account invitation path, on deployed `dev` (task E14/F06/US01/T01,
 * wave w14 "workspace is real", us-01-final-integration; NW-58 acceptance N3b-1…N3b-8;
 * `.helix/reports/architecture/waves/w14.md` NW-58 row, client-architect's e2e paragraph;
 * ADR-025 Rules C9/C10/D.3/D.5, ADR-026 §D4–§D6, ADR-018 / ADR-020 w14 footers, screen 11).
 * The prose runbook for the same row, with the API and SQL checks a browser cannot make,
 * is `docs/waves/w14-acceptance.md` → N3b.
 *
 * The walk: an Admin invites an address → a **second browser context** (fresh storage, a
 * different Entra account) opens the accept link → signs in through the accept screen's own
 * popup CTA → **Join** → lands in *that* workspace, never on a create form → the Admin removes
 * them from `/workspace/members` → the removed account's next load loses access through the
 * ordinary sign-in revalidation (`GET /api/workspaces`, no new endpoint), and the link they
 * used now renders the "no longer valid" state, because removal revoked that email's live
 * invitations in the same transaction.
 *
 * ## Why it is skipped today, and with which words
 *
 * NW-67 has Raffa provision the invitee's second Entra account itself — a B2B guest created via
 * Microsoft Graph at invite time (ADR-025 §J.8b) — so a pre-existing second account is no longer
 * the operator prerequisite `reports/audit/w14-hitl.md` recorded for w14. What still cannot be
 * automated is reading the **one-time passcode** Entra emails the invitee to complete that first
 * sign-in: there is no mail-catcher this wave (a `dev`-only one is bounded future work for NW-50,
 * W18 — ADR-025 §J.8c) and this file has no way to script reading a human inbox. Until one
 * exists, this file `test.skip`s with that exact, named reason (the same callback form
 * `v2.spec.ts` uses, so an unconfigured run never attempts a sign-in and the report still shows
 * the gate exists) — never a silent gap.
 *
 * The second account does **not** have to share the Admin's email domain: after w14 the
 * cross-domain invite check is a non-blocking warning, not a block (ADR-001 w14 footer;
 * `memberViewModel.ts#inviteDomainWarning` — the server has no domain rule at all), so the
 * invite form accepts any well-formed address.
 *
 * ## Required environment
 *
 * | Variable | Meaning |
 * |---|---|
 * | `RAFFA_E2E_BASE_URL` | The deployed SPA origin (`dev`). |
 * | `RAFFA_E2E_ENTRA_EMAIL` / `RAFFA_E2E_ENTRA_PASSWORD` | The **Admin**: it must already hold an Admin membership in at least one workspace (N1/N2 precede this row in the runbook). |
 * | `RAFFA_E2E_SECOND_ENTRA_EMAIL` / `RAFFA_E2E_SECOND_ENTRA_PASSWORD` | The **invitee**: a second account on the same Entra tenant, excluded from interactive MFA like the first, holding **no** membership in the Admin's workspace when the run starts. |
 *
 * Selectors and copy are read from the committed source they drive and cited inline
 * (`routes/workspace/members/{index,InvitePane,MembersTable}.tsx`,
 * `routes/invite/accept/index.tsx`, `routes/signin/WorkspacePickerScreen.tsx`,
 * `components/shell/RailNav.tsx`). Parsed with `npx playwright test --list`; a real run needs
 * the deployed environment and the two accounts, which is an operator act (ADR-016).
 */

const BASE_URL = process.env.RAFFA_E2E_BASE_URL ?? "";
const ADMIN_EMAIL = process.env.RAFFA_E2E_ENTRA_EMAIL ?? "";
const ADMIN_PASSWORD = process.env.RAFFA_E2E_ENTRA_PASSWORD ?? "";
const INVITEE_EMAIL = process.env.RAFFA_E2E_SECOND_ENTRA_EMAIL ?? "";
const INVITEE_PASSWORD = process.env.RAFFA_E2E_SECOND_ENTRA_PASSWORD ?? "";

const SIGN_IN_READY = BASE_URL !== "" && ADMIN_EMAIL !== "" && ADMIN_PASSWORD !== "";
const SECOND_ACCOUNT_READY = INVITEE_EMAIL !== "" && INVITEE_PASSWORD !== "";

const MISSING_SIGN_IN_ENV =
  "RAFFA_E2E_BASE_URL / RAFFA_E2E_ENTRA_EMAIL / RAFFA_E2E_ENTRA_PASSWORD are not set — " +
  "see web/README.md 'End-to-end (workspace invitation, N3b)' for how to supply them against `dev`.";

const SECOND_ACCOUNT_REASON =
  "requires reading the invitee's one-time passcode, emailed by Entra to complete the sign-in — " +
  "NW-67 has Raffa provision the invitee's second Entra account itself (a B2B guest, created via " +
  "Microsoft Graph at invite time), so a pre-existing second account is no longer the blocker " +
  "(ADR-025 §J.8b). There is no mail-catcher this wave to read that passcode automatically (a " +
  "dev-only one is bounded future work for NW-50, W18 — ADR-025 §J.8c), so " +
  "RAFFA_E2E_SECOND_ENTRA_EMAIL / RAFFA_E2E_SECOND_ENTRA_PASSWORD cannot yet be supplied by a script.";

/**
 * Entra's own identifier → password → optional "Stay signed in?" pages — the same selectors
 * `v2.spec.ts#signInWithEntra` drives, factored out because this file needs them twice: once
 * on the Admin's page (redirect flow) and once inside the invitee's popup.
 */
async function driveEntraPages(page: Page, email: string, password: string): Promise<void> {
  await page.locator('input[name="loginfmt"]').waitFor({ state: "visible", timeout: 60_000 });
  await page.locator('input[name="loginfmt"]').fill(email);
  await page.locator("#idSIButton9").click();

  await page.locator('input[name="passwd"]').waitFor({ state: "visible", timeout: 30_000 });
  await page.locator('input[name="passwd"]').fill(password);
  await page.locator("#idSIButton9").click();

  // "Stay signed in?" is optional and tenant-configurable.
  try {
    await page.locator("#idSIButton9").waitFor({ state: "visible", timeout: 8_000 });
    await page.locator("#idSIButton9").click();
  } catch {
    // Not shown for this tenant/account — nothing to dismiss.
  }
}

/** The app-wide sign-in: `routes/signin/index.tsx` → `loginRedirect`, back to the SPA when done. */
async function signInWithEntra(page: Page, email: string, password: string): Promise<void> {
  await page.goto("/");
  await page.getByRole("button", { name: /continue with microsoft entra id/i }).click();
  await page.waitForURL(/login\.microsoftonline\.com/i, { timeout: 60_000 });
  await driveEntraPages(page, email, password);
  await page.waitForURL((url) => !/login\.microsoftonline\.com/i.test(url.href), { timeout: 60_000 });
}

/**
 * After the Admin's sign-in the gate resolves `GET /api/workspaces` (N2): one membership lands
 * straight in the shell, several show the picker (`WorkspacePickerScreen.tsx` → one
 * `button.workspace-row` per workspace), none shows the create form. The Admin account is
 * required to already own a workspace, so the create form is a precondition failure here —
 * never something this spec papers over by creating one of its own.
 */
async function enterAdminWorkspace(page: Page): Promise<string> {
  const railName = page.locator(".shell-rail-workspace-name");
  const picker = page.getByRole("heading", { name: "Choose a workspace" });
  const createForm = page.getByRole("heading", { name: "Create your workspace" });

  await expect(railName.or(picker).or(createForm).first()).toBeVisible({ timeout: 60_000 });
  if (await createForm.isVisible()) {
    throw new Error(
      "RAFFA_E2E_ENTRA_EMAIL holds no workspace membership on this environment — run N1 first; " +
        "N3b needs an Admin who already owns a workspace.",
    );
  }
  if (await picker.isVisible()) {
    await page.locator("button.workspace-row").first().click();
  }

  await expect(railName).toBeVisible({ timeout: 30_000 });
  const name = (await railName.innerText()).trim();
  expect(name, "the rail must name the workspace the Admin entered").not.toBe("");
  return name;
}

/** One roster row (`MembersTable.tsx` renders a real `<table>`, one `<tr>` per member). */
function rosterRow(page: Page, email: string) {
  return page.getByRole("row").filter({ hasText: email });
}

test.describe("N3b — invite → accept in a second browser → removal ends access", () => {
  test.describe.configure({ mode: "serial" });

  // Group-level conditional skips (callback form): with either in force Playwright never enters
  // `beforeAll`, so an unconfigured run never attempts a real Entra sign-in — it reports the gate
  // as skipped, with the reason.
  test.skip(() => !SIGN_IN_READY, MISSING_SIGN_IN_ENV);
  test.skip(() => !SECOND_ACCOUNT_READY, SECOND_ACCOUNT_REASON);

  let admin: BrowserContext;
  let adminPage: Page;
  let invitee: BrowserContext;
  let inviteePage: Page;
  let workspaceName = "";
  let acceptLink = "";

  test.beforeAll(async ({ browser }) => {
    admin = await browser.newContext();
    adminPage = await admin.newPage();
    // A genuinely separate browser: its own cookies, storage and MSAL cache, nothing shared.
    invitee = await browser.newContext();
    inviteePage = await invitee.newPage();

    await signInWithEntra(adminPage, ADMIN_EMAIL, ADMIN_PASSWORD);
    workspaceName = await enterAdminWorkspace(adminPage);
  });

  test.afterAll(async () => {
    await admin?.close();
    await invitee?.close();
  });

  test("N3b-1/2 — the Admin invites the second account and gets a copyable link, never a 'sent' claim", async () => {
    await adminPage.goto("/workspace/members");
    await expect(adminPage.getByRole("heading", { name: /workspace & members/i })).toBeVisible();

    // `InvitePane.tsx`: "Work email" field, Procurement is the default role.
    await adminPage.getByLabel("Email").fill(INVITEE_EMAIL);
    await adminPage.getByRole("button", { name: /send invitation/i }).click();

    // `mailDelivered` is false by construction in w14 (no transport, ADR-026 §D6): the pane must
    // render the server's fact — a link — and the word "sent" nowhere (NW-58 must #1).
    const outcome = adminPage.locator(".members-invite-link");
    await expect(outcome).toContainText(`Invitation ready for ${INVITEE_EMAIL}`);
    await expect(outcome).not.toContainText("Invitation sent");

    acceptLink = await adminPage.getByLabel("Invitation link").inputValue();
    expect(acceptLink, "the link is /invite/accept#<token> resolved against the SPA origin (Rule C9)").toContain(
      "/invite/accept#",
    );

    // The roster is re-read from the server after the invite: the row is Invited, not a local echo.
    await expect(rosterRow(adminPage, INVITEE_EMAIL)).toContainText("Invited");
  });

  test("N3b-3/4 — a second browser opens the link, signs in through the popup, joins, and lands in that workspace", async () => {
    await inviteePage.goto(acceptLink);

    // Screen 11 state 3: the workspace name and the offered role — never the invited address
    // (ADR-025: echoing it would turn a leaked link into an address-discovery tool).
    await expect(inviteePage.getByRole("heading", { name: `Join ${workspaceName}` })).toBeVisible({ timeout: 30_000 });
    expect(inviteePage.url(), "the token is read from the fragment on mount and the address bar is cleared (Rule C10)").not.toContain("#");
    await expect(inviteePage.locator("main")).not.toContainText(INVITEE_EMAIL);

    // Signed out, the CTA is the Entra sign-in — `loginPopup`, not the app-wide redirect (ADR-012
    // w14 footer clause 6), so this page is never unloaded and the in-memory token survives.
    const popupPromise = invitee.waitForEvent("page");
    await inviteePage.getByRole("button", { name: /continue with microsoft entra id/i }).click();
    const popup = await popupPromise;
    await driveEntraPages(popup, INVITEE_EMAIL, INVITEE_PASSWORD);
    if (!popup.isClosed()) {
      await popup.waitForEvent("close", { timeout: 60_000 });
    }

    // Same mount, same token: the CTA becomes "Join {workspace}" off `useMsal().accounts` alone.
    const join = inviteePage.getByRole("button", { name: `Join ${workspaceName}` });
    await expect(join).toBeVisible({ timeout: 30_000 });
    await join.click();

    // Membership is proven by the next GET /api/workspaces, not trusted from the accept body: the
    // invitee lands in *that* workspace — the rail names it — and never on a create form.
    await expect(inviteePage).not.toHaveURL(/\/invite\/accept/, { timeout: 60_000 });
    await expect(inviteePage.locator(".shell-rail-workspace-name")).toHaveText(workspaceName, { timeout: 60_000 });
    await expect(inviteePage.getByRole("heading", { name: "Create your workspace" })).toHaveCount(0);
  });

  test("N3b-5 — the Admin's roster shows the member Active; a sole Admin's own Remove is disabled, never a 409 click", async () => {
    await adminPage.reload();
    await expect(rosterRow(adminPage, INVITEE_EMAIL)).toContainText("Active");

    // `MembersTable.tsx`: the last Workspace Admin's Remove renders disabled with a hint (ADR-020 w14
    // footer). Only assertable when this Admin is the sole one — reported, not assumed, otherwise.
    const adminRows = adminPage.getByRole("row").filter({ hasText: /workspace admin/i });
    if ((await adminRows.count()) === 1) {
      await expect(rosterRow(adminPage, ADMIN_EMAIL).getByRole("button", { name: "Remove" })).toBeDisabled();
    } else {
      test.info().annotations.push({
        type: "note",
        description: "workspace has more than one Admin — the last-Admin disabled control is not exercised by this run",
      });
    }
  });

  test("N3b-6/7/8 — removal ends access on the next load, the used link is no longer valid, re-joining needs a new invitation", async () => {
    const row = rosterRow(adminPage, INVITEE_EMAIL);
    await row.getByRole("button", { name: "Remove" }).click();
    // Confirmed inline in the row, never a dialog (ADR-019 w14 footer).
    await row.getByRole("button", { name: "Yes, remove" }).click();
    await expect(row).toHaveCount(0);

    // The removed account's next load: the ordinary sign-in revalidation (`GET /api/workspaces`)
    // no longer lists this tenant — no new endpoint, nothing cached (ADR-025 Rule F.1d).
    await inviteePage.goto("/");
    const createForm = inviteePage.getByRole("heading", { name: "Create your workspace" });
    const picker = inviteePage.getByRole("heading", { name: "Choose a workspace" });
    const rail = inviteePage.locator(".shell-rail-workspace-name");
    await expect(createForm.or(picker).or(rail).first()).toBeVisible({ timeout: 60_000 });
    await expect(inviteePage.locator("button.workspace-row", { hasText: workspaceName })).toHaveCount(0);
    if ((await rail.count()) > 0) {
      await expect(rail).not.toHaveText(workspaceName);
    }

    // Removal revoked that email's live invitations in the same transaction (ADR-025 Rule D.5c):
    // the very link they joined with now renders the shared expired-or-revoked state — a 404 from
    // GET /api/invites, indistinguishable from "never existed" (screen 11 state 8).
    await inviteePage.goto(acceptLink);
    await expect(inviteePage.getByRole("heading", { name: "This invitation is no longer valid." })).toBeVisible({
      timeout: 30_000,
    });
  });
});
