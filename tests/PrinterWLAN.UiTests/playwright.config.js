import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: 1,
  timeout: 45_000,
  use: { baseURL: process.env.PRINTERWLAN_BASE_URL || 'http://127.0.0.1:8080', trace: 'retain-on-failure' },
  reporter: [['list'], ['html', { open: 'never' }]]
});
