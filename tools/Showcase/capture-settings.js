#!/usr/bin/env node
// Standalone capture of the real embedded settings HTML. No server URL, login, or live API exists.
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(__dirname, '../..');
const out = path.resolve(process.argv[2] || path.join(root, 'assets'));

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROMIUM_EXECUTABLE || undefined });
  const page = await browser.newPage({ viewport: { width: Number(process.env.SHOWCASE_VIEWPORT_WIDTH || 1180), height: 900 }, deviceScaleFactor: 1 });
  await page.addInitScript(({previewUrl, cssUrl}) => {
    const config = { BadgesEnabled:true, BannerStyle:'glass', BannerShape:'pill', BannerPosition:'top', BannerAlign:'center', BannerFont:'default', BannerFontScale:1, BannerIcons:true, BannerShadow:true, BannerShadowStrength:60, GlassTint:'#0E1018', GlassTintStrength:49, GlassBlur:50, NeonGlow:60, BadgeSide:'left', BadgeVertical:'middle', BadgeScale:100, BadgeGapPercent:1, ScheduleEnabled:true, ScheduleHour:3, ScheduleMinute:0, TrendingTimeWindow:'week', WatchHistoryAllUsers:true, Libraries:[{Name:'TV Shows',Enabled:true,StatusOverlays:true,TrendingBadge:true,WatchHistoryBadge:true,ImdbTop250Badge:false},{Name:'Movies',Enabled:true,StatusOverlays:false,TrendingBadge:true,WatchHistoryBadge:true,ImdbTop250Badge:true}] };
    window.ApiClient = { accessToken:()=> 'synthetic-capture-token', getUrl:(route)=>route.includes('configPage.css') ? cssUrl : previewUrl, getJSON:()=>Promise.resolve([]), ajax:()=>Promise.resolve({}), getPluginConfiguration:()=>Promise.resolve(config), updatePluginConfiguration:()=>Promise.resolve({}), getVirtualFolders:()=>Promise.resolve([{Name:'TV Shows',CollectionType:'tvshows'},{Name:'Movies',CollectionType:'movies'}]), getUsers:()=>Promise.resolve([{Id:'demo',Name:'Demo user'}]), getScheduledTasks:()=>Promise.resolve([]) };
    window.Dashboard = { showLoadingMsg(){}, hideLoadingMsg(){}, processPluginConfigurationUpdateResult(){}, alert(){}, confirm(_m,_t,cb){cb(false);} };
  }, {previewUrl:'file://' + path.join(root, 'private/showcase-input/breaking-bad.jpg'), cssUrl:'file://' + path.join(root, 'Jellyfin.Plugin.Overcoat/Configuration/configPage.css')});
  await page.goto('file://' + path.join(root, 'Jellyfin.Plugin.Overcoat/Configuration/configPage.html'));
  await page.addStyleTag({path:path.join(root, 'Jellyfin.Plugin.Overcoat/Configuration/configPage.css')});
  await page.addStyleTag({content:'html,body{margin:0;height:100%;background:#101217;color:#e8ebef;font-family:Arial,sans-serif}.jellyfinViewport{height:100%;overflow:auto}.pluginConfigurationPage{overflow:hidden}.content-primary form{max-width:54em;margin:0 auto}.content-primary{padding:0 3.2%;max-width:100%}button,input,select,textarea{font:inherit;color:inherit;background:#20252d;border:1px solid #4a5260;border-radius:4px;padding:8px}.material-icons,.ovcIcon{font-family:Arial!important}'});
  if (process.env.SHOWCASE_THEME === 'light') await page.addStyleTag({content:'html,body{background:#f4f5f7!important;color:#18202a!important}button,input,select,textarea{color:#18202a!important;background:#fff!important;border-color:#aab2bd!important}'});
  await page.locator('#OvercoatConfigPage').evaluate(el => { const shell=document.createElement('div'); shell.className='jellyfinViewport'; el.parentNode.insertBefore(shell,el); shell.appendChild(el); });
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
    await page.evaluate(() => {
      window.scrollTo(0, Math.floor(document.body.scrollHeight * .35));
      const shell = document.querySelector('.jellyfinViewport');
      const preview = document.querySelector('[data-preview-kind="banner"]');
      const stacked = matchMedia('(max-width:1099px)').matches;
      shell.scrollTop += Math.max(0, stacked ? preview.getBoundingClientRect().bottom + 80 : preview.getBoundingClientRect().top - 150);
    });
    await page.waitForTimeout(150);
    const state = await page.evaluate(() => ({
      mobile: window.matchMedia('(max-width: 1099px)').matches,
      floating: document.querySelector('#OvercoatFloatingPreview').classList.contains('ovcVisible'),
      previewTop: document.querySelector('[data-preview-kind="banner"]').getBoundingClientRect().top,
    }));
    if (state.mobile && !state.floating) throw new Error('Mobile floating preview did not appear after scrolling.');
    if (!state.mobile && (state.previewTop < 100 || state.previewTop > 600)) throw new Error(`Desktop preview is not sticky below the page chrome (top=${state.previewTop}).`);
    console.log(`scroll preview ok: ${state.mobile ? 'floating mobile' : 'sticky desktop'}`);
    await page.locator('button[data-tab="general"]').click();
    const computed = await page.evaluate(() => ({
      form: document.querySelector('#OvercoatConfigForm').getBoundingClientRect().width,
      available: document.querySelector('.content-primary').getBoundingClientRect().width,
      minCard: Math.min(...[...document.querySelectorAll('[data-panel="general"] > .ovcCard')].map(x=>x.getBoundingClientRect().width)),
      badgeOptions: [...document.querySelectorAll('#BadgeSide option')].map(x=>x.value),
      details: document.querySelectorAll('[data-panel="banners"] details.ovcCard').length,
    }));
    if (computed.form < Math.min(1200, computed.available - 8)) throw new Error(`Jellyfin's 54em form cap was not overridden (${computed.form}px).`);
    if (page.viewportSize().width >= 1200 && computed.minCard < 470) throw new Error(`General card is narrower than its 480px design minimum (${computed.minCard}px).`);
    if (computed.badgeOptions.join(',') !== 'left') throw new Error('Badge placement exposes an unsupported side.');
    if (computed.details < 3) throw new Error('Advanced banner controls are not accordions.');
    await page.locator('button[data-tab="banners"]').click();
    const dateDetails = page.locator('[data-panel="banners"] details').filter({hasText:'Status dates'});
    const wasOpen = await dateDetails.getAttribute('open');
    await dateDetails.locator('summary').click();
    if ((await dateDetails.getAttribute('open')) === wasOpen) throw new Error('Banner accordion did not toggle.');
    await page.locator('button[data-tab="general"]').click();
    await page.locator('#WatchHistoryDays').fill('21');
    await page.locator('#WatchHistoryDays').dispatchEvent('input');
    const dirtyText = await page.locator('#OvercoatSaveState').textContent();
    if (!/Unsaved/.test(dirtyText)) throw new Error(`Dirty-state feedback did not appear (${dirtyText}).`);
    await page.locator('#OvercoatConfigForm button[type="submit"]').click();
    await page.waitForTimeout(30);
    if (!/saved/i.test(await page.locator('#OvercoatSaveState').textContent())) throw new Error('Saved confirmation did not appear.');
    const collision = await page.evaluate(() => {
      const a=document.querySelector('#OvercoatSaveDock').getBoundingClientRect();
      const b=document.querySelector('#OvercoatFloatingPreview').getBoundingClientRect();
      return b.width > 0 && !(a.right < b.left || b.right < a.left || a.bottom < b.top || b.bottom < a.top);
    });
    if (collision) throw new Error('Floating preview collides with the save dock.');
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
