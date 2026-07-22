#!/usr/bin/env node
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const expected = {
  'showcase-hero.png':[1600,760], 'showcase-statuses.png':[1600,690],
  'showcase-styles.png':[1600,690], 'showcase-controls.png':[1600,1080],
  'showcase-badges.png':[1600,760],
  'settings-ui-v072-banners.png':[1180,900], 'settings-ui-v072-badges.png':[1180,900],
  'settings-ui-v072-libraries.png':[1180,900], 'settings-ui-v072-maintenance.png':[1180,900],
};
let failed = false;
for (const [name, dims] of Object.entries(expected)) {
  const file = path.join(root, 'assets', name);
  if (!fs.existsSync(file)) { console.error(`FAIL missing ${name}`); failed=true; continue; }
  const b=fs.readFileSync(file); const got=[b.readUInt32BE(16),b.readUInt32BE(20)];
  if (got[0]!==dims[0]||got[1]!==dims[1]) { console.error(`FAIL ${name}: ${got} != ${dims}`); failed=true; }
  if (b.length>1.2*1024*1024) { console.error(`FAIL ${name}: ${(b.length/1048576).toFixed(2)} MiB exceeds 1.2 MiB`); failed=true; }
  else console.log(`ok   ${name} ${got.join('x')} ${(b.length/1024).toFixed(0)} KiB`);
}
const generator=fs.readFileSync(path.join(root,'tools/Showcase/Program.cs'),'utf8');
if (/BadgeLayout\(\s*true/.test(generator)) { console.error('FAIL right-side badge layout found'); failed=true; }
else console.log('ok   showcase generator contains no right-side badge layout');
process.exit(failed?1:0);
