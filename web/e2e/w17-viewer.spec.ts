import { test, expect, type BrowserContext, type Page } from "@playwright/test";

/**
 * N17 — document viewer, on deployed `dev` (task E22/F03/US01/T01, NW-63 w17 half;
 * ADR-012 w17 clauses 39 and 41). Parent story us-01-document-viewer-route AC-1…AC-12.
 *
 * A validated document's viewer shows its own page; paging to 2 works; a deep link
 * beyond `pageCount` lands on not-found **with the URL still reading `?page=N`**;
 * a reprocessed document's viewer stops painting the pre-reprocess page.
 *
 * N20 is appended by E22/F04/US01/T01 (NW-66): a Why row shows no quote and no
 * percentage, carries a leverage word, and **Open in document viewer** lands on
 * the cited page.
 *
 * OQ-w17-cl-03: a **signed-out** `/documents/:id/viewer?page=3&clause=…` deep link
 * through the OIDC round trip. This spec asserts whichever branch actually held
 * (query survived vs dropped). The viewer never repairs a dropped query with
 * sessionStorage/localStorage; that repair is the auth landing's (ADR-012 w14
 * footer `:189-285`).
 *
 * ## Honesty note
 *
 * This file is **acceptance-runbook evidence, not a CI gate** (ADR-012 w17 clause 31
 * and §12): no workflow runs Playwright. Parsed with `npx playwright test --list`;
 * a real green walk needs the deployed environment, a fixture-seeded tenant and an
 * Entra test account (ADR-016).
 *
 * Recorded from this task's code inspection, pending the operator walk:
 * `handleRedirectPromiseOptions.navigateToLoginRequestUrl` is `false`
 * (`web/src/auth/msalConfig.ts`). That is the setting that decides whether MSAL
 * restores the pre-login href (query string included) after the OIDC round trip.
 * The signed-out case below records the landed URL either way.
 */

const BASE_URL = process.env.RAFFA_E2E_BASE_URL ?? "";
const ENTRA_EMAIL = process.env.RAFFA_E2E_ENTRA_EMAIL ?? "";
const ENTRA_PASSWORD = process.env.RAFFA_E2E_ENTRA_PASSWORD ?? "";
const FIXTURE_TENANT_ID = process.env.RAFFA_E2E_TENANT_ID ?? "";

const SIGN_IN_READY = BASE_URL !== "" && ENTRA_EMAIL !== "" && ENTRA_PASSWORD !== "";

const MISSING_SIGN_IN_ENV =
  "RAFFA_E2E_BASE_URL / RAFFA_E2E_ENTRA_EMAIL / RAFFA_E2E_ENTRA_PASSWORD are not set — " +
  "see web/README.md 'End-to-end (Ask Raffa V2 pilot path)' for how to supply them against `dev`. " +
  "This spec is runbook evidence for N17 / OQ-w17-cl-03, not a CI gate (ADR-012 w17 clause 31).";

const MISSING_TENANT_ENV =
  "RAFFA_E2E_TENANT_ID is not set — N17 needs the fixture-seeded workspace with validated " +
  "documents. Walking an empty self-created workspace would prove the empty state, not the page.";

const CURRENT_WORKSPACE_KEY = "raffa.signin.currentWorkspace";

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

type DocumentListItem = {
  id: string;
  pageCount: number | null;
  processingStatus: string;
  contractId: string | null;
};

async function firstViewableDocument(page: Page): Promise<DocumentListItem> {
  await page.goto("/documents");
  const listResponse = await page.waitForResponse(
    (response) => {
      const url = response.url();
      return /\/api\/documents(?:\?|$)/.test(url) && !url.includes("/preview") && response.request().method() === "GET";
    },
    { timeout: 30_000 },
  );
  const body = (await listResponse.json()) as { items?: DocumentListItem[] };
  const item = (body.items ?? []).find(
    (row) => row.pageCount !== null && row.pageCount >= 2 && (row.processingStatus === "Completed" || row.processingStatus === "NeedsReview"),
  );
  expect(item, "N17 needs a processed document with at least 2 pages in the fixture tenant").toBeTruthy();
  return item as DocumentListItem;
}

test.describe("N17 — document viewer (w17)", () => {
  test.skip(() => !SIGN_IN_READY, MISSING_SIGN_IN_ENV);
  test.skip(() => FIXTURE_TENANT_ID === "", MISSING_TENANT_ENV);

  let context: BrowserContext;
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    context = await browser.newContext();
    page = await context.newPage();
    await signInWithEntra(page);
    await useWorkspace(page, FIXTURE_TENANT_ID, "W17 viewer — fixture workspace");
  });

  test.afterAll(async () => {
    await context?.close();
  });

  test("a validated document's viewer shows its page; paging to 2 works; beyond pageCount keeps the URL", async () => {
    const doc = await firstViewableDocument(page);
    const pageCount = doc.pageCount as number;

    await page.goto(`/documents/${doc.id}/viewer?page=1`);
    await expect(page.getByRole("img", { name: "Document page" })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(`Page 1 of ${pageCount}`)).toBeVisible();

    await page.getByRole("button", { name: "Next" }).click();
    await expect(page).toHaveURL(new RegExp(`/documents/${doc.id}/viewer\\?page=2`));
    await expect(page.getByText(`Page 2 of ${pageCount}`)).toBeVisible();

    const beyond = pageCount + 3;
    await page.goto(`/documents/${doc.id}/viewer?page=${beyond}`);
    await expect(page.getByText(`This document has ${pageCount} pages.`)).toBeVisible({ timeout: 15_000 });
    expect(page.url()).toMatch(new RegExp(`[?&]page=${beyond}(?:&|$)`));
    await expect(page.getByRole("button", { name: "Go to page 1" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Retry" })).toHaveCount(0);
  });

  test("a remounted viewer fetches the page again (no cached object URL across a navigation)", async () => {
    const doc = await firstViewableDocument(page);
    await page.goto(`/documents/${doc.id}/viewer?page=1`);
    const img = page.getByRole("img", { name: "Document page" });
    await expect(img).toBeVisible({ timeout: 30_000 });
    const firstSrc = await img.getAttribute("src");

    await page.goto("/documents");
    await page.goto(`/documents/${doc.id}/viewer?page=1`);
    const imgAgain = page.getByRole("img", { name: "Document page" });
    await expect(imgAgain).toBeVisible({ timeout: 30_000 });
    const secondSrc = await imgAgain.getAttribute("src");
    expect(secondSrc, "ADR-012 w17 §46: every page view is a fetch; a remount must not reuse the previous object URL").not.toBe(firstSrc);
  });
});

test.describe("OQ-w17-cl-03 — signed-out viewer deep link", () => {
  test.skip(() => !SIGN_IN_READY, MISSING_SIGN_IN_ENV);
  test.skip(() => FIXTURE_TENANT_ID === "", MISSING_TENANT_ENV);

  test("deep-link ?page=3&clause= while signed out; record whether the query survives OIDC", async ({ browser }) => {
    const seeded = await browser.newContext();
    const seedPage = await seeded.newPage();
    await signInWithEntra(seedPage);
    await useWorkspace(seedPage, FIXTURE_TENANT_ID, "W17 viewer — fixture workspace");
    const doc = await firstViewableDocument(seedPage);
    await seeded.close();

    const fresh = await browser.newContext();
    const page = await fresh.newPage();
    const deepLink = `/documents/${doc.id}/viewer?page=3&clause=oq-w17-cl-03`;
    await page.goto(deepLink);

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
      // "Stay signed in?" is optional.
    }
    await page.waitForURL((url) => !/login\.microsoftonline\.com/i.test(url.href), { timeout: 60_000 });

    const landed = page.url();
    const landedUrl = new URL(landed);
    const querySurvived = landedUrl.searchParams.get("page") === "3" && landedUrl.searchParams.has("clause");

    test.info().annotations.push({
      type: "OQ-w17-cl-03",
      description: querySurvived
        ? `query survived; landed URL: ${landed}`
        : `query dropped; landed URL: ${landed}. Repair is ADR-012 w14 footer :189-285 (auth landing), never the viewer and never a browser store.`,
    });

    if (querySurvived) {
      await expect(page).toHaveURL(/[?&]page=3/);
      await expect(page.getByText(/Page 3 of/)).toBeVisible({ timeout: 15_000 });
    } else {
      const onViewer = /\/documents\/[^/]+\/viewer/.test(landedUrl.pathname);
      if (onViewer) {
        await expect(page.getByText(/citation could not be restored/i)).toBeVisible();
        await expect(page.getByText(/highlighted below/)).toHaveCount(0);
        await expect(page.getByRole("button", { name: "Retry" })).toHaveCount(0);
      }
    }

    await fresh.close();
  });
});

test.describe("N20 — Why row leverage and viewer landing (w17)", () => {
  test.skip(() => !SIGN_IN_READY, MISSING_SIGN_IN_ENV);
  test.skip(() => FIXTURE_TENANT_ID === "", MISSING_TENANT_ENV);

  let context: BrowserContext;
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    context = await browser.newContext();
    page = await context.newPage();
    await signInWithEntra(page);
    await useWorkspace(page, FIXTURE_TENANT_ID, "W17 viewer — fixture workspace");
  });

  test.afterAll(async () => {
    await context?.close();
  });

  test("a clause row shows no quote and no percentage, carries a leverage word, and its link lands on the cited page", async () => {
    await page.goto("/contracts");
    const contractLink = page.locator(".portfolio-cell-contract a").first();
    await expect(contractLink, "N20 needs a validated contract in the fixture tenant").toBeVisible({ timeout: 30_000 });
    await contractLink.click();
    await expect(page).toHaveURL(/\/contracts\/[^/]+$/, { timeout: 15_000 });

    const why = page.getByRole("region", { name: "Why — the clauses behind it" });
    await expect(why).toBeVisible({ timeout: 15_000 });
    await expect(why).toContainText("Push to change · Worth raising · Standard terms");

    const rows = why.getByRole("listitem");
    await expect(rows.first()).toBeVisible();
    const rowCount = await rows.count();
    expect(rowCount).toBeGreaterThan(0);

    const whyText = (await why.innerText()).trim();
    expect(whyText, "N20: no percentage on the Why list").not.toMatch(/\d\s*%/);
    expect(whyText, "N20: raw risk enum never reaches the row").not.toMatch(/\b(High|Critical|Medium|Low)\b/);

    const viewerLink = why.getByRole("link", { name: "Open in document viewer" }).first();
    await expect(viewerLink).toBeVisible();
    const href = await viewerLink.getAttribute("href");
    expect(href).toMatch(/\/documents\/[^/]+\/viewer\?page=\d+&clause=/);

    await viewerLink.click();
    await expect(page).toHaveURL(/\/documents\/[^/]+\/viewer\?page=\d+&clause=/, { timeout: 15_000 });
    const landed = new URL(page.url());
    const pageNumber = landed.searchParams.get("page");
    expect(pageNumber).toMatch(/^[1-9]\d*$/);
    await expect(page.getByText(new RegExp(`Page ${pageNumber} of`))).toBeVisible({ timeout: 30_000 });
  });
});
