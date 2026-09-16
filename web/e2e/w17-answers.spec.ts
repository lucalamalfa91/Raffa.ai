import { test, expect, type BrowserContext, type Page } from "@playwright/test";

/**
 * N16 — Contract 360 answers band, on deployed `dev` (task E21/F03/US02/T01, NW-62;
 * ADR-012 w17 clauses 39 and 41). Parent story us-02-answers-band-web AC-1…AC-3.
 *
 * On a validated contract, **Where you can save** and **When you must move** render
 * the server's `/strategy` pack — a figure + lever, or a representative band with
 * Ask provenance (`adapter A, n = 214`) on the detail line — and
 * `SAVINGS_NOT_YET_AVAILABLE` / `LEVER_NOT_YET_AVAILABLE` ("Not yet available" /
 * Benchmark Service) appear **only** where that fetch was made and the source
 * genuinely had nothing. A spec that only greps for the constant's absence would
 * pass on a screen that never called `/strategy`; this file asserts the **rendered**
 * state against the strategy response that actually landed.
 *
 * ## Honesty note
 *
 * This file is **acceptance-runbook evidence, not a CI gate** (ADR-012 w17 clause 31
 * and §12): no workflow runs Playwright, and nothing here presents this spec as one.
 * Parsed with `npx playwright test --list`; a real green walk needs the deployed
 * environment, a fixture-seeded tenant and an Entra test account (ADR-016).
 *
 * Selectors are read from the committed source they drive
 * (`routes/contracts/contract360/{index,AnswersBand}.tsx`,
 * `routes/contracts/PortfolioTable.tsx`, `routes/signin/workspaceStore.ts`).
 */

const BASE_URL = process.env.RAFFA_E2E_BASE_URL ?? "";
const ENTRA_EMAIL = process.env.RAFFA_E2E_ENTRA_EMAIL ?? "";
const ENTRA_PASSWORD = process.env.RAFFA_E2E_ENTRA_PASSWORD ?? "";
const FIXTURE_TENANT_ID = process.env.RAFFA_E2E_TENANT_ID ?? "";

const SIGN_IN_READY = BASE_URL !== "" && ENTRA_EMAIL !== "" && ENTRA_PASSWORD !== "";

const MISSING_SIGN_IN_ENV =
  "RAFFA_E2E_BASE_URL / RAFFA_E2E_ENTRA_EMAIL / RAFFA_E2E_ENTRA_PASSWORD are not set — " +
  "see web/README.md 'End-to-end (Ask Raffa V2 pilot path)' for how to supply them against `dev`. " +
  "This spec is runbook evidence for N16, not a CI gate (ADR-012 w17 clause 31).";

const MISSING_TENANT_ENV =
  "RAFFA_E2E_TENANT_ID is not set — N16 needs the fixture-seeded workspace with validated " +
  "contracts. Walking an empty self-created workspace would prove the placeholder, not the band.";

const CURRENT_WORKSPACE_KEY = "raffa.signin.currentWorkspace";

const SAVINGS_NOT_YET_AVAILABLE = "Not yet available";
const LEVER_NOT_YET_AVAILABLE = /Benchmark Service/;

type StrategyPack = {
  whenYouMustMove?: {
    renewalDate: string | null;
    cancellationDeadline: string | null;
    daysLeft: number | null;
    passedDeadline: boolean;
    explanation: string;
  };
  whereYouCanPush?: { leverType: string; rationale: string; citationKeys: string[] }[];
  targets?: {
    description: string;
    openingTarget: number | null;
    acceptableRangeLow: number | null;
    acceptableRangeHigh: number | null;
    walkAwayThreshold: number | null;
    explanation: string;
  }[];
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

test.describe("N16 — Contract 360 answers band (w17)", () => {
  test.skip(() => !SIGN_IN_READY, MISSING_SIGN_IN_ENV);
  test.skip(() => FIXTURE_TENANT_ID === "", MISSING_TENANT_ENV);

  let context: BrowserContext;
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    context = await browser.newContext();
    page = await context.newPage();
    await signInWithEntra(page);
    await useWorkspace(page, FIXTURE_TENANT_ID, "W17 answers — fixture workspace");
  });

  test.afterAll(async () => {
    await context?.close();
  });

  test("Where you can save and When you must move render the strategy pack; not-yet copy only after a real, empty-or-failed call", async () => {
    await page.goto("/contracts");
    const contractLink = page.locator(".portfolio-cell-contract a").first();
    await expect(contractLink, "N16 needs a validated contract in the fixture tenant").toBeVisible({ timeout: 30_000 });

    const strategyResponsePromise = page.waitForResponse(
      (response) => /\/api\/contracts\/[^/]+\/strategy(?:\?|$)/.test(response.url()),
      { timeout: 15_000 },
    );
    await contractLink.click();
    await expect(page).toHaveURL(/\/contracts\/[^/]+$/, { timeout: 15_000 });

    const strategyResponse = await strategyResponsePromise;
    const strategyStatus = strategyResponse.status();
    const strategyUrl = strategyResponse.url();
    let strategyPack: StrategyPack | null = null;
    if (strategyStatus === 200) {
      try {
        strategyPack = (await strategyResponse.json()) as StrategyPack;
      } catch {
        strategyPack = null;
      }
    }

    const band = page.getByRole("region", { name: "Answers" });
    await expect(band).toBeVisible({ timeout: 15_000 });
    expect(strategyUrl).toMatch(/\/api\/contracts\/[^/]+\/strategy/);

    const cells = band.locator(".contract360-answer");
    await expect(cells).toHaveCount(3);
    const save = cells.nth(0);
    const move = cells.nth(1);

    await expect(save).toContainText("Where you can save");
    await expect(move).toContainText("When you must move");

    const saveText = (await save.innerText()).trim();
    const moveText = (await move.innerText()).trim();

    expect(saveText, "N16: the band must not mention 'weak'").not.toMatch(/weak/i);
    expect(moveText, "N16: the band must not mention 'weak'").not.toMatch(/weak/i);
    expect(saveText, "ADR-018 w15 clause 6: no re-indexing banner").not.toMatch(/re-indexing|catching up/i);
    expect(moveText, "ADR-018 w15 clause 6: no re-indexing banner").not.toMatch(/re-indexing|catching up/i);
    expect(moveText, "AC-4: never 'Not determined'").not.toContain("Not determined");

    const pack = strategyPack;
    const calledAndFailed = strategyStatus !== 200 || pack === null;
    const firstTarget = pack?.targets?.[0];
    const representative =
      firstTarget !== undefined &&
      firstTarget.explanation.toLowerCase().includes("representative") &&
      firstTarget.acceptableRangeLow !== null &&
      firstTarget.acceptableRangeHigh !== null;
    const figure = firstTarget !== undefined && firstTarget.openingTarget !== null && (pack?.whereYouCanPush?.length ?? 0) > 0;
    const hasMoveDate = pack?.whenYouMustMove?.cancellationDeadline != null || pack?.whenYouMustMove?.renewalDate != null;

    if (calledAndFailed || (!representative && !figure)) {
      expect(
        saveText,
        "AC-3: 'Not yet available' is reachable only after the strategy call returned nothing or failed",
      ).toContain(SAVINGS_NOT_YET_AVAILABLE);
      expect(saveText).toMatch(LEVER_NOT_YET_AVAILABLE);
    } else if (representative) {
      expect(saveText).toMatch(/representative/i);
      expect(saveText).toMatch(/adapter /);
      expect(saveText).not.toContain(SAVINGS_NOT_YET_AVAILABLE);
    } else {
      expect(saveText).toMatch(/\d/);
      expect(saveText).not.toContain(SAVINGS_NOT_YET_AVAILABLE);
    }

    if (calledAndFailed) {
      expect(moveText).toContain(SAVINGS_NOT_YET_AVAILABLE);
    } else if (!hasMoveDate) {
      await expect(move.getByRole("link", { name: "Add the end date" })).toHaveAttribute("href", /\/contracts\/[^/]+\/review$/);
    } else {
      const deadline = pack?.whenYouMustMove?.cancellationDeadline ?? pack?.whenYouMustMove?.renewalDate;
      expect(deadline).toBeTruthy();
      const [year, month, day] = deadline!.split("-");
      expect(moveText).toContain(`${day}/${month}/${year}`);
      if (pack?.whenYouMustMove?.daysLeft != null && pack.whenYouMustMove.daysLeft >= 0) {
        expect(moveText).toMatch(new RegExp(`${pack.whenYouMustMove.daysLeft}\\s+days?`));
      }
    }
  });
});
