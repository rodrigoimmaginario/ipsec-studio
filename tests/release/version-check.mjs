// Release consistency (CV3.DS4.TS1): one version everywhere, notes for it, and no version number frozen in the UI.
// The UI shows the version it gets from /api/status, so it never goes stale again.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..', '..');
const read = (f) => fs.readFileSync(path.join(root, f), 'utf8');

const csproj = read('app/IpsecStudio/IpsecStudio.csproj').match(/<Version>([^<]+)<\/Version>/)?.[1];
const program = read('app/IpsecStudio/Program.cs').match(/const string Version = "([^"]+)"/)?.[1];
const changelog = read('CHANGELOG.md').match(/^## (\d+\.\d+\.\d+)/m)?.[1];
assert.ok(csproj && program && changelog, 'versions found');
assert.equal(csproj, program, 'IpsecStudio.csproj and Program.cs agree');
assert.equal(changelog, program, 'CHANGELOG.md starts with the current version');

const notesFile = `docs/release-notes/${program}.md`;
assert.ok(fs.existsSync(path.join(root, notesFile)), `release notes ${notesFile}`);
const notes = read(notesFile);
assert.ok(notes.includes(program) && /## Português/.test(notes) && /## English/.test(notes), 'notes in PT and EN');
assert.ok(/SHA-256/.test(notes) && /Get-FileHash/.test(notes), 'notes explain how to check the hash');
assert.ok(/Limites conhecidos/.test(notes) && /Known limitations/.test(notes), 'notes state the known limitations');

const ui = read('app/IpsecStudio/wwwroot/i18n.js') + read('app/IpsecStudio/wwwroot/app.js') + read('app/IpsecStudio/wwwroot/index.html');
assert.ok(!/\b\d+\.\d+(\.\d+)? · /.test(ui) && !/IPsec Studio \d+\.\d+/.test(ui), 'no version number frozen in the UI');

const workflow = fs.readdirSync(path.join(root, '.github/workflows')).map((f) => read(`.github/workflows/${f}`)).join('\n');
assert.ok(/gh release create/.test(workflow) && /contents: write/.test(workflow) && /release-notes/.test(workflow), 'CI creates the GitHub release from the tag with the notes');

console.log(`PASS release ${program}: csproj, Program.cs, CHANGELOG and notes agree; PT/EN notes with hash check and limits; no frozen version in the UI; CI publishes the release`);
