import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { App } from './app';
import { mixedSummary, noPhotosResult, summaryOf } from './testing/fixtures';

describe('App', () => {
  let fixture: ComponentFixture<App>;
  let http: HttpTestingController;
  let root: HTMLElement;

  async function start(latest: 'none' | 'summary' = 'none'): Promise<void> {
    fixture = TestBed.createComponent(App);
    http = TestBed.inject(HttpTestingController);
    root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    const req = http.expectOne('/api/runs/latest');
    if (latest === 'summary') {
      req.flush(mixedSummary());
    } else {
      req.flush(null, { status: 204, statusText: 'No Content' });
    }
    await fixture.whenStable();
  }

  const runButton = () => root.querySelector<HTMLButtonElement>('button.run')!;
  const cancelButton = () => root.querySelector<HTMLButtonElement>('button.cancel')!;
  const status = () => root.querySelector('[role="status"]')?.textContent?.trim() ?? '';

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => http.verify());

  it('starts idle with Run enabled, Cancel disabled and a hint instead of results', async () => {
    await start();

    expect(root.querySelector('h1')?.textContent).toContain('Mars Rover Photo Downloader');
    expect(runButton().disabled).toBe(false);
    expect(cancelButton().disabled).toBe(true);
    expect(root.textContent).toContain('No run yet');
    expect(root.querySelector('app-results')).toBeNull();
  });

  it('shows the previous run when the API already has one', async () => {
    await start('summary');

    expect(root.querySelectorAll('li.card').length).toBe(5);
  });

  it('shows a polite live status and busy state while running, then the results', async () => {
    await start();

    runButton().click();
    fixture.detectChanges();

    expect(status()).toContain('Running');
    expect(runButton().disabled).toBe(true);
    expect(cancelButton().disabled).toBe(false);
    expect(root.querySelector('[role="status"]')?.getAttribute('aria-live')).toBe('polite');
    expect(root.querySelector('#results')?.getAttribute('aria-busy')).toBe('true');

    http.expectOne('/api/runs').flush(mixedSummary());
    await fixture.whenStable();

    expect(root.querySelectorAll('li.card').length).toBe(5);
    expect(status()).toContain('finished');
    expect(root.querySelector('#results')?.getAttribute('aria-busy')).toBe('false');
    expect(runButton().disabled).toBe(false);
    expect(cancelButton().disabled).toBe(true);
  });

  it('shows an alert with the problem details and a retry button when the run fails', async () => {
    await start();

    runButton().click();
    http
      .expectOne('/api/runs')
      .flush(
        { title: 'The run could not be completed', detail: "Input file 'dates.txt' was not found.", code: 'INPUT_FILE_ERROR' },
        { status: 500, statusText: 'Server Error' },
      );
    await fixture.whenStable();

    const alert = root.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain("Input file 'dates.txt' was not found.");
    expect(alert.textContent).toContain('INPUT_FILE_ERROR');
    expect(alert.querySelector('button')).toBeTruthy();
    expect(runButton().disabled).toBe(false);
  });

  it('retrying after an error starts a new run and clears the error', async () => {
    await start();
    runButton().click();
    http.expectOne('/api/runs').flush({ title: 'x', detail: 'y', code: 'Z' }, { status: 500, statusText: 'e' });
    await fixture.whenStable();

    root.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    fixture.detectChanges();
    http.expectOne('/api/runs').flush(summaryOf(noPhotosResult));
    await fixture.whenStable();

    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(root.querySelectorAll('li.card').length).toBe(1);
  });

  it('tells the user plainly when the API is not running', async () => {
    await start();

    runButton().click();
    http.expectOne('/api/runs').error(new ProgressEvent('error'), { status: 0 });
    await fixture.whenStable();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Cannot reach the API');
  });

  it('refuses a second run politely when the API says one is already running', async () => {
    await start();

    runButton().click();
    http
      .expectOne('/api/runs')
      .flush({ title: 'Run in progress', detail: 'A run is already in progress.', code: 'RUN_IN_PROGRESS' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('already in progress');
  });

  it('cancel asks the API to cancel and keeps showing the run until it returns', async () => {
    await start();
    runButton().click();
    fixture.detectChanges();

    cancelButton().click();
    http.expectOne('/api/runs/cancel').flush(null, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(status()).toContain('Cancelling');

    const cancelled = summaryOf(noPhotosResult);
    cancelled.results[0] = { ...cancelled.results[0], status: 'Cancelled', note: 'The run was cancelled before this date was processed.' };
    http.expectOne('/api/runs').flush(cancelled);
    await fixture.whenStable();

    expect(root.querySelector('li.card')?.textContent).toContain('Cancelled');
    expect(status()).toContain('cancelled');
  });

  it('has a skip link, a main landmark and labelled sections', async () => {
    await start();

    expect(root.querySelector('a.skip-link')?.getAttribute('href')).toBe('#results');
    expect(root.querySelector('main')).toBeTruthy();
    for (const section of Array.from(root.querySelectorAll('section'))) {
      expect(section.getAttribute('aria-labelledby')).toBeTruthy();
    }
  });
});
