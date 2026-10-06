import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RunSummary } from './models/run-summary';
import { Results } from './results/results';
import { ApiError, RunsService } from './services/runs.service';

type RunState = 'idle' | 'running' | 'done' | 'cancelled' | 'error';

@Component({
  selector: 'app-root',
  imports: [Results],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  private readonly runs = inject(RunsService);

  protected readonly state = signal<RunState>('idle');
  protected readonly cancelling = signal(false);
  protected readonly summary = signal<RunSummary | null>(null);
  protected readonly error = signal<ApiError | null>(null);

  protected readonly running = computed(() => this.state() === 'running');

  /** Plain-language status announced politely to screen readers. */
  protected readonly statusMessage = computed(() => {
    switch (this.state()) {
      case 'running':
        return this.cancelling() ? 'Cancelling the run…' : 'Running… this usually takes a few seconds.';
      case 'done': {
        const totals = this.summary()?.totals;
        return totals
          ? `Run finished: ${totals.lines} lines, ${totals.downloaded} photos downloaded, ${totals.skipped} skipped, ${totals.failed} failed.`
          : 'Run finished.';
      }
      case 'cancelled':
        return 'Run cancelled. Showing the results so far.';
      case 'error':
        return 'The run failed. See the message below.';
      default:
        return this.summary() ? 'Showing the last run.' : 'Ready.';
    }
  });

  ngOnInit(): void {
    this.runs.latest().subscribe({
      next: (latest) => {
        if (latest && this.state() === 'idle' && !this.summary()) {
          this.summary.set(latest);
        }
      },
      // A broken API at load time (down, wrong base URL, proxy misroute) should be visible, not silent.
      error: (err: ApiError) => this.error.set(err),
    });
  }

  protected run(): void {
    if (this.running()) {
      return;
    }

    this.error.set(null);
    this.cancelling.set(false);
    this.state.set('running');
    this.runs.run().subscribe({
      next: (summary) => {
        this.summary.set(summary);
        this.state.set(summary.results.some((r) => r.status === 'Cancelled') ? 'cancelled' : 'done');
        this.cancelling.set(false);
      },
      error: (err: ApiError) => {
        this.error.set(err);
        this.state.set('error');
        this.cancelling.set(false);
      },
    });
  }

  protected cancel(): void {
    if (!this.running()) {
      return;
    }

    this.cancelling.set(true);
    this.runs.cancel().subscribe({
      error: (err: ApiError) => {
        this.cancelling.set(false);
        this.error.set(err);
      },
    });
  }
}
