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
  ['poster composite preview has sticky hook', /data-preview-kind="poster"/],
  ['poster preview image exists', /id="PostersPreview"/],
  ['badge side selector is locked to supported placement', /id="BadgeSide"[^>]*disabled/],
  ['badge side selector contains only the supported left option', /id="BadgeSide"[^>]*>\s*<option value="left">Left<\/option>\s*<\/select>/],
  ['wide badge side selector is locked to supported placement', /id="WideBadgeSide"[^>]*disabled/],
  ['wide-card customize toggle exists', /id="WideCardCustomize"/],
  ['wide-card composite preview has sticky hook', /data-preview-kind="wide"/],
  ['wide-card preview image exists', /id="WidePreview"/],
  ['external stylesheet is linked', /id="OvercoatStylesheet"[^>]*configPage\.css/],
  ['purpose-based navigation exists', /data-tab="design"[\s\S]*data-tab="libraries"[\s\S]*data-tab="sources"[\s\S]*data-tab="automation"[\s\S]*data-tab="recovery"/],
  ['design surface switch exists', /data-surface="design"[\s\S]*data-surface="wide"/],
  ['explicit discard action exists', /id="OvercoatDiscard"/],
  ['Dry Run notice exists', /id="OvercoatDryRunBanner"[^>]*role="status"/],
  ['skip cache shows its recommended state', /id="CacheEnabled"[\s\S]*class="ovcRecommended">Recommended on/],
  ['scan reapply shows its recommended state', /id="ReapplyAfterScan"[\s\S]*class="ovcRecommended">Recommended on/],
  ['library artwork choices use warning actions', /id="OvercoatUseWideCardsAll"[^>]*ovcWarningAction[\s\S]*id="OvercoatUseEpisodeStillsAll"[^>]*ovcWarningAction/],
  ['library artwork choices identify all-user scope', /id="OvercoatUseWideCardsAll"[\s\S]*Use Overcoat wide cards for all users[\s\S]*id="OvercoatUseEpisodeStillsAll"[\s\S]*Use episode stills for all users/],
  ['ignored titles are an always-open section', /class="ovcCard ovcWide" id="OvercoatIgnoreCard"[\s\S]*<h3>Titles to ignore<\/h3>/],
  ['targeted runs use Jellyfin item search', /id="OvercoatTitleSearch"[\s\S]*id="LimitToItemIds"/],
  ['ignored titles use Jellyfin item search', /id="OvercoatIgnoreSearch"[\s\S]*id="IgnoreItemIds"/],
  ['stable item ids are serialized', /config\.LimitToItemIds\s*=\s*selectedTargetItems[\s\S]*config\.IgnoreItemIds\s*=\s*selectedIgnoredItems/],
  ['force restore uses a connected safety panel', /class="checkboxContainer ovcForceRestore"[\s\S]*id="ForceRestore"[\s\S]*Force restore over changed artwork/],
  ['redundant Automation run heading is removed', /if \(runHeading\) \{ runHeading\.remove\(\); \}/],
  ['session draft persistence exists', /sessionStorage\.setItem\(draftKey/],
  ['full form serialization exists', /config\.Libraries\s*=\s*collectLibraries\(\)/],
  ['segmented controls expose radio state', /setAttribute\('aria-checked'/],
  ['save dock exposes status feedback', /id="OvercoatSaveState"[^>]*role="status"/],
  ['status editor exposes clear banner-text fields', /class="ovcStatusHeader"[^>]*>[\s\S]*Banner text[\s\S]*Show[\s\S]*class="ovcStatusText"/],
  ['status visibility switches use one native control shape', /id="ShowNew"[\s\S]*id="ShowAiring"[\s\S]*id="ShowReturning"[\s\S]*id="ShowEnded"[\s\S]*id="ShowCanceled"/],
  ['poster effects explain their controls', /id="BannerIcons"[\s\S]*Draw the ★[\s\S]*id="BannerShadow"[\s\S]*Add a soft shadow/],
  ['preview requests carry a stable poster key', /previewKey=' \+ encodeURIComponent\(previewKey\)/],
  ['all-user wide-card action exists', /id="OvercoatUseWideCardsAll"/],
  ['all-user episode-still action exists', /id="OvercoatUseEpisodeStillsAll"/],
  ['all-user preference update preserves the DTO', /entry\.prefs\.CustomPrefs\[episodeImagesPreferenceKey\][\s\S]*updateDisplayPreferences\([\s\S]*entry\.prefs/],
];
for (const [label, pattern] of requiredPatterns) {
  if (!pattern.test(html)) {
    console.error(`FAIL ${label}`);
    failures++;
  } else {
    console.log(`ok   ${label}`);
  }
}

// Badge-source controls are moved into the purpose-based Data Sources area at initialization. Guard
// both their stable grouping hooks and the relocation map so a markup cleanup cannot strand them.
for (const id of ['BadgesEnabled', 'TrendingTimeWindow', 'WatchHistoryDays', 'WatchHistoryAllUsers', 'ImdbTop250TvListId']) {
  if (!html.includes(`id="${id}"`) || !/OvercoatBadgeSourcesCard|OvercoatWatchHistoryCard|OvercoatImdbListsCard/.test(html)) {
    console.error(`FAIL data-source setting ${id} has no purpose-area grouping hook`);
    failures++;
  }
}
if (!/OvercoatBadgeSourcesCard'[\s\S]*OvercoatDataSourcesBody/.test(html)
    || !/OvercoatOverridesCard'[\s\S]*OvercoatDataSourcesBody/.test(html)) {
  console.error('FAIL source cards are not relocated into Data Sources'); failures++;
} else { console.log('ok   source cards relocate into Data Sources'); }

const cssPatterns = [
  ['form width overrides Jellyfin cap', /#OvercoatConfigPage #OvercoatConfigForm[\s\S]*max-width:\s*none/],
  ['save dock is revealable', /\.ovcSaveDock\.ovcVisible/],
  ['plugin overflow is corrected', /overflow:\s*visible\s*!important/],
  ['desktop preview is sticky', /\.ovcBannerPreview\s*\{[^}]*position:\s*sticky/],
  ['preview controls stay above artwork without nested scrolling', /\.ovcBannerPreview img\s*\{[^}]*order:\s*2[\s\S]*\.ovcBannerPreview \.ovcStatusSwitch\s*\{\s*order:\s*1/],
  ['navigation stays in a top rail', /\.ovcTabs\s*\{[\s\S]*position:\s*sticky;[\s\S]*flex-direction:\s*row/],
  ['operation buttons stay rounded', /#OvercoatRunNow,[\s\S]*#OvercoatRestore,[\s\S]*#OvercoatVaultRefresh\s*\{[^}]*border-radius:\s*999px\s*!important/],
  ['library artwork buttons stay rounded', /#OvercoatUseWideCardsAll,[\s\S]*#OvercoatUseEpisodeStillsAll\s*\{[^}]*border-radius:\s*999px\s*!important/],
  ['library artwork buttons use warning colour', /\.ovcWarningAction\s*\{[^}]*background:\s*var\(--ov-warning\)\s*!important/],
  ['force restore control keeps switch beside copy', /\.ovcForceRestore\s*\{[^}]*grid-template-columns:\s*42px\s+minmax\(0,\s*1fr\)/],
  ['catalogue search buttons stay compact', /\.ovcSearchRow button\s*\{[^}]*width:\s*auto\s*!important[^}]*max-width:\s*7rem/],
  ['catalogue search text stays left aligned', /#OvercoatTitleSearch,[\s\S]*#OvercoatIgnoreSearch\s*\{[^}]*text-align:\s*left\s*!important[^}]*text-indent:\s*0\s*!important/],
  ['selected-title remove button has fixed geometry', /\.ovcRemoveTitle\s*\{[^}]*width:\s*1\.45rem\s*!important[^}]*height:\s*1\.45rem\s*!important[^}]*overflow:\s*hidden/],
  ['custom schedule fields respect their hidden state', /#ScheduleTimeRow\[hidden\]\s*\{[^}]*display:\s*none\s*!important/],
  ['Quick Look buttons resist Jellyfin full-width button styles', /\.ovcPreset\s*\{[^}]*width:\s*auto\s*!important[^}]*max-width:\s*8rem/],
  ['studio stacks below 1100px', /@media\s*\(max-width:\s*1099px\)/],
  ['responsive cards use a bounded minimum', /minmax\(min\(100%,\s*440px\)\s*,\s*1fr\)/],
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
const returningFormat = html.match(/<select[^>]*id="ReturningDateFormat"[^>]*>([\s\S]*?)<\/select>/);
if (!returningFormat || /value="day"/.test(returningFormat[1])
    || !/value="date"/.test(returningFormat[1]) || !/value="countdown"/.test(returningFormat[1])) {
  console.error('FAIL Returning format must offer only Date and Countdown'); failures++;
} else { console.log('ok   Returning format excludes day of week'); }
if (!/<details class="ovcCard" open>\s*<summary>Status dates/.test(html)
    || !/<details class="ovcCard" open>\s*<summary>Colours &amp; labels/.test(html)) {
  console.error('FAIL status dates and colours/labels must default open'); failures++;
} else { console.log('ok   requested banner accordions default open'); }
const designTabAt = html.indexOf('data-tab="design"');
const librariesTabAt = html.indexOf('data-tab="libraries"');
const sourcesTabAt = html.indexOf('data-tab="sources"');
const automationTabAt = html.indexOf('data-tab="automation"');
const recoveryTabAt = html.indexOf('data-tab="recovery"');
if (!(designTabAt !== -1 && designTabAt < librariesTabAt && librariesTabAt < sourcesTabAt
    && sourcesTabAt < automationTabAt && automationTabAt < recoveryTabAt)) {
  console.error('FAIL tab order must be Design, Libraries, Data Sources, Automation, Recovery'); failures++;
} else { console.log('ok   purpose navigation order is correct'); }

process.exit(failures ? 1 : 0);
