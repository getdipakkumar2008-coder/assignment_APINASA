import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RunSummary } from '../models/run-summary';
import { invalidResult, mixedSummary, noPhotosResult, outsideResult, partialResult, successResult, summaryOf } from '../testing/fixtures';
import { API_BASE_URL } from '../services/runs.service';
import { Results } from './results';

describe('Results', () => {
  let fixture: ComponentFixture<Results>;
  let root: HTMLElement;

  async function render(summary: RunSummary): Promise<void> {
    fixture = TestBed.createComponent(Results);
    fixture.componentRef.setInput('summary', summary);
    await fixture.whenStable();
    root = fixture.nativeElement as HTMLElement;
  }

  const cards = () => Array.from(root.querySelectorAll<HTMLElement>('li.card'));
  const card = (index: number) => cards()[index];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Results] }).compileComponents();
  });

  it('shows one card per input line, in order, each headed by the original text', async () => {
    await render(mixedSummary());

    expect(cards().length).toBe(5);
    const headings = cards().map((c) => c.querySelector('h3')?.textContent ?? '');
    expect(headings[0]).toContain('02/27/17');
    expect(headings[1]).toContain('June 2, 2018');
    expect(headings[2]).toContain('April 31, 2018');
    expect(headings[3]).toContain('Jul-13-2016');
    expect(headings[4]).toContain('01/01/01');
  });

  it('shows the normalized date, or says there is none', async () => {
    await render(mixedSummary());

    expect(card(0).textContent).toContain('2017-02-27');
    expect(card(2).textContent).toContain('no date');
  });

  it('states the status in words, not only colour, with a decorative symbol hidden from assistive technology', async () => {
    await render(mixedSummary());

    const badge = card(0).querySelector('.badge') as HTMLElement;
    expect(badge.textContent).toContain('Success');
    expect(badge.querySelector('[aria-hidden="true"]')).toBeTruthy();
    expect(card(1).querySelector('.badge')?.textContent).toContain('Partial failure');
    expect(card(2).querySelector('.badge')?.textContent).toContain('Invalid date');
    expect(card(3).querySelector('.badge')?.textContent).toContain('No photos');
    expect(card(4).querySelector('.badge')?.textContent).toContain('Outside mission range');
  });

  it('gives successful and failed dates different style hooks', async () => {
    await render(mixedSummary());

    expect(card(0).classList).toContain('status-Success');
    expect(card(1).classList).toContain('status-PartialFailure');
    expect(card(2).classList).toContain('status-InvalidDate');
  });

  it('shows new, skipped and failed counts', async () => {
    await render(summaryOf(successResult));

    const counts = card(0).querySelector('.counts')?.textContent ?? '';
    expect(counts).toContain('New: 2');
    expect(counts).toContain('Skipped: 1');
    expect(counts).toContain('Failed: 0');
  });

  it('shows the totals for the whole run', async () => {
    await render(mixedSummary());

    const totals = root.querySelector('.totals')?.textContent ?? '';
    expect(totals).toContain('Lines');
    expect(totals).toContain('5');
    expect(totals).toContain('Downloaded');
    expect(root.querySelector('.meta')?.textContent).toContain('curiosity');
    expect(root.querySelector('.meta')?.textContent).toContain('2.5');
  });

  it('lists errors with their code and message', async () => {
    await render(mixedSummary());

    const invalid = card(2).querySelector('.errors')?.textContent ?? '';
    expect(invalid).toContain('DATE_INVALID');
    expect(invalid).toContain('Day 31 does not exist in April 2018');
    expect(card(1).querySelector('.errors')?.textContent).toContain('DL_FAILED');
  });

  it('shows which source served a date and why a fallback was used', async () => {
    await render(summaryOf(successResult));

    expect(card(0).textContent).toContain('RawImageFeed');
    expect(card(0).textContent).toContain('HTTP 404');
  });

  it('shows an empty state for dates without photos instead of a blank area', async () => {
    await render(summaryOf(noPhotosResult));

    expect(card(0).querySelector('img')).toBeNull();
    expect(card(0).querySelector('.empty')?.textContent).toContain('No photos');
  });

  it('explains out-of-range dates through the note', async () => {
    await render(summaryOf(outsideResult));

    expect(card(0).textContent).toContain('mission range');
    expect(card(0).querySelector('.errors')).toBeNull();
  });

  it('renders thumbnails that load lazily and have meaningful alternative text', async () => {
    await render(summaryOf(successResult));

    const images = Array.from(card(0).querySelectorAll('img'));
    expect(images.length).toBe(3);
    for (const img of images) {
      expect(img.getAttribute('loading')).toBe('lazy');
      expect(img.getAttribute('alt')).toMatch(/Mars photo .* 2017-02-27/);
    }
    expect(images[0].getAttribute('src')).toBe('/photos/2017-02-27/a1.jpg');
  });

  it('describes a failed photo in text instead of showing a broken image', async () => {
    await render(summaryOf(partialResult));

    expect(card(0).querySelectorAll('img').length).toBe(1);
    expect(card(0).textContent).toContain('b2');
    expect(card(0).textContent).toContain('HTTP 500');
  });

  it('opens full-size photos through a real link whose accessible name comes from the image alternative text', async () => {
    await render(summaryOf(successResult));

    const link = card(0).querySelector('a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/photos/2017-02-27/a1.jpg');
    expect(link.querySelector('img')?.getAttribute('alt')).toBeTruthy();
    expect(link.getAttribute('rel')).toContain('noopener');
  });

  it('prefixes photo links and thumbnails with the configured API base URL', async () => {
    TestBed.overrideProvider(API_BASE_URL, { useValue: 'https://api.example.test' });
    await render(summaryOf(successResult));

    expect(card(0).querySelector('img')?.getAttribute('src')).toBe('https://api.example.test/photos/2017-02-27/a1.jpg');
    expect(card(0).querySelector('a')?.getAttribute('href')).toBe('https://api.example.test/photos/2017-02-27/a1.jpg');
  });

  it('uses a list of articles so assistive technology can navigate the results', async () => {
    await render(mixedSummary());

    expect(root.querySelector('ul.cards')).toBeTruthy();
    expect(root.querySelectorAll('li.card > article').length).toBe(5);
  });

  it('shows an empty-run message when there are no results', async () => {
    await render(summaryOf());

    expect(root.querySelector('.empty-run')?.textContent).toContain('no dates');
  });

  it('never renders anything that looks like an API key', async () => {
    await render(mixedSummary());

    expect(root.innerHTML).not.toMatch(/api[_-]?key/i);
  });

  it('renders an invalid-date card without any photo section', async () => {
    await render(summaryOf(invalidResult));

    expect(card(0).querySelector('.thumbs')).toBeNull();
    expect(card(0).querySelector('.empty')).toBeNull();
  });
});
