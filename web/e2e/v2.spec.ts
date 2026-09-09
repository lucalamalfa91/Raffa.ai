import { test, expect, type BrowserContext, type Locator, type Page } from "@playwright/test";

/**
 * Ask Contigo **V2 pilot path** — browser walk on `dev` (task E13/F11/US01/T01,
 * us-01-integration AC-3; `inputs/requirements.md` §10 acceptance A1–A14;
 * ADR-024 "Implications for the decomposition" → `web/e2e/v2.spec.ts`;
 * `inputs/design/prototypes/contigo-v2/ia-v2.md` "Pilot path").
 *
 * This file is the declared replacement for `e2e/day1.spec.ts`, which walks the
 * V1 information architecture and is red since task E13/F09/US01/T01 moved the
 * app to the V2 shell (`web/README.md` → "End-to-end (Day-1 browser walk)" →
 * "Known regression"). Nothing here is copied from that file's selectors: the
 * V2 screens are different components with different copy (no `.ask-citation-chip`,
 * no `Home` screen, no `Use sample file` button).
 *
 * ## Which acceptance rows run here
 *
 * | Row | Covered by | Gate |
 * |---|---|---|
 * | A1 | "a recipe and an unreadable image are refused" (+ "…and the MSA still lands") | always (the MSA half needs `CONTIGO_E2E_MSA_PATH`) |
 * | A2 | "a scanned order form is OCR'd, admitted and shows its supplier" | `E2E_LIVE_FOUNDRY=1` + `CONTIGO_E2E_ORDER_FORM_PNG` |
 * | A3 | "ciao → warm decline, never an abstain block" | always |
 * | A4 | "posso fare causa a Salesforce? → refusal + commercial analogue" | always |
 * | A5 | "is my Allianz contract above market?" | `E2E_LIVE_FOUNDRY=1` |
 * | A6 | "come dovrei affrontare il rinnovo Salesforce?" | `E2E_LIVE_FOUNDRY=1` |
 * | A7 | "quali sono i contratti più critici…?" (new chat) | `E2E_LIVE_FOUNDRY=1` |
 * | A8 | "cosa sai fare? → feature cards with working links" | always |
 * | A9 | "a reply carries a citation card and an action, never a guid or a route line" | always |
 * | A10 | "reload /ask/<id> resumes the conversation" | always |
 * | A11–A13 | **not here** — see below | — |
 * | A14 | "/ lands on /ask; the rail is two-tier; the secondary tier is greyed before validation" | always |
 *
 * A2 / A5 / A6 / A7 are `test.skip`ped with the named reason "requires live
 * Foundry" unless `E2E_LIVE_FOUNDRY=1`, exactly as the task text and
 * OQ-askv2-009 require (`inputs/requirements.md` §13 A9: "Live Foundry on
 * `dev` / `demo` is required for acceptance A2–A8; the fixture gateway proves
 * the same paths in CI"). A8/A9 stay unconditional because the fixture gateway
 * answers them deterministically (the capability catalog is static, and the
 * "no chrome" assertion is a property of every reply, model or not).
 *
 * A11 (cross-tenant isolation), A12 (no tools / grounding in the Foundry
 * request body) and A13 (golden set) are deliberately **not** browser
 * assertions: they are proven by `Contigo.IntegrationTests`,
 * `Contigo.AiGateway.Tests` and `Contigo.AiEval` respectively, all of which run
 * under `dotnet test Contigo.slnx` in `.github/workflows/backend.yml`. A
 * browser cannot observe a request body or another tenant's rows without
 * fabricating a second identity; `docs/ask-v2-acceptance.md` names the real
 * check for each.
 *
 * ## Required environment (no localhost default, no fabricated data)
 *
 * | Variable | Meaning |
 * |---|---|
 * | `CONTIGO_E2E_BASE_URL` | The `dev` Static Web App origin (also used for `demo`). |
 * | `CONTIGO_E2E_ENTRA_EMAIL` | A test-account UPN on that environment's Entra tenant. |
 * | `CONTIGO_E2E_ENTRA_PASSWORD` | That account's password. |
 * | `CONTIGO_E2E_TENANT_ID` | The **fixture-seeded** workspace id (the tenant with validated contracts). Without it the pilot-path suite skips rather than walking an empty, self-created workspace and calling that a pass. |
 * | `CONTIGO_E2E_MSA_PATH` | Optional: a real contract PDF on disk, for A1's "the MSA still lands" half. |
 * | `CONTIGO_E2E_ORDER_FORM_PNG` | Optional: a real scanned order-form PNG, for A2. |
 * | `CONTIGO_E2E_EMPTY_TENANT_ID` | Optional: a workspace id known to hold **zero** validated contracts, for A14's greyed-rail assertion. Defaults to a random uuid, which is equivalent for that assertion (an unknown tenant has no contracts) and is annotated as such. |
 * | `E2E_LIVE_FOUNDRY` | `1` to run A2 / A5 / A6 / A7. |
 *
 * Sign-in is the real Entra PKCE redirect (ADR-022 keeps it for V2): there is
 * no auth bypass in this codebase and inventing one would defeat the point of
 * an end-to-end gate. The test account must be excluded from interactive MFA,
 * the same constraint `day1.spec.ts` already documents.
 *
 * ## Why the workspace is pinned through `sessionStorage`
 *
 * There is still no endpoint that lists the workspaces an identity belongs to
 * (`src/routes/signin/workspaceStore.ts`'s own documented gap), so a stock
 * browser context always lands on "No workspaces yet" and would create an
 * empty workspace — a workspace with no validated contracts, where Ask is
 * correctly **off** and A1/A3–A10 cannot be observed at all. Rather than assert
 * an honestly-empty screen and call it acceptance, this suite writes the same
 * `contigo.signin.currentWorkspace` key the app itself writes (`workspaceStore.ts`
 * `CURRENT_WORKSPACE_KEY`) with the operator-supplied fixture tenant id — the
 * documented seam, not a mock: every API call then carries that tenant as
 * `X-Tenant-Id` exactly as a human click would.
 *
 * ## Honesty note
 *
 * Selectors and copy below were read from the currently-committed V2 source
 * (`src/components/shell/{navItems.ts,RailNav.tsx}`, `src/routes/ask/**`,
 * `src/routes/documents/**`, `src/components/ask-bar/GlobalAskBar.tsx`) and are
 * cited inline. This file has been parsed by `npx playwright test --list`; a
 * real green run needs a deployed environment, a fixture-seeded tenant and an
 * Entra test account, which is an operator/CI action (ADR-016), not something
 * a Helix implementer session can perform.
 */

const BASE_URL = process.env.CONTIGO_E2E_BASE_URL ?? "";
const ENTRA_EMAIL = process.env.CONTIGO_E2E_ENTRA_EMAIL ?? "";
const ENTRA_PASSWORD = process.env.CONTIGO_E2E_ENTRA_PASSWORD ?? "";
const FIXTURE_TENANT_ID = process.env.CONTIGO_E2E_TENANT_ID ?? "";
const EMPTY_TENANT_ID = process.env.CONTIGO_E2E_EMPTY_TENANT_ID ?? "";
const MSA_PATH = process.env.CONTIGO_E2E_MSA_PATH ?? "";
const ORDER_FORM_PNG_PATH = process.env.CONTIGO_E2E_ORDER_FORM_PNG ?? "";
const LIVE_FOUNDRY = process.env.E2E_LIVE_FOUNDRY === "1";

const SIGN_IN_READY = BASE_URL !== "" && ENTRA_EMAIL !== "" && ENTRA_PASSWORD !== "";

const MISSING_SIGN_IN_ENV =
  "CONTIGO_E2E_BASE_URL / CONTIGO_E2E_ENTRA_EMAIL / CONTIGO_E2E_ENTRA_PASSWORD are not set — " +
  "see web/README.md 'End-to-end (Ask Contigo V2 pilot path)' for how to supply them against `dev`.";

const MISSING_TENANT_ENV =
  "CONTIGO_E2E_TENANT_ID is not set — the V2 pilot path needs the fixture-seeded workspace " +
  "(the one with validated contracts). A self-created, empty workspace turns Ask off by design " +
  "(R-ASK-10), so walking it would prove nothing. Seed one with " +
  "`.github/workflows/seed-demo-fixture.yml` + `.github/workflows/reprocess-tenant-documents.yml` " +
  "and pass its id.";

const LIVE_FOUNDRY_REASON =
  "requires live Foundry (OQ-askv2-009; inputs/requirements.md §13 A9) — set E2E_LIVE_FOUNDRY=1 " +
  "when the target environment has AiGateway__Endpoint wired.";

/** `src/routes/signin/workspaceStore.ts` → `CURRENT_WORKSPACE_KEY`. */
const CURRENT_WORKSPACE_KEY = "contigo.signin.currentWorkspace";

/** Engineer chrome that `inputs/requirements.md` R-ASK-08 / A9 forbid in any rendered reply. */
const FORBIDDEN_REPLY_CHROME = [
  "Structured query",
  "Clause retrieval",
  "Document:",
  "not wired",
  "chunk ",
];

const GUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i;

// ---------------------------------------------------------------------------
// In-memory upload fixtures
//
// This repo ships no `*.pdf` / `*.png` asset on purpose — `src/routes/documents/
// sampleDocument.ts` records that fact and builds its own minimal PDF in the
// browser for the same reason. A1 needs a file whose *extracted text* reads as a
// recipe (so the admission gate can answer "not a contract" rather than "no
// readable text"), which the sample's content-free PDF cannot provide, so this
// builds a real, structurally-valid, single-page PDF with a text stream and a
// correct xref table. Nothing here mocks the API: the bytes go through the
// ordinary `<input type="file">` the human uses.
// ---------------------------------------------------------------------------

function escapePdfText(value: string): string {
  return value.replace(/\\/g, "\\\\").replace(/\(/g, "\\(").replace(/\)/g, "\\)");
}

/** A valid one-page PDF whose page content is `lines`, rendered in Helvetica 12. */
function buildTextPdf(lines: string[]): Buffer {
  const body = [
    "BT",
    "/F1 12 Tf",
    "72 720 Td",
    "14 TL",
    ...lines.map((line) => `(${escapePdfText(line)}) Tj T*`),
    "ET",
  ].join("\n");

  const objects = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
      "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
    "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    `<< /Length ${Buffer.byteLength(body, "latin1")} >>\nstream\n${body}\nendstream`,
  ];

  let pdf = "%PDF-1.4\n";
  const offsets: number[] = [];
  objects.forEach((object, index) => {
    offsets.push(Buffer.byteLength(pdf, "latin1"));
    pdf += `${index + 1} 0 obj\n${object}\nendobj\n`;
  });

  const startXref = Buffer.byteLength(pdf, "latin1");
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (const offset of offsets) {
    pdf += `${offset.toString().padStart(10, "0")} 00000 n \n`;
  }
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${startXref}\n%%EOF\n`;

  return Buffer.from(pdf, "latin1");
}

/** A recipe, not a contract — R-DOC-03 AC-1 / A1's `carbonara.pdf`. */
const CARBONARA_PDF = buildTextPdf([
  "Spaghetti alla Carbonara - ricetta per 4 persone",
  "Ingredienti: 320 g di spaghetti, 150 g di guanciale, 4 tuorli,",
  "50 g di pecorino romano, pepe nero macinato fresco.",
  "Rosolare il guanciale in padella senza olio finche' e' croccante.",
  "Sbattere i tuorli con il pecorino e una macinata di pepe.",
  "Scolare la pasta al dente e mantecare fuori dal fuoco.",
  "Servire subito con altro pecorino e pepe.",
]);

/**
 * A 1x1 PNG: valid magic bytes (so it passes the format sniffer, R-DOC-02) with
 * no readable text at all, so OCR falls below `Documents:MinReadableChars` and
 * the gate answers `no_readable_text`, not "not a contract" — R-DOC-03 AC-2 /
 * A1's `nonna.jpg`.
 */
const UNREADABLE_PNG = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
  "base64",
);

/** Fallback MSA text when the operator supplies no real contract PDF. */
const FALLBACK_MSA_PDF = buildTextPdf([
  "MASTER SERVICES AGREEMENT",
  "This Master Services Agreement is entered into as of 1 January 2024",
  "between Salesforce, Inc. (the Supplier) and the Customer.",
  "1. Term. The initial term is twenty-four (24) months.",
  "2. Renewal. This agreement shall automatically renew for successive",
  "twelve (12) month periods unless either party gives ninety (90) days",
  "written notice prior to the end of the then-current term.",
  "3. Fees. The annual subscription fee is CHF 148,000, payable annually",
  "in advance, subject to an uplift of no more than five per cent (5%).",
  "4. Limitation of liability. Each party's aggregate liability is capped",
  "at the fees paid in the twelve (12) months preceding the claim.",
]);

// ---------------------------------------------------------------------------
// Shared browser helpers
// ---------------------------------------------------------------------------

/**
 * Real Entra ID redirect sign-in — the same flow `day1.spec.ts#signInWithEntra`
 * drives (`src/routes/signin/SignInScreen.tsx`'s button is
 * "Continue with Microsoft Entra ID"). Kept here rather than imported so the
 * V1 spec can be deleted without breaking this one.
 */
async function signInWithEntra(page: Page): Promise<void> {
  await page.goto("/");
  await page.getByRole("button", { name: /continue with microsoft entra id/i }).click();
  await page.waitForURL(/login\.microsoftonline\.com/i, { timeout: 60_000 });

  await page.locator('input[name="loginfmt"]').fill(ENTRA_EMAIL);
  await page.locator("#idSIButton9").click();

  await page.locator('input[name="passwd"]').waitFor({ state: "visible", timeout: 30_000 });
  await page.locator('input[name="passwd"]').fill(ENTRA_PASSWORD);
  await page.locator("#idSIButton9").click();

  // "Stay signed in?" is optional and tenant-configurable.
  try {
    await page.locator("#idSIButton9").waitFor({ state: "visible", timeout: 8_000 });
    await page.locator("#idSIButton9").click();
  } catch {
    // Not shown for this tenant/account — nothing to dismiss.
  }

  await page.waitForURL((url) => !/login\.microsoftonline\.com/i.test(url.href), { timeout: 60_000 });
}

/**
 * Point the SPA at one workspace without going through the picker, by writing
 * the exact `sessionStorage` entry `workspaceStore.ts#saveCurrentWorkspace`
 * writes. Every `ApiClient` call then sends it as `X-Tenant-Id`
 * (`src/api/client.ts`: the tenant is a per-call argument sourced from
 * `loadCurrentWorkspace()`), which is precisely what a human selecting that
 * workspace would produce.
 */
async function useWorkspace(page: Page, tenantId: string, name: string): Promise<void> {
  await page.evaluate(
    ([key, id, workspaceName]) => {
      window.sessionStorage.setItem(key, JSON.stringify({ id, name: workspaceName }));
    },
    [CURRENT_WORKSPACE_KEY, tenantId, name] as const,
  );
}

/** The rail — `RailNav.tsx` renders `<nav className="shell-rail" aria-label="Primary">`. */
function rail(page: Page): Locator {
  return page.getByRole("navigation", { name: "Primary" });
}

/** The newest Contigo turn's reply body (`ReplyBody.tsx` → `.reply-body[data-reply-kind]`). */
function latestReply(page: Page): Locator {
  return page.locator('.ask-message[data-role="contigo"] .reply-body').last();
}

/**
 * Ask one question on `/ask` and wait for the reply. Returns the reply body
 * locator and the `data-reply-kind` the app actually chose — never asserted
 * here, so each test can state its own expectation and report the real value
 * when it differs.
 */
async function ask(page: Page, question: string): Promise<{ reply: Locator; kind: string }> {
  const input = page.getByRole("textbox", { name: "Ask Contigo a question", exact: true });
  await input.waitFor({ state: "visible", timeout: 30_000 });
  await input.fill(question);

  const before = await page.locator('.ask-message[data-role="contigo"] .reply-body').count();
  await page.getByRole("button", { name: "Ask", exact: true }).click();

  await expect
    .poll(async () => page.locator('.ask-message[data-role="contigo"] .reply-body').count(), {
      timeout: 120_000,
      message: `Contigo never answered "${question}"`,
    })
    .toBeGreaterThan(before);

  const reply = latestReply(page);
  const kind = (await reply.getAttribute("data-reply-kind")) ?? "";
  test.info().annotations.push({ type: "reply kind", description: `${question} → ${kind}` });
  return { reply, kind };
}

/**
 * R-ASK-08 / A9's negative half, true of *every* reply regardless of kind: no
 * guid, no engineer route line, no `Document:<guid>` chip, no "not wired".
 * Asserted on rendered text, so an `href="/contracts/<guid>"` (a legitimate
 * deep link, R-SYS-03) is not a false positive.
 */
async function expectNoEngineerChrome(reply: Locator): Promise<void> {
  const text = (await reply.innerText()).trim();
  expect(text.length, "a reply must render prose, not an empty body").toBeGreaterThan(0);
  expect(text, "R-ASK-08: a rendered reply never shows a guid").not.toMatch(GUID_PATTERN);
  for (const chrome of FORBIDDEN_REPLY_CHROME) {
    expect(text, `R-ASK-08: a rendered reply never shows "${chrome}"`).not.toContain(chrome);
  }
}

// ---------------------------------------------------------------------------
// A14 — the V2 shell itself, on a workspace with no validated contracts
// ---------------------------------------------------------------------------

test.describe("A14 — Ask is home and the rail is two-tier", () => {
  test.describe.configure({ mode: "serial" });

  let context: BrowserContext;
  let page: Page;

  // Group-level conditional skip (callback form): with it in force Playwright
  // never enters `beforeAll`, so an unconfigured run never attempts a real
  // Entra sign-in — it reports the gate as skipped, with the reason.
  test.skip(() => !SIGN_IN_READY, MISSING_SIGN_IN_ENV);

  test.beforeAll(async ({ browser }) => {
    context = await browser.newContext();
    page = await context.newPage();
    await signInWithEntra(page);

    // "Before validation" needs a workspace with zero validated contracts. An
    // operator-supplied empty tenant is the honest choice; a random uuid is
    // equivalent for this one assertion (an unknown tenant has no contracts,
    // `GET /api/contracts` returns an empty page) and is reported as such
    // rather than passed off as a real workspace.
    const tenantId = EMPTY_TENANT_ID !== "" ? EMPTY_TENANT_ID : crypto.randomUUID();
    await useWorkspace(page, tenantId, "V2 acceptance — empty workspace");
    console.log(
      EMPTY_TENANT_ID !== ""
        ? `[A14] empty workspace: CONTIGO_E2E_EMPTY_TENANT_ID=${tenantId}`
        : `[A14] empty workspace: generated tenant ${tenantId} (set CONTIGO_E2E_EMPTY_TENANT_ID to pin a real one)`,
    );
  });

  test.afterAll(async () => {
    await context?.close();
  });

  test("`/` lands on `/ask` (R-WEB-01)", async () => {
    await page.goto("/");
    await expect(page).toHaveURL(/\/ask$/);
    // No Home item anywhere in the rail — ADR-024's V2 IA removed it.
    await expect(rail(page).getByRole("link", { name: "Home", exact: true })).toHaveCount(0);
  });

  test("the rail is two-tier, in the prototype's order (R-WEB-02)", async () => {
    await page.goto("/ask");
    const labels = rail(page).locator(".shell-rail-item-label");
    await expect(labels).toHaveText(["Ask Contigo", "Documents", "Portfolio", "Renewals", "Quote check"]);
    await expect(rail(page).locator(".shell-rail-section-kicker")).toHaveText("From your contracts");
    await expect(rail(page).getByRole("link", { name: "+ New chat" })).toBeVisible();
  });

  test("the secondary tier is greyed until the first validated contract (R-WEB-02, R-SYS-04)", async () => {
    await page.goto("/ask");
    for (const label of ["Portfolio", "Renewals", "Quote check"]) {
      const item = rail(page).locator(".shell-rail-secondary-item").filter({ hasText: label });
      await expect(item, `${label} must be greyed before validation`).toHaveClass(/is-greyed/);
    }
    // ...and Ask itself is off, with the prototype's own copy (R-ASK-10).
    await expect(page.getByRole("heading", { name: "Ask needs at least one validated contract." })).toBeVisible();
    await expect(page.locator(".ask-off-actions").getByRole("link")).toHaveAttribute("href", "/documents");
  });
});

// ---------------------------------------------------------------------------
// The pilot path, on the fixture-seeded workspace
// ---------------------------------------------------------------------------

test.describe("V2 pilot path on the fixture-seeded workspace", () => {
  test.describe.configure({ mode: "serial" });

  let context: BrowserContext;
  let page: Page;

  test.skip(() => !SIGN_IN_READY, MISSING_SIGN_IN_ENV);
  test.skip(() => FIXTURE_TENANT_ID === "", MISSING_TENANT_ENV);

  test.beforeAll(async ({ browser }) => {
    context = await browser.newContext();
    page = await context.newPage();
    await signInWithEntra(page);
    await useWorkspace(page, FIXTURE_TENANT_ID, "V2 acceptance — fixture workspace");
    await page.goto("/ask");
    // Everything below assumes Ask is on. If it is not, the workspace passed in
    // has no validated contract and every later assertion would fail for that
    // one reason — say so once, here.
    await expect(
      page.getByRole("heading", { name: "Ask needs at least one validated contract." }),
      `CONTIGO_E2E_TENANT_ID=${FIXTURE_TENANT_ID} has no validated contract — run ` +
        "`.github/workflows/reprocess-tenant-documents.yml` and validate one document first.",
    ).toHaveCount(0);
  });

  test.afterAll(async () => {
    await context?.close();
  });

  // -- A1 ------------------------------------------------------------------

  test("A1 — a recipe and an unreadable image are refused together, and nothing is stored", async () => {
    test.setTimeout(180_000);

    await page.goto("/documents");
    await page.getByLabel("Choose contract files from your computer").setInputFiles([
      { name: "carbonara.pdf", mimeType: "application/pdf", buffer: CARBONARA_PDF },
      { name: "nonna.png", mimeType: "image/png", buffer: UNREADABLE_PNG },
    ]);

    // `UploadResultCard.tsx` renders one `.upload-result-card` per *rejected*
    // file, with the literal badge "Not added" and the reason in
    // `.upload-result-message` (R-DOC-04).
    const cards = page.locator(".upload-result-card");
    await expect(cards, "R-DOC-03: both files are refused by the admission gate").toHaveCount(2, {
      timeout: 120_000,
    });
    await expect(cards.locator(".tag")).toHaveText(["Not added", "Not added"]);

    for (const fileName of ["carbonara.pdf", "nonna.png"]) {
      const card = cards.filter({ hasText: fileName });
      await expect(card).toHaveCount(1);
      const message = (await card.locator(".upload-result-message").innerText()).trim();
      test.info().annotations.push({ type: "rejection", description: `${fileName}: ${message}` });
      // Either documented reason is a pass — which one depends on whether this
      // environment's parser reads the recipe's text (`not_a_contract`) or not
      // (`no_readable_text`). Both are R-DOC-03 refusals; neither is a claim
      // Contigo cannot back.
      expect(message).toMatch(/^Not added: /);
      expect(message.length, "R-DOC-04: the refusal says why, warmly and specifically").toBeGreaterThan(40);
    }

    // R-DOC-04/R-DOC-06: rejected files are session-only and are never counted
    // as documents — they must not appear in the server-backed list.
    await page.getByRole("button", { name: /^All documents ·/ }).click();
    await expect(page.locator(".document-status-table")).toBeVisible();
    await expect(page.locator(".document-status-table").getByText("carbonara.pdf")).toHaveCount(0);
    await expect(page.locator(".document-status-table").getByText("nonna.png")).toHaveCount(0);
  });

  test("A1 — the MSA dropped alongside them still lands", async () => {
    test.skip(
      MSA_PATH === "",
      "no real contract PDF is checked into this repo (see src/routes/documents/sampleDocument.ts) — " +
        "set CONTIGO_E2E_MSA_PATH to a signed MSA to walk the admitted half of A1. The refusal half " +
        "above runs unconditionally.",
    );
    test.setTimeout(240_000);

    await page.goto("/documents");
    const before = await page.locator(".document-status-table tbody tr").count();

    await page.getByLabel("Choose contract files from your computer").setInputFiles([
      { name: "carbonara.pdf", mimeType: "application/pdf", buffer: CARBONARA_PDF },
      { name: "nonna.png", mimeType: "image/png", buffer: UNREADABLE_PNG },
      MSA_PATH,
    ]);

    // R-DOC-01 AC-2: a rejected file never hides the outcome of the others.
    await expect(page.locator(".upload-result-card")).toHaveCount(2, { timeout: 120_000 });
    await expect
      .poll(async () => page.locator(".document-status-table tbody tr").count(), { timeout: 180_000 })
      .toBeGreaterThan(before);

    // ...and that row reaches a terminal status (`getStatusTag` labels), not a
    // spinner that never resolves.
    const statuses = page.locator(".document-status-table tbody tr .tag");
    await expect
      .poll(async () => (await statuses.allInnerTexts()).some((t) => /Needs review|Completed|Failed/.test(t)), {
        timeout: 180_000,
      })
      .toBe(true);
  });

  // -- A2 (live Foundry) ---------------------------------------------------

  test("A2 — a scanned order form is OCR'd, admitted, and shows its supplier", async () => {
    test.skip(!LIVE_FOUNDRY, LIVE_FOUNDRY_REASON);
    test.skip(
      ORDER_FORM_PNG_PATH === "",
      "set CONTIGO_E2E_ORDER_FORM_PNG to a real scanned order-form image — a synthetic PNG has no " +
        "contract text to OCR, and asserting on one would prove nothing about ADR-017.",
    );
    test.setTimeout(300_000);

    await page.goto("/documents");
    const before = await page.locator(".document-status-table tbody tr").count();
    await page.getByLabel("Choose contract files from your computer").setInputFiles([ORDER_FORM_PNG_PATH]);

    await expect(page.locator(".upload-result-card"), "an admitted scan produces no Not added card").toHaveCount(0, {
      timeout: 30_000,
    });
    await expect
      .poll(async () => page.locator(".document-status-table tbody tr").count(), { timeout: 240_000 })
      .toBeGreaterThan(before);

    // R-SUP-04: the supplier column shows a name, never a guid.
    await page.getByRole("button", { name: /^All documents ·/ }).click();
    const supplierCell = page.locator(".document-status-table tbody tr").first().locator("td").nth(1);
    const supplier = (await supplierCell.innerText()).trim();
    test.info().annotations.push({ type: "supplier", description: supplier });
    expect(supplier).not.toMatch(GUID_PATTERN);
    expect(supplier.length).toBeGreaterThan(1);
  });

  // -- A3 ------------------------------------------------------------------

  test("A3 — \"ciao\" is a warm decline with a portfolio hook, never an abstain block", async () => {
    test.setTimeout(180_000);
    await page.goto("/ask");

    const { reply, kind } = await ask(page, "ciao");
    expect(kind, "R-ASK-02: a greeting is a redirect, not an answer or an abstain").toBe("redirect");
    await expect(reply.locator(".abstain-block"), "A3: no abstain block for a greeting").toHaveCount(0);
    await expect(reply).not.toContainText("Cannot determine reliably.");
    // R-ASK-07: redirects are warm prose plus exactly one CTA.
    await expect(reply.locator(".reply-actions a")).toHaveCount(1);
    await expectNoEngineerChrome(reply);
  });

  // -- A4 ------------------------------------------------------------------

  test("A4 — \"posso fare causa a Salesforce?\" is refused with a commercial analogue", async () => {
    test.setTimeout(180_000);
    await page.goto("/ask");

    const { reply, kind } = await ask(page, "Posso fare causa a Salesforce?");
    expect(kind, "R-ASK-02: a legal question is refused, never answered").toBe("refusal");
    await expect(reply.locator(".abstain-block")).toHaveCount(0);

    const action = reply.locator(".reply-actions a").first();
    await expect(action, "R-ASK-02 AC-3: the refusal still routes somewhere useful").toBeVisible();
    const href = await action.getAttribute("href");
    expect(href, "A4: the commercial analogue points at Contract 360").toMatch(/^\/contracts(\/|\?|$)/);
    await expectNoEngineerChrome(reply);
  });

  // -- A5 / A6 / A7 (live Foundry) ----------------------------------------

  test("A5 — \"is my Allianz contract above market?\" cites a band or says the data is thin", async () => {
    test.skip(!LIVE_FOUNDRY, LIVE_FOUNDRY_REASON);
    test.setTimeout(180_000);
    await page.goto("/ask");

    const { reply, kind } = await ask(page, "Is my Allianz contract above market?");
    expect(["answer", "abstain"], `A5: expected a comparison or an honest abstain, got ${kind}`).toContain(kind);

    if (kind === "answer") {
      // R-CMP-01/R-MKT-04: a market claim carries a market citation card whose
      // badge names the provenance.
      const marketCard = reply.locator(".citation-card").filter({ hasText: "Market · representative" });
      await expect(marketCard.first(), "R-MKT-04: every market number is provenance-labelled").toBeVisible();
      await expect(reply).toContainText(/below|in line|above/i);
    } else {
      await expect(reply.locator(".abstain-block")).toBeVisible();
    }
    await expectNoEngineerChrome(reply);
  });

  test("A6 — \"come dovrei affrontare il rinnovo Salesforce?\" narrates the calculators", async () => {
    test.skip(!LIVE_FOUNDRY, LIVE_FOUNDRY_REASON);
    test.setTimeout(180_000);
    await page.goto("/ask");

    const { reply, kind } = await ask(page, "Come dovrei affrontare il rinnovo Salesforce?");
    expect(["answer", "abstain"], `A6: expected a strategy or an honest abstain, got ${kind}`).toContain(kind);

    if (kind === "answer") {
      // R-STR-01: actions to Contract 360 and to the renewal tracker.
      const hrefs = await reply.locator(".reply-actions a").evaluateAll((nodes) =>
        nodes.map((node) => node.getAttribute("href") ?? ""),
      );
      test.info().annotations.push({ type: "actions", description: hrefs.join(" ") });
      expect(hrefs.some((href) => href.startsWith("/contracts"))).toBe(true);
      expect(hrefs.some((href) => href.startsWith("/renewals"))).toBe(true);
      await expect(reply.locator(".citation-card").first()).toBeVisible();
    }
    await expectNoEngineerChrome(reply);
  });

  test("A7 — a new chat answers the portfolio question with a ranked, cited list", async () => {
    test.skip(!LIVE_FOUNDRY, LIVE_FOUNDRY_REASON);
    test.setTimeout(180_000);

    // "+ New chat" in the rail — the V2 principle is that a portfolio question
    // starts its own conversation (R-CONV-02).
    await page.goto("/ask");
    await rail(page).getByRole("link", { name: "+ New chat" }).click();
    await expect(page).toHaveURL(/\/ask$/);

    const { reply, kind } = await ask(page, "Quali sono i contratti più critici e dove posso risparmiare?");
    expect(["answer", "abstain"], `A7: expected a ranking or an honest abstain, got ${kind}`).toContain(kind);

    if (kind === "answer") {
      await expect(reply.locator(".citation-card").first()).toBeVisible();
      await expect(reply.locator(".reply-actions a").first()).toBeVisible();
    }
    await expectNoEngineerChrome(reply);
  });

  // -- A8 ------------------------------------------------------------------

  test("A8 — \"cosa sai fare?\" answers from the capability catalog, with links that work", async () => {
    test.setTimeout(180_000);
    await page.goto("/ask");

    const { reply, kind } = await ask(page, "Cosa sai fare?");
    expect(kind, "R-ASK-02: a capability question is answered from the catalog").toBe("answer");

    // R-SYS-03: a capability answer cites *feature* cards (`corpus: contigo` →
    // `getCorpusBadge` label "Contigo", class `tag-accent`).
    const featureCards = reply.locator(".citation-card").filter({ hasText: "Contigo" });
    await expect(featureCards.first(), "A8: feature cards, not tenant chunks").toBeVisible();

    const hrefs = await reply.locator(".reply-actions a").evaluateAll((nodes) =>
      nodes.map((node) => node.getAttribute("href") ?? ""),
    );
    expect(hrefs.length, "R-SYS-02: every capability reply carries at least one action").toBeGreaterThan(0);
    for (const href of hrefs) {
      // R-ASK-06 guard 3: hrefs come from the catalog, never from the model —
      // so they are in-app routes, never absolute URLs.
      expect(href, "R-SYS-02: actions are in-app routes from the catalog").toMatch(/^\//);
    }
    await expectNoEngineerChrome(reply);

    // "Working links": follow the first one and confirm the app renders it,
    // rather than trusting the string.
    await reply.locator(".reply-actions a").first().click();
    await expect(page).toHaveURL(new RegExp(hrefs[0].split("?")[0].replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
    await expect(rail(page)).toBeVisible();
  });

  // -- A9 ------------------------------------------------------------------

  test("A9 — a reply carries a citation card and an action, and never engineer chrome", async () => {
    test.setTimeout(180_000);
    await page.goto("/ask");

    const { reply, kind } = await ask(page, "When does our Salesforce contract expire?");
    await expectNoEngineerChrome(reply);

    if (kind === "answer") {
      const card = reply.locator(".citation-card").first();
      await expect(card, "R-ASK-08: an answer cites at least one source").toBeVisible();
      // A human citation, not a `Document:<guid>` chip: a title, and a subtitle
      // carrying page/section or the market record's provenance.
      await expect(card.locator(".citation-card-title")).not.toBeEmpty();
      await expect(card.locator(".citation-card-subtitle")).not.toBeEmpty();
      await expect(card.locator(".citation-card-title")).not.toContainText("Document:");
      await expect(
        reply.locator(".reply-actions a").first(),
        "R-SYS-02: an answer offers at least one in-app action",
      ).toBeVisible();
    } else {
      // An abstain/redirect is a legitimate outcome for a tenant that has no
      // Salesforce contract — record it instead of failing on missing data.
      test.info().annotations.push({
        type: "A9",
        description: `reply kind was "${kind}" — no citation card expected; the no-chrome half still asserted.`,
      });
      await expect(reply.locator(".reply-actions a").first()).toBeVisible();
    }
  });

  // -- A10 -----------------------------------------------------------------

  test("A10 — reloading /ask/<id> resumes the conversation with its cards and actions", async () => {
    test.setTimeout(180_000);
    await page.goto("/ask");

    const question = "Which contracts renew in the next 120 days?";
    const { reply } = await ask(page, question);
    const bodyBefore = (await reply.innerText()).trim();

    await expect(page, "R-WEB-01: asking moves the browser onto the conversation's own route").toHaveURL(
      /\/ask\/[0-9a-f-]{36}$/i,
    );
    const conversationUrl = page.url();

    await page.reload();

    // The user's own turn and Contigo's reply both come back from the server
    // (R-CONV-02 AC-1), and the actions are still clickable.
    await expect(page.locator('.ask-message[data-role="you"]').last()).toContainText(question, {
      timeout: 60_000,
    });
    const resumed = latestReply(page);
    await expect(resumed).toBeVisible();
    expect((await resumed.innerText()).trim(), "A10: the resumed reply is the same reply").toBe(bodyBefore);
    await expect(page).toHaveURL(conversationUrl);
    await expectNoEngineerChrome(resumed);

    // Resume from a *fresh* tab too — "from another browser" in A10's own
    // words: same server-side conversation, same user, no client state reused
    // beyond the sign-in cookie and the pinned workspace.
    const second = await context.newPage();
    try {
      await second.goto("/");
      await useWorkspace(second, FIXTURE_TENANT_ID, "V2 acceptance — fixture workspace");
      await second.goto(conversationUrl);
      await expect(second.locator('.ask-message[data-role="you"]').last()).toContainText(question, {
        timeout: 60_000,
      });
      await expect(second.locator(".ask-message[data-role='contigo'] .reply-body").last()).toBeVisible();
    } finally {
      await second.close();
    }
  });
});
