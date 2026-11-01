import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  timeout: 120_000,
  retries: 0,
  snapshotPathTemplate: '{testDir}/../goldens/{arg}{ext}', // goldens/<viewport>/<shot>.png — produced by reference.capture.spec.ts
  use: { browserName: 'chromium', colorScheme: 'light' },
});
