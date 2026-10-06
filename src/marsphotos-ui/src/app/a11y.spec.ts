import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import axe from 'axe-core';
import { App } from './app';
import { mixedSummary, noPhotosResult, summaryOf } from './testing/fixtures';

/**
 * Automated WCAG 2.1 A/AA checks with axe-core on every visible state (FR-041, NFR-11).
 * jsdom has no layout engine, so colour contrast cannot be computed here; contrast is verified manually (verification.md)
 * and the palette in the component styles was chosen for at least 4.5:1.
 */
describe('Accessibility (axe-core)', () => {
  let fixture: ComponentFixture<App>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(App);
    http = TestBed.inject(HttpTestingController);
    document.documentElement.lang = 'en';
    document.title = 'Mars Rover Photo Downloader';
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.nativeElement.remove();
  });

  async function violations(): Promise<string[]> {
    const result = await axe.run(fixture.nativeElement as HTMLElement, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'] },
      rules: { 'color-contrast': { enabled: false }, region: { enabled: false } },
    });
    return result.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`);
  }

  function noLatest(): void {
    http.expectOne('/api/runs/latest').flush({ code: 'NO_RUN_YET' }, { status: 404, statusText: 'Not Found' });
  }

  it('has no violations in the initial empty state', async () => {
    noLatest();
    await fixture.whenStable();

    expect(await violations()).toEqual([]);
  });

  it('has no violations while a run is in progress', async () => {
    noLatest();
    fixture.nativeElement.querySelector('button.run').click();
    fixture.detectChanges();

    expect(await violations()).toEqual([]);
    http.expectOne('/api/runs').flush(summaryOf());
  });

  it('has no violations for a mixed result set with thumbnails, errors and empty states', async () => {
    noLatest();
    fixture.nativeElement.querySelector('button.run').click();
    http.expectOne('/api/runs').flush(mixedSummary());
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelectorAll('li.card').length).toBe(5);
    expect(await violations()).toEqual([]);
  });

  it('has no violations for an empty run and for a no-photos result', async () => {
    noLatest();
    fixture.nativeElement.querySelector('button.run').click();
    http.expectOne('/api/runs').flush(summaryOf(noPhotosResult));
    await fixture.whenStable();

    expect(await violations()).toEqual([]);
  });

  it('has no violations in the error state', async () => {
    noLatest();
    fixture.nativeElement.querySelector('button.run').click();
    http
      .expectOne('/api/runs')
      .flush({ title: 'The run could not be completed', detail: 'Input file was not found.', code: 'INPUT_FILE_ERROR' }, { status: 500, statusText: 'x' });
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeTruthy();
    expect(await violations()).toEqual([]);
  });

  it('lets keyboard users reach every control: all buttons and links are natively focusable', async () => {
    noLatest();
    fixture.nativeElement.querySelector('button.run').click();
    http.expectOne('/api/runs').flush(mixedSummary());
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    const interactive = Array.from(root.querySelectorAll<HTMLElement>('button, a[href]'));
    expect(interactive.length).toBeGreaterThan(5);
    for (const element of interactive) {
      expect(element.tabIndex).toBeGreaterThanOrEqual(0);
    }
    expect(root.querySelectorAll('[tabindex]:not([tabindex="0"]):not([tabindex="-1"])').length).toBe(0);
  });
});
