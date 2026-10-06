import AxeBuilder from '@axe-core/playwright';
import { expect, Page, test } from '@playwright/test';
import * as fs from 'fs';
import { paths } from '../playwright.config';

/**
 * The whole product, end to end, with nothing mocked:
 *   real Chrome -> Angular -> proxy -> real API -> real NASA services -> files on disk -> thumbnails in the page.
 *
 * Run with `npm run e2e:live` (needs NASA__ApiKey in the git-ignored .env or the environment, and internet access).
 * Tests share one API process and run in order: the second run must find the first run's photos already on disk.
 */
test.describe.configure({ mode: 'serial' });

const run = (page: Page) => page.getByRole('button', { name: 'Run', exact: true });
const card = (page: Page, text: string) => page.locator('li.card').filter({ hasText: text });

test('the Cancel run button stops a real run in progress and marks unprocessed dates Cancelled', async ({ page, request }) => {
  await page.goto('/');

  await run(page).click();
  await expect(page.getByRole('status')).toContainText('Running'); // the real run is active on the server
  await page.getByRole('button', { name: 'Cancel run' }).click();

  await expect(page.getByRole('status')).toContainText('Run cancelled');
  await expect(page.locator('li.card')).toHaveCount(4);
  await expect(page.locator('li.card .badge', { hasText: 'Cancelled' }).first()).toBeVisible();
  // the last valid date cannot have finished: the run was cut short
  await expect(card(page, 'Jul-13-2016').locator('.badge')).toContainText('Cancelled');
  expect((await request.get('http://localhost:5180/api/runs/latest')).status()).toBe(200);

  // start the real download tests from an empty folder again (partial photos from the cancelled run are removed)
  for (const entry of fs.readdirSync(paths.photosRoot)) {
    fs.rmSync(`${paths.photosRoot}/${entry}`, { recursive: true, force: true });
  }
});

test('Run downloads real photos for the three valid sample dates and reports the invalid one', async ({ page }) => {
  const problems: string[] = [];
  page.on('pageerror', (e) => problems.push(e.message));
  page.on('console', (m) => m.type() === 'error' && problems.push(m.text()));
  await page.goto('/');

  await run(page).click();
  await expect(page.getByRole('status')).toContainText('Run finished');

  for (const input of ['02/27/17', 'June 2, 2018', 'Jul-13-2016']) {
    const c = card(page, input);
    await expect(c.locator('.badge')).toContainText('Success');
    await expect(c.locator('.counts')).toContainText('New: 5');
    await expect(c.locator('.thumbs img')).toHaveCount(5);
    await expect(c.locator('.source')).toContainText('RawImageFeed'); // the Mars Rover Photos API is gone; the fallback served it
    await expect(c.locator('.note')).toContainText('MarsRoverPhotos');
  }

  const invalid = card(page, 'April 31, 2018');
  await expect(invalid.locator('.badge')).toContainText('Invalid date');
  await expect(invalid).toContainText('Day 31 does not exist in April 2018');

  const totals = page.locator('dl.totals');
  await expect(totals.locator('div', { hasText: 'Downloaded' })).toContainText('15');
  expect(problems).toEqual([]);
});

test('the thumbnails are real images served by the API (decoded by the browser, not broken)', async ({ page }) => {
  await page.goto('/'); // the page loads the API's latest run

  const images = page.locator('li.card .thumbs img');
  await expect(images).toHaveCount(15);
  await page.waitForFunction(() => Array.from(document.images).every((img) => img.complete));
  const broken = await images.evaluateAll((list) =>
    (list as HTMLImageElement[]).filter((img) => img.naturalWidth === 0).map((img) => img.getAttribute('src')),
  );

  expect(broken).toEqual([]);
});

test('the photos really exist on the server: a thumbnail URL returns a JPEG', async ({ page, request }) => {
  await page.goto('/');
  const src = await page.locator('li.card .thumbs img').first().getAttribute('src');

  const response = await request.get(`http://localhost:4200${src}`);

  expect(response.status()).toBe(200);
  expect(response.headers()['content-type']).toBe('image/jpeg');
  const bytes = await response.body();
  expect([...bytes.subarray(0, 3)]).toEqual([0xff, 0xd8, 0xff]); // JPEG signature
});

test('a second Run downloads nothing: every photo is skipped because it is already on disk', async ({ page }) => {
  await page.goto('/');

  await run(page).click();
  await expect(page.getByRole('status')).toContainText('Run finished');

  for (const input of ['02/27/17', 'June 2, 2018', 'Jul-13-2016']) {
    await expect(card(page, input).locator('.counts')).toContainText('New: 0 · Skipped: 5');
  }
  const totals = page.locator('dl.totals');
  await expect(totals.locator('div', { hasText: 'Downloaded' })).toContainText('0');
  await expect(totals.locator('div', { hasText: 'Skipped' })).toContainText('15');
});

test('no WCAG A/AA violations (including colour contrast) with real success cards and thumbnails', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('li.card .thumbs img')).toHaveCount(15);

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();

  expect(results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([]);
});

test('the page keeps the last run after a reload and a screenshot of the finished page is saved', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('li.card')).toHaveCount(4);

  await page.screenshot({ path: 'playwright-report/live-results.png', fullPage: true });
});
