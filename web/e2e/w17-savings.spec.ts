import { test, expect, type BrowserContext, type Page } from "@playwright/test";

/**
 * A17-S1 — Savings verified KPI, on deployed `dev` (task E20/F01/US01/T01, NW-72;
 * ADR-012 w17 clauses 39 and 41). Parent story us-01-savings-verified-kpi AC-1…AC-7.
 *
 * Record an outcome that realizes an opportunity, then the band shows **realized
 * money grouped by currency** — one formatted line per currency — under the label
 * **"Savings verified"**, and that figure **agrees after a reload and in a second
 * browser context** (AC-3: the server is the record, not the tab). An outcome whose
 * `savingsPropagated` is `null` moves **no** figure (AC-5). The pre-negotiation
 * estimate never appears in that cell (AC-2).
 *
 * ## Honesty note
 *
 * This file is **acceptance-runbook evidence, not a CI gate** (ADR-012 w17 clause 31
 * and §12): no workflow runs Playwright. Parsed with `npx playwright test --list`;
 * a real green walk needs the deployed environment, a fixture-seeded tenant and an
 * Entra test account (ADR-016).
 *
 * Selectors are read from the committed source they drive
 * (`routes/savings/{index,KpiRow,savingsViewModel}.ts(x)`).
 */

const BASE_URL = process.env.RAFFA_E2E_BASE_URL ?? "";
const ENTRA_EMAIL = process.env.RAFFA_E2E_ENTRA_EMAIL ?? "";
const ENTRA_PASSWORD = process.env.RAFFA_E2E_ENTRA_PASSWORD ?? "";
const FIXTURE_TENANT_ID = process.env.RAFFA_E2E_TENANT_ID ?? "";

const SIGN_IN_READY = BASE_URL !== "" && ENTRA_EMAIL !== "" && ENTRA_PASSWORD !== "";

const MISSING_SIGN_IN_ENV =
  "RAFFA_E2E_BASE_URL / RAFFA_E2E_ENTRA_EMAIL / RAFFA_E2E_ENTRA_PASSWORD are not set — " +
  "see web/README.md 'End-to-end (Ask Raffa V2 pilot path)' for how to supply them against `dev`. " +
  "This spec is runbook evidence for A17-S1, not a CI gate (ADR-012 w17 clause 31).";

const MISSING_TENANT_ENV =
  "RAFFA_E2E_TENANT_ID is not set — A17-S1 needs the fixture-seeded workspace. " +
  "Walking an empty self-created workspace would prove the empty state, not verified money.";

const CURRENT_WORKSPACE_KEY = "raffa.signin.currentWorkspace";

type RealizedBucket = { currency: string; amount: number; count: number };
type IdentifiedBucket = { currency: string; low: number; high: number; count: number };
type KpiBody = {
  savingsIdentified: IdentifiedBucket[];
  savingsRealized: RealizedBucket[];
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

function formatAmount(currency: string, amount: number): string {
  return `${currency} ${new Intl.NumberFormat("en-GB").format(Math.round(amount))}`;
}

function verifiedCell(page: Page) {
  return page.getByRole("group", { name: "Savings KPIs" }).locator(".savings-kpi-cell").filter({ hasText: "Savings verified" });
}

async function openSavingsAndReadKpis(page: Page): Promise<{ body: KpiBody; response: Awaited<ReturnType<Page["waitForResponse"]>> }> {
  const kpisPromise = page.waitForResponse(
    (response) => /\/api\/savings\/kpis(?:\?|$)/.test(response.url()) && response.request().method() === "GET",
    { timeout: 30_000 },
  );
  await page.goto("/savings");
  const response = await kpisPromise;
  expect(response.ok(), "GET /api/savings/kpis must succeed for A17-S1").toBeTruthy();
  const body = (await response.json()) as KpiBody;
  return { body, response };
}

test.describe("A17-S1 — Savings verified (w17)", () => {
  test.skip(() => !SIGN_IN_READY, MISSING_SIGN_IN_ENV);
  test.skip(() => FIXTURE_TENANT_ID === "", MISSING_TENANT_ENV);

  let context: BrowserContext;
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    context = await browser.newContext();
    page = await context.newPage();
    await signInWithEntra(page);
    await useWorkspace(page, FIXTURE_TENANT_ID, "W17 savings — fixture workspace");
  });

  test.afterAll(async () => {
    await context?.close();
  });

  test("the band shows realized money grouped by currency under Savings verified; reload and a second context agree", async ({ browser }) => {
    const { body } = await openSavingsAndReadKpis(page);

    const band = page.getByRole("group", { name: "Savings KPIs" });
    await expect(band).toBeVisible({ timeout: 15_000 });
    const cells = band.locator(".savings-kpi-cell");
    await expect(cells).toHaveCount(4);
    // Verified money leads the band; contracts analyzed and upcoming renewals moved to the
    // portfolio-context strip under it.
    await expect(cells.nth(0)).toContainText("Savings verified");
    await expect(cells.nth(1)).toContainText("Savings identified");
    await expect(cells.nth(2)).toContainText("Savings in progress");
    await expect(cells.nth(3)).toContainText("Savings potential");
    await expect(cells.nth(0)).not.toContainText("Realized");
    const context = page.getByRole("navigation", { name: "Portfolio context" });
    await expect(context).toContainText("Contracts analyzed");
    await expect(context).toContainText("Upcoming renewals");

    for (const bucket of body.savingsRealized) {
      expect(bucket).toHaveProperty("amount");
      expect(bucket).toHaveProperty("count");
      expect(bucket).not.toHaveProperty("low");
      expect(bucket).not.toHaveProperty("high");
    }

    const verified = verifiedCell(page);
    if (body.savingsRealized.length === 0) {
      await expect(verified).toContainText("—");
      await expect(verified).toContainText("no verified savings recorded yet");
      await expect(verified).not.toContainText("0");
    } else {
      for (const bucket of body.savingsRealized) {
        await expect(verified).toContainText(formatAmount(bucket.currency, bucket.amount));
      }
      const summed = body.savingsRealized.reduce((total, bucket) => total + bucket.amount, 0);
      if (body.savingsRealized.length > 1) {
        await expect(verified).not.toContainText(formatAmount(body.savingsRealized[0].currency, summed));
      }
    }

    for (const bucket of body.savingsIdentified) {
      if (bucket.low !== bucket.high) {
        const range = `${bucket.currency} ${new Intl.NumberFormat("en-GB").format(Math.round(bucket.low))}–${new Intl.NumberFormat("en-GB").format(Math.round(bucket.high))}`;
        await expect(verified).not.toContainText(range);
      }
    }

    const beforeReload = (await verified.innerText()).trim();
    await page.reload();
    await expect(verifiedCell(page)).toBeVisible({ timeout: 15_000 });
    expect((await verifiedCell(page).innerText()).trim()).toBe(beforeReload);

    const second = await browser.newContext();
    const secondPage = await second.newPage();
    try {
      await signInWithEntra(secondPage);
      await useWorkspace(secondPage, FIXTURE_TENANT_ID, "W17 savings — second context");
      await openSavingsAndReadKpis(secondPage);
      await expect(verifiedCell(secondPage)).toBeVisible({ timeout: 15_000 });
      expect((await verifiedCell(secondPage).innerText()).trim()).toBe(beforeReload);
    } finally {
      await second.close();
    }
  });

  test("recording an outcome that realizes an opportunity moves verified money; a null propagation does not", async ({ request }) => {
    const { body: before, response } = await openSavingsAndReadKpis(page);
    const beforeText = (await verifiedCell(page).innerText()).trim();
    const headers = response.request().headers();
    const authorization = headers["authorization"];
    const apiOrigin = new URL(response.url()).origin;
    expect(authorization, "A17-S1 needs the bearer the SPA already sent").toBeTruthy();

    const apiHeaders = {
      Authorization: authorization,
      "X-Tenant-Id": FIXTURE_TENANT_ID,
      "Content-Type": "application/json",
    };

    const quotesResponse = await request.get(`${apiOrigin}/api/quotes`, { headers: apiHeaders });
    test.skip(!quotesResponse.ok(), "A17-S1 recording walk needs at least one quote in the fixture tenant");
    const quotesBody = (await quotesResponse.json()) as { items?: { id: string }[] };
    const quoteId = quotesBody.items?.[0]?.id;
    test.skip(quoteId === undefined, "A17-S1 recording walk needs at least one quote in the fixture tenant");

    const savingsResponse = await request.get(`${apiOrigin}/api/savings`, { headers: apiHeaders });
    expect(savingsResponse.ok()).toBeTruthy();
    const savingsBody = (await savingsResponse.json()) as {
      items?: { id: string; currency: string; status: string }[];
    };
    const identified = (savingsBody.items ?? []).find((item) => item.status === "Identified");

    const nullPropagation = await request.post(`${apiOrigin}/api/negotiations/outcomes`, {
      headers: apiHeaders,
      data: {
        quoteId,
        originalQuoteTotal: 1000,
        targetPrice: 900,
        finalPrice: 950,
        negotiationDurationDays: 1,
        leversUsed: ["Term"],
      },
    });
    expect(nullPropagation.status(), "capture without a linked opportunity must still 201").toBe(201);
    const nullBody = (await nullPropagation.json()) as { savingsPropagated: boolean | null };
    if (nullBody.savingsPropagated === null) {
      await page.reload();
      await expect(verifiedCell(page)).toBeVisible({ timeout: 15_000 });
      expect((await verifiedCell(page).innerText()).trim()).toBe(beforeText);
    }

    test.skip(identified === undefined, "A17-S1 positive walk needs an Identified opportunity to realize");

    const uniqueAmount = 12_347;
    const propagate = await request.post(`${apiOrigin}/api/negotiations/outcomes`, {
      headers: apiHeaders,
      data: {
        quoteId,
        originalQuoteTotal: uniqueAmount + 1_000,
        targetPrice: uniqueAmount,
        finalPrice: 1_000,
        negotiationDurationDays: 2,
        leversUsed: ["Term"],
        savingsOpportunityId: identified!.id,
      },
    });
    expect(propagate.status()).toBe(201);
    const propagateBody = (await propagate.json()) as { savingsPropagated: boolean | null; realizedSaving: number };
    expect(propagateBody.savingsPropagated, "AC-1: a linked outcome must propagate").toBe(true);

    await page.reload();
    const after = await openSavingsAndReadKpis(page);
    const beforeAmount =
      before.savingsRealized.find((bucket) => bucket.currency === identified!.currency)?.amount ?? 0;
    const afterAmount =
      after.body.savingsRealized.find((bucket) => bucket.currency === identified!.currency)?.amount ?? 0;
    expect(afterAmount).toBe(beforeAmount + propagateBody.realizedSaving);
    await expect(verifiedCell(page)).toContainText(formatAmount(identified!.currency, afterAmount));
    await expect(verifiedCell(page)).not.toContainText("Realized");
  });
});
