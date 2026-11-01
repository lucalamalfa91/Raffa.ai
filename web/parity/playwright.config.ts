import { defineConfig } from "@playwright/test";

/**
 * AskRaffa 4.0 parity suite (design_handoff_askraffa_v4/PARITY.md).
 *   reference.capture.spec.ts -> goldens/<viewport>/<shot>.png, from the reference prototype
 *   app.parity.spec.ts        -> the same click-path on the app, compared with the goldens
 *   geometry.spec.ts          -> computed-style assertions
 * PW_CHROMIUM_PATH lets a machine with a pre-installed Chromium use it.
 */
export default defineConfig({
  testDir: ".",
  timeout: 300_000,
  retries: 0,
  workers: 1,
  reporter: [["list"], ["html", { open: "never", outputFolder: "../parity-report" }]],
  snapshotPathTemplate: "{testDir}/goldens/{arg}{ext}",
  use: {
    browserName: "chromium",
    colorScheme: "light",
    launchOptions: process.env.PW_CHROMIUM_PATH ? { executablePath: process.env.PW_CHROMIUM_PATH } : {},
  },
});
