import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, InjectionToken, inject } from '@angular/core';
import { Observable, catchError, map, throwError } from 'rxjs';
import { RunRequest, RunSummary } from '../models/run-summary';

/** Base URL of the API. Empty in development because the dev server proxies /api and /photos. */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', { providedIn: 'root', factory: () => '' });

/** A failed API call in a shape the UI can show. Never contains secrets: the UI never holds any. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly title: string,
    readonly detail: string,
  ) {
    super(`${title}: ${detail}`);
    this.name = 'ApiError';
  }
}

interface ProblemBody {
  title?: string;
  detail?: string;
  code?: string;
}

function toApiError(error: HttpErrorResponse): Observable<never> {
  if (error.status === 0) {
    return throwError(
      () =>
        new ApiError(
          0,
          'NETWORK',
          'The API is not reachable',
          'Cannot reach the API. Start it with "dotnet run --project src/MarsPhotos.Api" and try again.',
        ),
    );
  }

  const body = (typeof error.error === 'object' && error.error !== null ? error.error : {}) as ProblemBody;
  return throwError(
    () =>
      new ApiError(
        error.status,
        body.code ?? `HTTP_${error.status}`,
        body.title ?? 'The request failed',
        body.detail ?? `The server answered HTTP ${error.status}.`,
      ),
  );
}

@Injectable({ providedIn: 'root' })
export class RunsService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  /** Starts a run. Without options the API reads its configured dates file. */
  run(request?: RunRequest): Observable<RunSummary> {
    return this.http.post<RunSummary>(`${this.base}/api/runs`, request ?? null).pipe(catchError(toApiError));
  }

  /** The last completed run, or null when none has finished yet. */
  latest(): Observable<RunSummary | null> {
    return this.http.get<RunSummary | null>(`${this.base}/api/runs/latest`).pipe(
      map((summary) => summary ?? null), // 204 No Content means no run yet
      catchError(toApiError), // anything else, including a 404 from a wrong base URL or proxy, is a real error
    );
  }

  cancel(): Observable<void> {
    return this.http.post(`${this.base}/api/runs/cancel`, null).pipe(
      map(() => undefined),
      catchError(toApiError),
    );
  }
}
