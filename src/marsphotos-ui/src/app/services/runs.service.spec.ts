import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { mixedSummary } from '../testing/fixtures';
import { ApiError, RunsService } from './runs.service';

describe('RunsService', () => {
  let service: RunsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(RunsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts to /api/runs without a body to use the configured dates file', () => {
    let result: unknown;
    service.run().subscribe((s) => (result = s));

    const req = http.expectOne('/api/runs');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(mixedSummary());

    expect((result as { rover: string }).rover).toBe('curiosity');
  });

  it('sends supplied options in the body', () => {
    service.run({ dates: ['02/27/17'], photosPerDate: 3 }).subscribe();

    const req = http.expectOne('/api/runs');
    expect(req.request.body).toEqual({ dates: ['02/27/17'], photosPerDate: 3 });
    req.flush(mixedSummary());
  });

  it.each([
    [400, 'Invalid request', 'BAD_REQUEST', 'At most 100 dates can be supplied.'],
    [409, 'Run in progress', 'RUN_IN_PROGRESS', 'A run is already in progress.'],
    [500, 'The run could not be completed', 'INPUT_FILE_ERROR', "Input file 'dates.txt' was not found."],
  ])('maps a %i problem response to an ApiError', (status, title, code, detail) => {
    let error: ApiError | undefined;
    service.run().subscribe({ error: (e) => (error = e) });

    http.expectOne('/api/runs').flush({ title, status, detail, code }, { status, statusText: title });

    expect(error).toBeInstanceOf(ApiError);
    expect(error?.status).toBe(status);
    expect(error?.code).toBe(code);
    expect(error?.title).toBe(title);
    expect(error?.detail).toBe(detail);
  });

  it('reports an unreachable API in plain language', () => {
    let error: ApiError | undefined;
    service.run().subscribe({ error: (e) => (error = e) });

    http.expectOne('/api/runs').error(new ProgressEvent('error'), { status: 0 });

    expect(error?.code).toBe('NETWORK');
    expect(error?.detail).toContain('Cannot reach the API');
  });

  it('tolerates an error body that is not problem json', () => {
    let error: ApiError | undefined;
    service.run().subscribe({ error: (e) => (error = e) });

    http.expectOne('/api/runs').flush('<html>bad gateway</html>', { status: 502, statusText: 'Bad Gateway' });

    expect(error?.status).toBe(502);
    expect(error?.code).toBe('HTTP_502');
    expect(error?.detail).not.toContain('<html>');
  });

  it('latest() returns the summary, or null when no run exists yet (204)', () => {
    const results: unknown[] = [];
    service.latest().subscribe((s) => results.push(s));
    http.expectOne('/api/runs/latest').flush(mixedSummary());
    service.latest().subscribe((s) => results.push(s));
    http.expectOne('/api/runs/latest').flush(null, { status: 204, statusText: 'No Content' });

    expect(results[0]).toBeTruthy();
    expect(results[1]).toBeNull();
  });

  it('latest() surfaces a 404 as an error instead of hiding a wrong base URL or proxy misroute as "no run yet"', () => {
    let error: ApiError | undefined;
    service.latest().subscribe({ error: (e) => (error = e) });

    http.expectOne('/api/runs/latest').flush('not found', { status: 404, statusText: 'Not Found' });

    expect(error?.status).toBe(404);
  });

  it('cancel() posts to /api/runs/cancel', () => {
    let done = false;
    service.cancel().subscribe(() => (done = true));

    const req = http.expectOne('/api/runs/cancel');
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 202, statusText: 'Accepted' });

    expect(done).toBe(true);
  });
});
