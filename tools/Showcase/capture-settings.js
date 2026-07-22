#!/usr/bin/env node
// Standalone capture of the real embedded settings HTML. No server URL, login, or live API exists.
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(__dirname, '../..');
const out = path.resolve(process.argv[2] || path.join(root, 'assets'));

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROMIUM_EXECUTABLE || undefined });
  const page = await browser.newPage({ viewport: { width: Number(process.env.SHOWCASE_VIEWPORT_WIDTH || 1180), height: 900 }, deviceScaleFactor: 1 });
  await page.addInitScript(() => {
    const config = { BadgesEnabled:true, BannerStyle:'glass', BannerShape:'pill', BannerPosition:'top', BannerAlign:'center', BannerFont:'default', BannerFontScale:1, BannerIcons:true, BannerShadow:true, BannerShadowStrength:60, GlassTint:'#0E1018', GlassTintStrength:49, GlassBlur:50, NeonGlow:60, BadgeSide:'left', BadgeVertical:'middle', BadgeScale:100, BadgeGapPercent:1, ScheduleEnabled:true, ScheduleHour:3, ScheduleMinute:0, TrendingTimeWindow:'week', WatchHistoryAllUsers:true, Libraries:[{Name:'TV Shows',Enabled:true,StatusOverlays:true,TrendingBadge:true,WatchHistoryBadge:true,ImdbTop250Badge:false},{Name:'Movies',Enabled:true,StatusOverlays:false,TrendingBadge:true,WatchHistoryBadge:true,ImdbTop250Badge:true}] };
    window.ApiClient = { accessToken:()=> 'mock-token', getUrl:(p)=>'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" width="500" height="750"><rect width="100%" height="100%" fill="%2318202c"/><text x="50%" y="48%" text-anchor="middle" fill="white" font-size="32">SAFE MOCK PREVIEW</text></svg>', getPluginConfiguration:()=>Promise.resolve(config), updatePluginConfiguration:()=>Promise.resolve({}), getVirtualFolders:()=>Promise.resolve([{Name:'TV Shows'},{Name:'Movies'}]), getUsers:()=>Promise.resolve([{Id:'demo',Name:'Demo user'}]), getScheduledTasks:()=>Promise.resolve([]) };
    window.Dashboard = { showLoadingMsg(){}, hideLoadingMsg(){}, processPluginConfigurationUpdateResult(){}, alert(){}, confirm(_m,_t,cb){cb(false);} };
  });
  await page.goto('file://' + path.join(root, 'Jellyfin.Plugin.Overcoat/Configuration/configPage.html'));
  await page.addStyleTag({content:'body{margin:0!important;background:#0d1117!important;color:#e6edf3!important;font-family:Arial,sans-serif}.content-primary{max-width:1080px!important;margin:auto!important;padding:28px!important}button,input,select,textarea{font:inherit;color:inherit;background:#18202c;border:1px solid #364152;border-radius:6px;padding:8px} .paperList,.visualCardBox{background:#161b22}'});
  await page.locator('#OvercoatConfigPage').evaluate(el => el.dispatchEvent(new Event('viewshow')));
  await page.waitForTimeout(350);
  // Keep the capture deterministic even if a future config load rejects before the library mock
  // resolves: the shell still shows clearly fictional, non-server library rows.
  await page.locator('#OvercoatLibraries').evaluate(el => {
    if (!el.querySelector('.ovcLibraryRow')) el.innerHTML = '<div class="ovcCard ovcLibraryRow"><h3>TV Shows <small>(mock)</small></h3><label><input type="checkbox" checked> Process library</label> &nbsp; <label><input type="checkbox" checked> Status banners</label> &nbsp; <label><input type="checkbox" checked> TMDB Trending</label></div><div class="ovcCard ovcLibraryRow"><h3>Movies <small>(mock)</small></h3><label><input type="checkbox" checked> Process library</label> &nbsp; <label><input type="checkbox" checked> Watch history</label> &nbsp; <label><input type="checkbox" checked> IMDb Top 250</label></div>';
  });
  for (const tab of ['banners','badges','libraries']) {
    await page.locator(`button[data-tab="${tab}"]`).click();
    await page.screenshot({ path:path.join(out,`.settings-${tab}.png`), fullPage:true });
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
