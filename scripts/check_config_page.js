#!/usr/bin/env node
/*
 * Sanity checks for Configuration/configPage.html.
 *
 * The settings page is a single hand-edited HTML file with an inline <script>. Nothing compiles it,
 * so a stray comma or an id renamed in the markup but not the JS ships silently and only shows up
 * as a dead settings page in someone's browser. These two checks caught real breakage while the
 * Schedule tab and the removal of the dead settings were being written.
 *
 *   node scripts/check_config_page.js
 *
 * Exits non-zero on failure so CI fails the build.
 */
const fs = require('fs');
const path = require('path');

const file = process.argv[2]
  || path.join(__dirname, '..', 'Jellyfin.Plugin.Overcoat', 'Configuration', 'configPage.html');

const html = fs.readFileSync(file, 'utf8');
const cssFile = path.join(path.dirname(file), 'configPage.css');
const css = fs.readFileSync(cssFile, 'utf8');
const m = html.match(/<script[^>]*>([\s\S]*)<\/script>/);
if (!m) {
  console.error(`FAIL: no <script> block found in ${file}`);
  process.exit(1);
}

let failures = 0;

// 1. The inline script must parse. `new Function` compiles without executing, so no DOM is needed.
try {
  new Function(m[1]);
  console.log('ok   inline script parses');
} catch (e) {
  console.error(`FAIL inline script has a syntax error: ${e.message}`);
  failures++;
}

// 2. Every element the script reaches for must exist in the markup. Catches an input removed from
//    the HTML while its load()/applyForm() lines linger, which throws at runtime on page open.
const referenced = [...new Set(
  [...m[1].matchAll(/querySelector\(\s*'#([A-Za-z0-9_-]+)'\s*\)/g)].map((x) => x[1]),
)];
const missing = referenced.filter((id) => !new RegExp(`id="${id}"`).test(html));
if (missing.length) {
  console.error(`FAIL script references ${missing.length} element id(s) not present in the markup: ${missing.join(', ')}`);
  failures++;
} else {
  console.log(`ok   all ${referenced.length} referenced element ids exist`);
}

// 3. IDs must stay unique. Duplicate IDs make querySelector silently wire the wrong control.
const ids = [...html.matchAll(/\bid="([A-Za-z0-9_-]+)"/g)].map((x) => x[1]);
const duplicateIds = [...new Set(ids.filter((id, i) => ids.indexOf(id) !== i))];
if (duplicateIds.length) {
  console.error(`FAIL duplicate element id(s): ${duplicateIds.join(', ')}`);
  failures++;
} else {
  console.log(`ok   all ${ids.length} element ids are unique`);
}

// 4. Security/responsive hooks that are easy to lose in a markup cleanup.
const requiredPatterns = [
  ['TMDB API key is masked', /id="TmdbApiKey"[^>]*type="password"|type="password"[^>]*id="TmdbApiKey"/],
  ['API key reveal button exists', /id="ToggleTmdbApiKey"/],
  ['mobile floating preview exists', /id="OvercoatFloatingPreview"/],
  ['banner preview has sticky hook', /data-preview-kind="banner"/],
  ['badge preview has sticky hook', /data-preview-kind="badge"/],
  ['badge side selector is locked to supported placement', /id="BadgeSide"[^>]*disabled/],
  ['badge side selector contains only the supported left option', /id="BadgeSide"[^>]*>\s*<option value="left">Left<\/option>\s*<\/select>/],
  ['external stylesheet is linked', /id="OvercoatStylesheet"[^>]*configPage\.css/],
  ['descriptions toggle exists', /id="OvercoatDescriptions"/],
  ['save dock exposes status feedback', /id="OvercoatSaveState"[^>]*role="status"/],
  ['preview requests carry a stable poster key', /previewKey=' \+ encodeURIComponent\(previewKey\)/],
];
for (const [label, pattern] of requiredPatterns) {
  if (!pattern.test(html)) {
    console.error(`FAIL ${label}`);
    failures++;
  } else {
    console.log(`ok   ${label}`);
  }
}

// The General tab was folded into Maintenance; this guards that badge settings did not come with it.
const maintenanceMarkup = html.slice(html.indexOf('data-panel="maintenance"'));
const badgeMarkup = html.slice(html.indexOf('data-panel="badges"'), html.indexOf('data-panel="apikeys"'));
for (const id of ['BadgesEnabled', 'TrendingTimeWindow', 'WatchHistoryDays', 'WatchHistoryAllUsers', 'ImdbTop250TvListId']) {
  if (maintenanceMarkup.includes(`id="${id}"`) || !badgeMarkup.includes(`id="${id}"`)) {
    console.error(`FAIL badge setting ${id} is not grouped exclusively on the Badges tab`);
    failures++;
  }
}

const cssPatterns = [
  ['form width overrides Jellyfin cap', /#OvercoatConfigPage #OvercoatConfigForm[\s\S]*max-width:\s*1600px/],
  ['plugin overflow is corrected', /overflow:\s*visible\s*!important/],
  ['desktop preview is sticky', /\.ovcPreviewRail\s*\{[^}]*position:\s*sticky/],
  ['studio stacks below 1100px', /@media\s*\(max-width:1099px\)/],
  ['responsive cards use 480px minimum', /minmax\(min\(100%,480px\),1fr\)/],
];
for (const [label, pattern] of cssPatterns) {
  if (!pattern.test(css)) { console.error(`FAIL ${label}`); failures++; }
  else { console.log(`ok   ${label}`); }
}

if (/<style\b/i.test(html)) {
  console.error('FAIL config page contains a legacy inline style block');
  failures++;
} else {
  console.log('ok   no legacy inline style block');
}
if (/<[^>]+\sstyle="/i.test(html)) {
  console.error('FAIL config markup contains inline layout styles');
  failures++;
} else {
  console.log('ok   no inline layout styles');
}

// 5. Schedule choices must be unique so a hand-edited option cannot mask another value.
const minuteSelect = html.match(/<select[^>]*id="ScheduleMinute"[^>]*>([\s\S]*?)<\/select>/);
const minuteValues = minuteSelect ? [...minuteSelect[1].matchAll(/value="([^"]+)"/g)].map((x) => x[1]) : [];
if (!minuteSelect || new Set(minuteValues).size !== minuteValues.length) {
  console.error('FAIL schedule minute options are missing or duplicated');
  failures++;
} else {
  console.log('ok   schedule minute options are unique');
}

// 6. The General tab was removed and its settings folded into Maintenance. A stray tab button or
//    panel would render as an empty tab, so fail rather than ship one.
if (/data-tab="general"/.test(html) || /data-panel="general"/.test(html)) {
  console.error('FAIL the removed General tab is referenced again');
  failures++;
} else {
  console.log('ok   General tab stays removed');
}

if (!/id="TrendingTimeWindow"[\s\S]*?<option value="month">Month<\/option>/.test(html)) {
  console.error('FAIL monthly TMDB trending option is missing'); failures++;
} else { console.log('ok   monthly TMDB trending option exists'); }
if (!/<details class="ovcCard" open>\s*<summary>Status dates/.test(html)
    || !/<details class="ovcCard" open>\s*<summary>Colours &amp; labels/.test(html)) {
  console.error('FAIL status dates and colours/labels must default open'); failures++;
} else { console.log('ok   requested banner accordions default open'); }
const badgeTabAt = html.indexOf('data-tab="badges"');
const librariesTabAt = html.indexOf('data-tab="libraries"');
const apiTabAt = html.indexOf('data-tab="apikeys"');
if (!(badgeTabAt < librariesTabAt && librariesTabAt < apiTabAt)) {
  console.error('FAIL Libraries tab is not directly after Badges and before TMDB API'); failures++;
} else { console.log('ok   Libraries tab follows Badges'); }

process.exit(failures ? 1 : 0);
