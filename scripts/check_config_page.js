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

process.exit(failures ? 1 : 0);
