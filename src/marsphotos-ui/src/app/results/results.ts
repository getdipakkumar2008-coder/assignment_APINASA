import { Component, inject, input } from '@angular/core';
import { DateResult, DateResultStatus, PhotoOutcome, RunSummary } from '../models/run-summary';
import { API_BASE_URL } from '../services/runs.service';

interface StatusView {
  label: string;
  /** Decorative symbol; the label is what assistive technology reads. */
  symbol: string;
}

const STATUS: Record<DateResultStatus, StatusView> = {
  Success: { label: 'Success', symbol: '✓' },
  NoPhotos: { label: 'No photos', symbol: '○' },
  InvalidDate: { label: 'Invalid date', symbol: '✗' },
  UnrecognizedDate: { label: 'Unrecognized date', symbol: '?' },
  OutsideMissionRange: { label: 'Outside mission range', symbol: '⌀' },
  DuplicateDate: { label: 'Duplicate date', symbol: '⧉' },
  SourceUnavailable: { label: 'Source unavailable', symbol: '!' },
  PartialFailure: { label: 'Partial failure', symbol: '◐' },
  Failed: { label: 'Failed', symbol: '✗' },
  Cancelled: { label: 'Cancelled', symbol: '■' },
};

@Component({
  selector: 'app-results',
  templateUrl: './results.html',
  styleUrl: './results.css',
})
export class Results {
  readonly summary = input.required<RunSummary>();

  private readonly base = inject(API_BASE_URL);

  /** Photo links are served by the API, so they follow the same base URL as the API calls (empty behind the dev proxy). */
  protected photoUrl(photo: PhotoOutcome): string | null {
    return photo.url === null ? null : `${this.base}${photo.url}`;
  }

  protected label(status: DateResultStatus): string {
    return STATUS[status].label;
  }

  protected symbol(status: DateResultStatus): string {
    return STATUS[status].symbol;
  }

  protected seconds(): string {
    return (this.summary().durationMs / 1000).toFixed(1);
  }

  /** Alternative text for a thumbnail: what it is and which date it belongs to. */
  protected alt(result: DateResult, photo: PhotoOutcome): string {
    return `Mars photo ${photo.id} for ${result.date ?? result.input}`;
  }

  /** Only dates that were actually looked up show a photo area. */
  protected hasPhotoArea(result: DateResult): boolean {
    return result.status === 'NoPhotos' || result.photos.length > 0;
  }
}
