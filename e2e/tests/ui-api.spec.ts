import AxeBuilder from '@axe-core/playwright';
import { expect, Page, test } from '@playwright/test';

/**
 * Deterministic end-to-end tests: real Chrome -> Angular dev server -> proxy -> real API process -> real file reading.
 * The dates file contains only dates that never reach NASA, so no network is needed.
 */

const API = 'http://localhost:5180';

/** Collects anything that would show up as a red line in the browser console or a failed request. */
function watchForProblems(page: Page): { consoleErrors: string[]; badResponses: string[] } {
  const consoleErrors: string[] = [];
  const badResponses: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', (error) => consoleErrors.push(error.message));
  page.on('response', (response) => {
    if (response.status() >= 400) badResponses.push(`${response.status()} ${response.url()}`);
  });
  return { consoleErrors, badResponses };
}

test.describe('page load', () => {
  test('shows the idle page and the browser console stays clean (no red 404 for "no run yet")', async ({ page }) => {
    const problems = watchForProblems(page);

    await page.goto('/');

    await expect(page.getByRole('heading', { level: 1, name: 'Mars Rover Photo Downloader' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Run', exact: true })).toBeEnabled();
    await expect(page.getByRole('button', { name: 'Cancel run' })).toBeDisabled();
    await expect(page.getByText('No run yet')).toBeVisible();
    await expect(page.getByRole('status')).toHaveText('Ready.');
    expect(problems.badResponses).toEqual([]);
    expect(problems.consoleErrors).toEqual([]);
  });

  test('tells the user when the API cannot be reached instead of failing silently', async ({ page }) => {
    await page.route('**/api/runs/latest', (route) => route.abort('connectionrefused'));

    await page.goto('/');

    await expect(page.getByRole('alert')).toContainText('Cannot reach the API');
  });
});

test.describe('run from the UI through the API', () => {
  test('Run reads the dates file on the server and shows each line with its reason', async ({ page }) => {
    const problems = watchForProblems(page);
    await page.goto('/');

    await page.getByRole('button', { name: 'Run', exact: true }).click();

    await expect(page.getByRole('status')).toContainText('Run finished');
    const cards = page.locator('li.card');
    await expect(cards).toHaveCount(4);

    const invalid = cards.filter({ hasText: 'April 31, 2018' });
    await expect(invalid).toContainText('Invalid date');
    await expect(invalid).toContainText('DATE_INVALID');
    await expect(invalid).toContainText('Day 31 does not exist in April 2018');

    await expect(cards.filter({ hasText: 'abc' })).toContainText('Unrecognized date');
    await expect(cards.filter({ hasText: '01/01/01' })).toContainText('Outside mission range');
    await expect(cards.filter({ hasText: '12/31/2099' })).toContainText('Outside mission range');

    const totals = page.locator('dl.totals');
    await expect(totals.locator('div', { hasText: 'Lines' })).toContainText('4');
    await expect(totals.locator('div', { hasText: 'Invalid' })).toContainText('2');
    await expect(totals.locator('div', { hasText: 'Outside range' })).toContainText('2');
    await expect(totals.locator('div', { hasText: 'Downloaded' })).toContainText('0');
    await expect(page.locator('li.card img')).toHaveCount(0);
    expect(problems.consoleErrors).toEqual([]);
  });

  test('the result survives a page reload because the API keeps the latest run', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: 'Run', exact: true }).click();
    await expect(page.locator('li.card')).toHaveCount(4);

    await page.reload();

    await expect(page.locator('li.card')).toHaveCount(4);
    await expect(page.getByRole('status')).toHaveText('Showing the last run.');
  });

  test('a second run through the UI is accepted and produces the same summary', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: 'Run', exact: true }).click();
    await expect(page.locator('li.card')).toHaveCount(4);

    await page.getByRole('button', { name: 'Run', exact: true }).click();

    await expect(page.getByRole('status')).toContainText('Run finished');
    await expect(page.locator('li.card')).toHaveCount(4);
  });
});

test.describe('keyboard and accessibility in a real browser', () => {
  test('a keyboard-only user can reach Run, start it and see a visible focus indicator', async ({ page }) => {
    await page.goto('/');

    await page.keyboard.press('Tab'); // skip link
    await expect(page.getByRole('link', { name: 'Skip to results' })).toBeFocused();
    await page.keyboard.press('Tab'); // Run
    const run = page.getByRole('button', { name: 'Run', exact: true });
    await expect(run).toBeFocused();

    const outline = await run.evaluate((el) => {
      const style = getComputedStyle(el);
      return { width: parseFloat(style.outlineWidth), style: style.outlineStyle };
    });
    expect(outline.style).not.toBe('none');
    expect(outline.width).toBeGreaterThanOrEqual(2);

    await page.keyboard.press('Enter');
    await expect(page.locator('li.card')).toHaveCount(4);
  });

  test('the skip link moves focus to the results region', async ({ page }) => {
    await page.goto('/');

    await page.keyboard.press('Tab');
    await page.keyboard.press('Enter');

    await expect(page).toHaveURL(/#results$/);
  });

  for (const state of ['on load', 'after a run']) {
    test(`no WCAG A/AA violations in a real browser (${state}), including colour contrast`, async ({ page }) => {
      await page.goto('/');
      if (state === 'after a run') {
        await page.getByRole('button', { name: 'Run', exact: true }).click();
        await expect(page.locator('li.card')).toHaveCount(4);
      }

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
        .analyze();

      expect(results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([]);
    });
  }

  test('the page is usable at a phone width without horizontal scrolling', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 700 });
    await page.goto('/');
    await page.getByRole('button', { name: 'Run', exact: true }).click();
    await expect(page.locator('li.card')).toHaveCount(4);

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

    expect(overflow).toBeLessThanOrEqual(0);
  });
});

test.describe('API surface reached from a real browser', () => {
  test('Swagger UI loads and lists the operations of the contract', async ({ page }) => {
    await page.goto(`${API}/swagger`);

    await expect(page.getByRole('heading', { name: /Mars Rover Photo Downloader API/ })).toBeVisible();
    for (const path of ['/api/runs', '/api/runs/cancel', '/api/runs/latest', '/health']) {
      await expect(page.locator('.opblock-summary-path', { hasText: new RegExp(`^${path}$`) })).toBeVisible();
    }
  });

  test('a page from another site cannot start or cancel a run (cross-origin POST is rejected)', async ({ request }) => {
    const start = await request.post(`${API}/api/runs`, { headers: { Origin: 'http://evil.example' } });
    const cancel = await request.post(`${API}/api/runs/cancel`, { headers: { Origin: 'http://evil.example' } });

    expect(start.status()).toBe(403);
    expect(cancel.status()).toBe(403);
    expect((await start.json()).code).toBe('FORBIDDEN_ORIGIN');
  });

  test('API answers are never cacheable and never contain the key', async ({ request }) => {
    const latest = await request.get(`${API}/api/runs/latest`);
    const text = await latest.text();

    expect(latest.headers()['cache-control']).toBe('no-store');
    expect(text).not.toContain('e2e-test-key-0000');
  });
});
