/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { configDefaults } from "vitest/config";

// Plain Vite React SPA (ADR-012): static build output (`dist/`), no SSR/server
// runtime to operate. `test` config lives here (not a separate vitest.config)
// so there is exactly one build/test tool config to keep in sync.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./tests/setup.ts"],
    css: false,
    // `e2e/day1.spec.ts` (task E08/F04/US01/T01) is a Playwright spec, not a
    // Vitest one. Vitest's default include glob (`**/*.{test,spec}.*`) would
    // otherwise pick it up too and fail with "Playwright Test did not expect
    // test.describe() to be called here." Playwright's own config
    // (`playwright.config.ts`) already scopes the other direction
    // (`testDir: "./e2e"`), so excluding `e2e/**` here keeps the two
    // runners' test sets disjoint by construction, not by convention.
    // Spread `configDefaults.exclude` first so this doesn't drop Vitest's
    // own default excludes (node_modules, dist, ...).
    exclude: [...configDefaults.exclude, "e2e/**"],
  },
});
