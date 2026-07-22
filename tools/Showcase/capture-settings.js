#!/usr/bin/env node
// Standalone capture of the real embedded settings HTML. No server URL, login, or live API exists.
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(__dirname, '../..');
const out = path.resolve(process.argv[2] || path.join(root, 'assets'));

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROMIUM_EXECUTABLE || undefined });
  const page = await browser.newPage({ viewport: { width: Number(process.env.SHOWCASE_VIEWPORT_WIDTH || 1180), height: 900 }, deviceScaleFactor: 1 });
  await page.addInitScript((previewUrl) => {
    const config = { BadgesEnabled:true, BannerStyle:'glass', BannerShape:'pill', BannerPosition:'top', BannerAlign:'center', BannerFont:'default', BannerFontScale:1, BannerIcons:true, BannerShadow:true, BannerShadowStrength:60, GlassTint:'#0E1018', GlassTintStrength:49, GlassBlur:50, NeonGlow:60, BadgeSide:'left', BadgeVertical:'middle', BadgeScale:100, BadgeGapPercent:1, ScheduleEnabled:true, ScheduleHour:3, ScheduleMinute:0, TrendingTimeWindow:'week', WatchHistoryAllUsers:true, Libraries:[{Name:'TV Shows',Enabled:true,StatusOverlays:true,TrendingBadge:true,WatchHistoryBadge:true,ImdbTop250Badge:false},{Name:'Movies',Enabled:true,StatusOverlays:false,TrendingBadge:true,WatchHistoryBadge:true,ImdbTop250Badge:true}] };
    window.ApiClient = { accessToken:()=> 'synthetic-capture-token', getUrl:()=>previewUrl, getPluginConfiguration:()=>Promise.resolve(config), updatePluginConfiguration:()=>Promise.resolve({}), getVirtualFolders:()=>Promise.resolve([{Name:'TV Shows'},{Name:'Movies'}]), getUsers:()=>Promise.resolve([{Id:'demo',Name:'Demo user'}]), getScheduledTasks:()=>Promise.resolve([]) };
    window.Dashboard = { showLoadingMsg(){}, hideLoadingMsg(){}, processPluginConfigurationUpdateResult(){}, alert(){}, confirm(_m,_t,cb){cb(false);} };
  }, 'file://' + path.join(root, 'private/showcase-input/breaking-bad.jpg'));
  await page.goto('file://' + path.join(root, 'Jellyfin.Plugin.Overcoat/Configuration/configPage.html'));
  await page.addStyleTag({content:'body{margin:0!important;background:#0d1117!important;color:#e6edf3!important;font-family:Arial,sans-serif}.content-primary{padding:28px 0!important}button,input,select,textarea{font:inherit;color:inherit;background:#18202c;border:1px solid #364152;border-radius:6px;padding:8px} .paperList,.visualCardBox{background:#161b22}'});
  await page.locator('#OvercoatConfigPage').evaluate(el => el.dispatchEvent(new Event('viewshow')));
  await page.waitForTimeout(350);
  // Keep the capture deterministic even if a future config load rejects before the library mock
  // resolves: the shell still shows clearly fictional, non-server library rows.
  await page.locator('#OvercoatLibraries').evaluate(el => {
    if (!el.querySelector('.ovcLibraryRow')) el.innerHTML = '<div class="ovcCard ovcLibraryRow"><h3>TV Shows <small>(mock)</small></h3><label><input type="checkbox" checked> Process library</label> &nbsp; <label><input type="checkbox" checked> Status banners</label><br><label><input type="checkbox" checked> Watch history</label> &nbsp; <label><input type="checkbox" checked> TMDB Trending</label> &nbsp; <label><input type="checkbox"> IMDb Top 250</label></div><div class="ovcCard ovcLibraryRow"><h3>Movies <small>(mock)</small></h3><label><input type="checkbox" checked> Process library</label><br><label><input type="checkbox"> Watch history</label> &nbsp; <label><input type="checkbox" checked> TMDB Trending</label> &nbsp; <label><input type="checkbox" checked> IMDb Top 250</label></div>';
  });
  const captureTabs = process.env.SHOWCASE_CAPTURE_ALL === '1'
    ? ['general','banners','badges','apikeys','libraries','maintenance']
    : ['banners','badges','libraries'];
  for (const tab of captureTabs) {
    await page.locator(`button[data-tab="${tab}"]`).click();
    await page.waitForTimeout(250);
    await page.screenshot({ path:path.join(out,`settings-${tab}.png`), fullPage:true });
  }
  if (process.env.SHOWCASE_VERIFY_SCROLL === '1') {
    for (const tab of ['general','banners','badges','apikeys','libraries','maintenance']) {
      await page.locator(`button[data-tab="${tab}"]`).click();
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      if (overflow > 1) throw new Error(`${tab} has ${overflow}px of horizontal overflow.`);
    }
    if (await page.locator('button[data-tab="maintenance"]').getAttribute('aria-selected') !== 'true') {
      throw new Error('Tab ARIA selection state did not update.');
    }
    await page.locator('button[data-tab="general"]').focus();
    await page.keyboard.press('ArrowRight');
    if (await page.locator('button[data-tab="banners"]').getAttribute('aria-selected') !== 'true') {
      throw new Error('Keyboard tab navigation failed.');
    }
    await page.locator('button[data-tab="apikeys"]').click();
    if (await page.locator('#TmdbApiKey').getAttribute('type') !== 'password') throw new Error('TMDB key is not masked by default.');
    await page.locator('#ToggleTmdbApiKey').click();
    if (await page.locator('#TmdbApiKey').getAttribute('type') !== 'text') throw new Error('TMDB key reveal control failed.');
    await page.locator('#ToggleTmdbApiKey').click();
    if (await page.locator('#TmdbApiKey').getAttribute('type') !== 'password') throw new Error('TMDB key hide control failed.');
    await page.locator('button[data-tab="banners"]').click();
    await page.evaluate(() => window.scrollTo(0, Math.floor(document.body.scrollHeight * .65)));
    await page.waitForTimeout(150);
    const state = await page.evaluate(() => ({
      mobile: window.matchMedia('(max-width: 739px)').matches,
      floating: document.querySelector('#OvercoatFloatingPreview').classList.contains('ovcVisible'),
      previewTop: document.querySelector('[data-preview-kind="banner"]').getBoundingClientRect().top,
    }));
    if (state.mobile && !state.floating) throw new Error('Mobile floating preview did not appear after scrolling.');
    if (!state.mobile && (state.previewTop < 48 || state.previewTop > 128)) throw new Error(`Desktop preview is not sticky below the page chrome (top=${state.previewTop}).`);
    console.log(`scroll preview ok: ${state.mobile ? 'floating mobile' : 'sticky desktop'}`);
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
