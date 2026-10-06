import { DateResult, RunSummary } from '../models/run-summary';

const photo = (id: string, date: string, status: 'Downloaded' | 'Skipped' = 'Downloaded') => ({
  id,
  status,
  url: `/photos/${date}/${id}.jpg`,
  error: null,
});

export const successResult: DateResult = {
  input: '02/27/17',
  date: '2017-02-27',
  status: 'Success',
  provider: 'RawImageFeed',
  fallbackReason: 'MarsRoverPhotos: HTTP 404 (service not found)',
  note: null,
  photosAvailable: 31,
  downloaded: 2,
  skipped: 1,
  failed: 0,
  photos: [photo('a1', '2017-02-27'), photo('a2', '2017-02-27'), photo('a3', '2017-02-27', 'Skipped')],
  errors: [],
};

export const partialResult: DateResult = {
  input: 'June 2, 2018',
  date: '2018-06-02',
  status: 'PartialFailure',
  provider: 'RawImageFeed',
  fallbackReason: null,
  note: null,
  photosAvailable: 60,
  downloaded: 1,
  skipped: 0,
  failed: 1,
  photos: [
    photo('b1', '2018-06-02'),
    { id: 'b2', status: 'Failed', url: null, error: { code: 'DL_FAILED', message: 'The photo server answered HTTP 500.' } },
  ],
  errors: [{ code: 'DL_FAILED', message: 'b2: The photo server answered HTTP 500.' }],
};

export const invalidResult: DateResult = {
  input: 'April 31, 2018',
  date: null,
  status: 'InvalidDate',
  provider: null,
  fallbackReason: null,
  note: null,
  photosAvailable: 0,
  downloaded: 0,
  skipped: 0,
  failed: 0,
  photos: [],
  errors: [{ code: 'DATE_INVALID', message: 'Day 31 does not exist in April 2018' }],
};

export const noPhotosResult: DateResult = {
  input: 'Jul-13-2016',
  date: '2016-07-13',
  status: 'NoPhotos',
  provider: 'RawImageFeed',
  fallbackReason: null,
  note: 'No photos are available for this date.',
  photosAvailable: 0,
  downloaded: 0,
  skipped: 0,
  failed: 0,
  photos: [],
  errors: [],
};

export const outsideResult: DateResult = {
  input: '01/01/01',
  date: '2001-01-01',
  status: 'OutsideMissionRange',
  provider: null,
  fallbackReason: null,
  note: 'Outside the curiosity mission range (2012-08-06 to 2026-10-06); no lookup made.',
  photosAvailable: 0,
  downloaded: 0,
  skipped: 0,
  failed: 0,
  photos: [],
  errors: [],
};

export function summaryOf(...results: DateResult[]): RunSummary {
  return {
    rover: 'curiosity',
    startedAt: '2026-10-06T12:00:00Z',
    durationMs: 2500,
    totals: {
      lines: results.length,
      valid: results.filter((r) => r.date !== null).length,
      invalid: results.filter((r) => r.status === 'InvalidDate' || r.status === 'UnrecognizedDate').length,
      outsideMissionRange: results.filter((r) => r.status === 'OutsideMissionRange').length,
      downloaded: results.reduce((n, r) => n + r.downloaded, 0),
      skipped: results.reduce((n, r) => n + r.skipped, 0),
      failed: results.reduce((n, r) => n + r.failed, 0),
    },
    results,
  };
}

export const mixedSummary = () =>
  summaryOf(successResult, partialResult, invalidResult, noPhotosResult, outsideResult);
