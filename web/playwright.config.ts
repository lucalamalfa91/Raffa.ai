import { defineConfig, devices } from "@playwright/test";

/**
 * Playwright config for the §20 Day-1 web-pass integration gate (task
 * E08/F04/US01/T01, us-01-final-integration; ADR-016 promotion, ADR-018 IA,
 * ADR-020 screen inventory, ADR-022 Day-1 demo auth + fixture seed).
 *
 * The task's own "Files to create or modify" table names only `e2e/day1.spec.ts`,
 * but a Playwright spec is inert without a runner config and a `package.json`
 * entry point -- this file and the `test:e2e` script are the minimal,
 * necessary infrastructure that makes that one named deliverable executable at
 * all, the same kind of pragmatic, documented scope decision earlier tasks in
 * this run recorded (e.g. `reports/open-questions.md`'s OQ-impl-001/002 for a
 * task-text-vs-ADR-014 path mismatch). See `web/README.md`'s "End-to-end
 * (Day-1 browser walk)" section for the full reasoning, including why this
 * lives at `web/e2e/` (the real, already-scaffolded product tree) and not
 * `workspace/contigo-web/e2e/` (the task text's literal path, which is not
 * where any of the ten real screens this spec drives actually live).
 *
 * There is no `localhost` fallback for `baseURL`: the parent story's own AC-2
 * forbids walking this suite against the local-dev `config.json` placeholder
 * ("UI matches ... not localhost config.json"). `CONTIGO_E2E_BASE_URL` must
 * be the real `demo` Static Web App origin; `e2e/day1.spec.ts` asserts this
 * at runtime too and skips (never silently falls back) when it, or the Entra
 * test-account env vars the real sign-in step needs, are not supplied.
 */
export default defineConfig({
  testDir: "./e2e",
  timeout: 240_000,
  expect: {
    timeout: 15_000,
  },
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [["html", { open: "never", outputFolder: "playwright-report" }], ["list"]],
  use: {
    baseURL: process.env.CONTIGO_E2E_BASE_URL,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
