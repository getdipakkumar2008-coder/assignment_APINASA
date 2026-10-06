/** Wire shapes returned by the API (see specs/001-mars-photo-downloader/contracts/openapi.yaml). */

export type DateResultStatus =
  | 'Success'
  | 'NoPhotos'
  | 'InvalidDate'
  | 'UnrecognizedDate'
  | 'OutsideMissionRange'
  | 'DuplicateDate'
  | 'SourceUnavailable'
  | 'PartialFailure'
  | 'Failed'
  | 'Cancelled';

export type PhotoStatus = 'Downloaded' | 'Skipped' | 'Failed';

export interface ErrorInfo {
  code: string;
  message: string;
}

export interface PhotoOutcome {
  id: string;
  status: PhotoStatus;
  /** Relative path such as /photos/2017-02-27/file.jpg; null when the photo failed. */
  url: string | null;
  error: ErrorInfo | null;
}

export interface DateResult {
  input: string;
  date: string | null;
  status: DateResultStatus;
  provider: string | null;
  fallbackReason: string | null;
  note: string | null;
  photosAvailable: number;
  downloaded: number;
  skipped: number;
  failed: number;
  photos: PhotoOutcome[];
  errors: ErrorInfo[];
}

export interface RunTotals {
  lines: number;
  valid: number;
  invalid: number;
  outsideMissionRange: number;
  downloaded: number;
  skipped: number;
  failed: number;
}

export interface RunSummary {
  rover: string;
  startedAt: string;
  durationMs: number;
  totals: RunTotals;
  results: DateResult[];
}

export interface RunRequest {
  dates?: string[];
  rover?: string;
  photosPerDate?: number;
}
