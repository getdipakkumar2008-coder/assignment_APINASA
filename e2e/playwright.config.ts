import { defineConfig } from '@playwright/test';
import * as path from 'path';

/**
 * End-to-end tests: a real Chrome drives the real Angular dev server, which proxies to the real API process.
 *
 *   npm run e2e        deterministic suite (no NASA calls; fake key; runs anywhere)
 *   npm run e2e:live   adds the full path through the real NASA services (needs NASA__ApiKey in .env or the environment)
 *
 * The suites use the dev ports (API 5180, UI 4200) because the API only accepts the UI's origin. Stop any running dev
 * servers first: reuseExistingServer is off so a test can never silently run against someone's real data.
 */
const live = process.env.E2E_LIVE === '1';
const tmp = path.resolve(__dirname, '.tmp', live ? 'live' : 'deterministic');
const repo = path.resolve(__dirname, '..');

export const paths = {
  tmp,
  datesFile: path.join(tmp, 'dates.txt'),
  photosRoot: path.join(tmp, 'photos'),
};

export default defineConfig({
  testDir: './tests',
  testMatch: live ? /live\.spec\.ts/ : /ui-api\.spec\.ts/,
  globalSetup: './global-setup.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: live ? 120_000 : 30_000,
  expect: { timeout: live ? 60_000 : 10_000 },
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL: 'http://localhost:4200',
    channel: 'chrome', // the installed Google Chrome: no browser download needed
    headless: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: 'dotnet run --no-build --project src/MarsPhotos.Api',
      cwd: repo,
      url: 'http://localhost:5180/health',
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        Run__InputFile: paths.datesFile,
        Run__OutputRoot: paths.photosRoot,
        ...(live
          ? {} // the API loads the real key from the git-ignored .env (or NASA__ApiKey)
          : { NASA__ApiKey: 'e2e-test-key-0000', MARSPHOTOS_SKIP_DOTENV: '1' }),
      },
    },
    {
      command: 'npm start',
      cwd: path.join(repo, 'src', 'marsphotos-ui'),
      url: 'http://localhost:4200',
      reuseExistingServer: false,
      timeout: 180_000,
    },
  ],
});
