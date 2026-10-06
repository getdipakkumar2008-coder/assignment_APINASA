import * as fs from 'fs';
import * as path from 'path';

const live = process.env.E2E_LIVE === '1';
const tmp = path.resolve(__dirname, '.tmp', live ? 'live' : 'deterministic');

/** Writes the dates file the API will read and empties the photos folder (the folder itself is kept: the API serves it). */
export default function globalSetup(): void {
  fs.mkdirSync(path.join(tmp, 'photos'), { recursive: true });
  for (const entry of fs.readdirSync(path.join(tmp, 'photos'))) {
    fs.rmSync(path.join(tmp, 'photos', entry), { recursive: true, force: true });
  }

  const lines = live
    ? ['02/27/17', 'June 2, 2018', 'Jul-13-2016', 'April 31, 2018'] // the sample dates from the brief
    : ['April 31, 2018', 'abc', '01/01/01', '12/31/2099']; // never reaches NASA: invalid, unrecognized, before landing, future
  fs.writeFileSync(path.join(tmp, 'dates.txt'), lines.join('\n') + '\n');
}
