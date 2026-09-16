import { test, expect, type BrowserContext, type Page } from "@playwright/test";

/**
 * N18 — unrecovered Review fields, on deployed `dev` (task E22/F05/US01/T01, NW-64;
 * ADR-012 w17 clauses 38, 39 and 41). Parent story us-01-not-found-in-the-document AC-1…AC-3.
 *
 * A document whose OCR missed **end date** and **cancellation deadline** shows both as
 * empty fillable rows under **Not found in the document**. Filling one persists across
 * a reload through the existing `PATCH /api/contracts/{id}` — the server is the record,
 * not the tab.
 *
 * ## Honesty note
 *
 * This file is **acceptance-runbook evidence, not a CI gate** (ADR-012 w17 clause 31
 * and §12): no workflow runs Playwright. Parsed with `npx playwright test --list`;
 * a real green walk needs the deployed environment, a fixture-seeded tenant and an
 * Entra test account (ADR-016).
 *
 * Selectors are read from the committed source they drive
 * (`routes/contracts/review/{index,ReviewFieldList}.tsx`).
 */

const BASE_URL = process.env.RAFFA_E2E_BASE_URL ?? "";
const ENTRA_EMAIL = process.env.RAFFA_E2E_ENTRA_EMAIL ?? "";
const ENTRA_PASSWORD = process.env.RAFFA_E2E_ENTRA_PASSWORD ?? "";
const FIXTURE_TENANT_ID = process.env.RAFFA_E2E_TENANT_ID ?? "";

const SIGN_IN_READY = BASE_URL !== "" && ENTRA_EMAIL !== "" && ENTRA_PASSWORD !== "";

const MISSING_SIGN_IN_ENV =
  "RAFFA_E2E_BASE_URL / RAFFA_E2E_ENTRA_EMAIL / RAFFA_E2E_ENTRA_PASSWORD are not set — " +
  "see web/README.md 'End-to-end (Ask Raffa V2 pilot path)' for how to supply them against `dev`. " +
  "This spec is runbook evidence for N18, not a CI gate (ADR-012 w17 clause 31).";

const MISSING_TENANT_ENV =
  "RAFFA_E2E_TENANT_ID is not set — N18 needs the fixture-seeded workspace. " +
  "Walking an empty self-created workspace would prove the empty state, not an OCR miss.";

const CURRENT_WORKSPACE_KEY = "raffa.signin.currentWorkspace";

type PortfolioBody = {
  items: { contractId: string; endDate: string | null; cancellationDeadline: string | null }[];
};

async function signInWithEntra(page: Page): Promise<void> {
  await page.goto("/");
  await page.getByRole("button", { name: /continue with microsoft entra id/i }).click();
  await page.waitForURL(/login\.microsoftonline\.com/i, { timeout: 60_000 });

  await page.locator('input[name="loginfmt"]').fill(ENTRA_EMAIL);
  await page.locator("#idSIButton9").click();

  await page.locator('input[name="passwd"]').waitFor({ state: "visible", timeout: 30_000 });
  await page.locator('input[name="passwd"]').fill(ENTRA_PASSWORD);
  await page.locator("#idSIButton9").click();

  try {
    await page.locator("#idSIButton9").waitFor({ state: "visible", timeout: 8_000 });
    await page.locator("#idSIButton9").click();
  } catch {
    // "Stay signed in?" is optional for this tenant/account.
  }

  await page.waitForURL((url) => !/login\.microsoftonline\.com/i.test(url.href), { timeout: 60_000 });
}

async function useWorkspace(page: Page, tenantId: string, name: string): Promise<void> {
  await page.evaluate(
    ([key, id, workspaceName]) => {
      window.sessionStorage.setItem(key, JSON.stringify({ id, name: workspaceName }));
    },
    [CURRENT_WORKSPACE_KEY, tenantId, name] as const,
  );
}

test.describe("N18 — unrecovered Review fields (w17)", () => {
  test.skip(() => !SIGN_IN_READY, MISSING_SIGN_IN_ENV);
  test.skip(() => FIXTURE_TENANT_ID === "", MISSING_TENANT_ENV);

  let context: BrowserContext;
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    context = await browser.newContext();
    page = await context.newPage();
    await signInWithEntra(page);
    await useWorkspace(page, FIXTURE_TENANT_ID, "W17 review — fixture workspace");
  });

  test.afterAll(async () => {
    await context?.close();
  });

  test("missed end date and cancellation deadline render fillable and the fill survives reload", async () => {
    const portfolioPromise = page.waitForResponse(
      (response) => /\/api\/contracts(?:\?|$)/.test(response.url()) && response.request().method() === "GET" && !/\/api\/contracts\/[^/?]+/.test(new URL(response.url()).pathname),
      { timeout: 30_000 },
    );
    await page.goto("/contracts");
    const portfolioResponse = await portfolioPromise;
    expect(portfolioResponse.ok(), "GET /api/contracts must succeed for N18").toBeTruthy();
    const portfolio = (await portfolioResponse.json()) as PortfolioBody;
    const missed = portfolio.items.find((item) => item.endDate === null && item.cancellationDeadline === null);

    test.skip(
      missed === undefined,
      "N18 needs a fixture contract whose OCR missed both end date and cancellation deadline. None in this tenant's portfolio.",
    );
    if (missed === undefined) return;

    await page.goto(`/contracts/${missed.contractId}/review`);
    await expect(page.getByRole("heading", { name: "Not found in the document" })).toBeVisible({ timeout: 15_000 });
    await expect(
      page.getByText("Raffa could not find these in the file. Type the value if you have it — it is saved as your correction."),
    ).toBeVisible();

    const endDate = page.getByLabel("End date");
    const cancellation = page.getByLabel("Cancellation deadline");
    await expect(endDate).toHaveValue("");
    await expect(cancellation).toHaveValue("");
    await expect(page.locator(".review-missing-fields .tag")).toHaveCount(0);

    const filled = "2027-03-31";
    const patchPromise = page.waitForResponse(
      (response) =>
        response.request().method() === "PATCH" &&
        new URL(response.url()).pathname === `/api/contracts/${missed.contractId}`,
      { timeout: 30_000 },
    );
    await endDate.fill(filled);
    await endDate.blur();
    const patch = await patchPromise;
    expect(patch.ok(), "PATCH /api/contracts/{id} must persist the fill").toBeTruthy();

    await page.reload();
    await expect(page.getByRole("button", { name: "End date" })).toBeVisible({ timeout: 15_000 });
    await expect(page.getByLabel("Cancellation deadline")).toHaveValue("");
    await expect(page.getByRole("heading", { name: "Not found in the document" })).toBeVisible();
  });
});
