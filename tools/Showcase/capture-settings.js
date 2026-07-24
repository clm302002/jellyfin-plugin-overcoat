#!/usr/bin/env node
// Standalone capture of the real embedded settings HTML. No server URL, login, or live API exists.
//
// Captures the 0.9 purpose-based shell: Design, Libraries, Data Sources, Automation, and Recovery.
// Poster/Wide Card are surfaces inside Design. Previews remain the real embedded-page hooks
// #PostersPreview and #WidePreview, backed here by a credential-free local image.
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(__dirname, '../..');
const out = path.resolve(process.argv[2] || path.join(root, 'assets'));

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROMIUM_EXECUTABLE || undefined });
  const page = await browser.newPage({ viewport: { width: Number(process.env.SHOWCASE_VIEWPORT_WIDTH || 1180), height: Number(process.env.SHOWCASE_VIEWPORT_HEIGHT || 900) }, deviceScaleFactor: 1 });
  await page.addInitScript(({previewUrl, cssUrl}) => {
    // Poster (flat) appearance fields, plus a full WideCard override object and the current semantic
    // settings. Keys mirror PluginConfiguration; ScheduleEnabled is gone (CustomScheduleTime now).
    const wide = {
      BannerStyle:'solid', BannerShape:'pill', BannerPosition:'top', BannerAlign:'center', BannerFont:'default',
      BannerFontScale:1, BannerIcons:true, BannerFullWidth:false, BannerShadow:true, BannerShadowStrength:60,
      GlassTint:'#0E1018', GlassTintStrength:49, GlassBlur:50, NeonGlow:60,
      BadgeSide:'left', BadgeVertical:'top', BadgeScale:100, BadgeGapPercent:1
    };
    const config = {
      BadgesEnabled:true,
      BannerStyle:'glass', BannerShape:'pill', BannerPosition:'top', BannerAlign:'center', BannerFont:'default',
      BannerFontScale:1, BannerIcons:true, BannerFullWidth:false, BannerShadow:true, BannerShadowStrength:60,
      GlassTint:'#0E1018', GlassTintStrength:49, GlassBlur:50, NeonGlow:60,
      BadgeSide:'left', BadgeVertical:'middle', BadgeScale:100, BadgeGapPercent:1,
      WideCardCustomize:true, WideCard:wide,
      ShowNew:true, ShowAiring:true, ShowReturning:true, ShowEnded:true, ShowCanceled:true,
      LabelNew:'NEW', LabelAiring:'AIRING', LabelReturning:'RETURNING', LabelEnded:'ENDED', LabelCanceled:'CANCELED',
      ColorNew:'#5EBD3E', ColorAiring:'#00A4DC', ColorReturning:'#8E5BEF', ColorEnded:'#5A6472', ColorCanceled:'#D23B3B',
      AiringDateFormat:'day', ReturningDateFormat:'date', ReturningDateWindowDays:90,
      TrendingTimeWindow:'week', WatchHistoryAllUsers:true, WatchHistoryDays:30, WatchHistoryMaxScan:2000, WatchHistoryUserId:'',
      ImdbTop250MovieListId:'', ImdbTop250TvListId:'',
      CustomScheduleTime:false, ScheduleHour:3, ScheduleMinute:0,
      CacheEnabled:true, DryRun:false, ReapplyAfterScan:true, ForceRestore:false,
      IgnoreTitles:[], IgnoreItemIds:[], LimitToTitles:[], LimitToItemIds:[], TmdbOverrides:[], TmdbApiKey:'',
      FutureSetting:'preserve-me',
      Libraries:[
        {Name:'TV Shows',Enabled:true,StatusOverlays:true,TrendingBadge:true,WatchHistoryBadge:true,ImdbTop250Badge:false},
        {Name:'Movies',Enabled:true,StatusOverlays:false,TrendingBadge:true,WatchHistoryBadge:true,ImdbTop250Badge:true}
      ]
    };
    window.__captureUpdates = [];
    const catalogue = [
      {Id:'11111111111111111111111111111111',Name:'Breaking Bad',Type:'Series',ProductionYear:2008},
      {Id:'22222222222222222222222222222222',Name:'El Camino: A Breaking Bad Movie',Type:'Movie',ProductionYear:2019}
    ];
    window.ApiClient = { accessToken:()=> 'synthetic-capture-token', getUrl:(route)=>route.includes('configPage.css') ? cssUrl : (route === 'Items' ? route : previewUrl), getJSON:(url)=>Promise.resolve(String(url).startsWith('Items') ? {Items:catalogue} : []), ajax:()=>Promise.resolve({}), getPluginConfiguration:()=>Promise.resolve(config), updatePluginConfiguration:(_id, next)=>{ window.__captureUpdates.push(next); Object.assign(config, next); return Promise.resolve(next); }, getVirtualFolders:()=>Promise.resolve([{Name:'TV Shows',CollectionType:'tvshows'},{Name:'Movies',CollectionType:'movies'}]), getUsers:()=>Promise.resolve([{Id:'demo',Name:'Demo user'}]), getScheduledTasks:()=>Promise.resolve([]) };
    window.Dashboard = { showLoadingMsg(){}, hideLoadingMsg(){}, processPluginConfigurationUpdateResult(){}, alert(){}, confirm(_m,_t,cb){cb(false);} };
  }, {previewUrl:'file://' + path.join(root, 'private/showcase-input/breaking-bad.jpg'), cssUrl:'file://' + path.join(root, 'Jellyfin.Plugin.Overcoat/Configuration/configPage.css')});
  await page.goto('file://' + path.join(root, 'Jellyfin.Plugin.Overcoat/Configuration/configPage.html'));
  await page.addStyleTag({path:path.join(root, 'Jellyfin.Plugin.Overcoat/Configuration/configPage.css')});
  await page.addStyleTag({content:'html,body{margin:0;height:100%;background:#101217;color:#e8ebef;font-family:Arial,sans-serif}.jellyfinViewport{height:100%;overflow:auto}.pluginConfigurationPage{overflow:hidden}.content-primary form{max-width:54em;margin:0 auto}.content-primary{padding:0 3.2%;max-width:100%}button,input,select,textarea{font:inherit;color:inherit;background:#20252d;border:1px solid #4a5260;border-radius:4px;padding:8px}.material-icons,.ovcIcon{display:none!important}'});
  if (process.env.SHOWCASE_THEME === 'light') await page.addStyleTag({content:'html,body{background:#f4f5f7!important;color:#18202a!important}button,input,select,textarea{color:#18202a!important;background:#fff!important;border-color:#aab2bd!important}'});
  await page.locator('#OvercoatConfigPage').evaluate(el => { const shell=document.createElement('div'); shell.className='jellyfinViewport'; el.parentNode.insertBefore(shell,el); shell.appendChild(el); });
  await page.locator('#OvercoatConfigPage').evaluate(el => el.dispatchEvent(new Event('viewshow')));
  await page.waitForTimeout(350);
  // Wait for the real library-row builder to render the fictional API response. This exercises the
  // same enable/collapse behavior as Jellyfin instead of replacing it with showcase-only markup.
  await page.locator('#OvercoatLibraries .ovcLib').first().waitFor({state:'attached'});
  const captures = process.env.SHOWCASE_CAPTURE_ALL === '1'
    ? [{tab:'design',name:'posters'},{tab:'design',surface:'wide',name:'wide'},{tab:'sources',name:'sources'},{tab:'libraries',name:'libraries'},{tab:'automation',name:'automation'},{tab:'recovery',name:'recovery'}]
    : [{tab:'design',name:'posters'},{tab:'design',surface:'wide',name:'wide'},{tab:'libraries',name:'libraries'},{tab:'automation',name:'maintenance'}];
  for (const capture of captures) {
    await page.evaluate(() => { window.scrollTo(0, 0); const shell = document.querySelector('.jellyfinViewport'); if (shell) shell.scrollTop = 0; });
    await page.locator(`button[data-tab="${capture.tab}"]`).click();
    if (capture.surface) await page.locator(`[data-panel="${capture.tab}"] button[data-surface="${capture.surface}"]`).click();
    await page.evaluate(() => { window.scrollTo(0, 0); const shell = document.querySelector('.jellyfinViewport'); if (shell) shell.scrollTop = 0; });
    await page.waitForTimeout(250);
    await page.screenshot({ path:path.join(out,`settings-${capture.name}.png`), fullPage:false });
  }
  if (process.env.SHOWCASE_CAPTURE_DETAILS === '1') {
    await page.locator('button[data-tab="design"]').click();
    const posterEffects = page.locator('[data-panel="design"] details').filter({has:page.locator('summary', {hasText:'Effects'})});
    const statusDesign = page.locator('[data-panel="design"] details').filter({has:page.locator('summary', {hasText:'Colours & labels'})});
    await posterEffects.screenshot({path:path.join(out, 'settings-posters-effects.png')});
    await statusDesign.screenshot({path:path.join(out, 'settings-posters-colours-labels.png')});
    await page.locator('[data-panel="design"] button[data-surface="wide"]').click();
    const wideEffects = page.locator('[data-panel="wide"] details').filter({has:page.locator('summary', {hasText:'Effects'})});
    await wideEffects.screenshot({path:path.join(out, 'settings-wide-effects.png')});
  }
  if (process.env.SHOWCASE_VERIFY_SCROLL === '1') {
    for (const tab of ['design','libraries','sources','automation','recovery']) {
      await page.locator(`button[data-tab="${tab}"]`).click();
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      if (overflow > 1) throw new Error(`${tab} has ${overflow}px of horizontal overflow.`);
    }
    if (await page.locator('button[data-tab="recovery"]').getAttribute('aria-selected') !== 'true') {
      throw new Error('Tab ARIA selection state did not update.');
    }
    await page.locator('button[data-tab="design"]').focus();
    await page.keyboard.press('ArrowRight');
    if (await page.locator('button[data-tab="libraries"]').getAttribute('aria-selected') !== 'true') {
      throw new Error('Keyboard tab navigation failed.');
    }
    await page.locator('button[data-tab="sources"]').click();
    if (await page.locator('#TmdbApiKey').getAttribute('type') !== 'password') throw new Error('TMDB key is not masked by default.');
    await page.locator('#ToggleTmdbApiKey').click();
    if (await page.locator('#TmdbApiKey').getAttribute('type') !== 'text') throw new Error('TMDB key reveal control failed.');
    await page.locator('#ToggleTmdbApiKey').click();
    if (await page.locator('#TmdbApiKey').getAttribute('type') !== 'password') throw new Error('TMDB key hide control failed.');
    await page.locator('button[data-tab="design"]').click();
    await page.evaluate(() => {
      window.scrollTo(0, 0);
      const shell = document.querySelector('.jellyfinViewport');
      const preview = document.querySelector('[data-preview-kind="poster"]');
      const stacked = matchMedia('(max-width:1099px)').matches;
      if (stacked) {
        document.querySelector('[data-panel="design"] .ovcBannerControls').scrollIntoView({block:'start'});
      } else {
        shell.scrollTop = Math.min(shell.scrollHeight - shell.clientHeight, 720);
      }
      shell.dispatchEvent(new Event('scroll'));
    });
    await page.waitForTimeout(150);
    const state = await page.evaluate(() => ({
      mobile: window.matchMedia('(max-width: 1099px)').matches,
      floating: document.querySelector('#OvercoatFloatingPreview').classList.contains('ovcVisible'),
      previewTop: document.querySelector('[data-preview-kind="poster"]').getBoundingClientRect().top,
      previewHeight: document.querySelector('[data-preview-kind="poster"]').getBoundingClientRect().height,
      previewPosition: getComputedStyle(document.querySelector('[data-preview-kind="poster"]')).position,
      previewMaxHeight: getComputedStyle(document.querySelector('[data-preview-kind="poster"]')).maxHeight,
    }));
    if (state.mobile && !state.floating) throw new Error('Mobile floating preview did not appear after scrolling.');
    if (!state.mobile && (state.previewTop < 50 || state.previewTop > 600)) throw new Error(`Desktop preview is not sticky below the page chrome (${JSON.stringify(state)}).`);
    console.log(`scroll preview ok: ${state.mobile ? 'floating mobile' : 'sticky desktop'}`);
    await page.locator('button[data-tab="automation"]').click();
    const computed = await page.evaluate(() => ({
      form: document.querySelector('#OvercoatConfigForm').getBoundingClientRect().width,
      available: document.querySelector('.content-primary').getBoundingClientRect().width,
      minCard: Math.min(...[...document.querySelectorAll('[data-panel="automation"] > .ovcCard')].map(x=>x.getBoundingClientRect().width)),
      badgeOptions: [...document.querySelectorAll('#BadgeSide option')].map(x=>x.value),
      details: document.querySelectorAll('[data-panel="design"] details.ovcCard').length,
      tabTops: [...document.querySelectorAll('.ovcTab')].map(x=>Math.round(x.getBoundingClientRect().top)),
      actionRadii: ['OvercoatRunNow','OvercoatRestore','OvercoatVaultRefresh'].map(id=>parseFloat(getComputedStyle(document.querySelector('#'+id)).borderRadius)),
      libraryActionRadii: ['OvercoatUseWideCardsAll','OvercoatUseEpisodeStillsAll'].map(id=>parseFloat(getComputedStyle(document.querySelector('#'+id)).borderRadius)),
      forceRestoreGap: parseFloat(getComputedStyle(document.querySelector('.ovcForceRestore')).columnGap),
      searchButtonWidths: ['OvercoatTitleSearchButton','OvercoatIgnoreSearchButton'].map(id=>Math.round(document.querySelector('#'+id).getBoundingClientRect().width)),
      runTop: document.querySelector('#OvercoatRunRestoreCard').getBoundingClientRect().top,
      behaviourTop: document.querySelector('[data-panel="automation"] .ovcCard:not(#OvercoatRunRestoreCard)').getBoundingClientRect().top,
      applyTop: document.querySelector('#OvercoatApplyTile').getBoundingClientRect().top,
      applyWidth: document.querySelector('#OvercoatApplyTile').getBoundingClientRect().width,
      runWidth: document.querySelector('#OvercoatRunRestoreCard').getBoundingClientRect().width,
      runBackground: getComputedStyle(document.querySelector('#OvercoatRunRestoreCard')).backgroundColor,
      statusSwitches: ['ShowNew','ShowAiring','ShowReturning','ShowEnded','ShowCanceled'].map(id=>{
        const el=document.querySelector('#'+id), box=el.getBoundingClientRect(), style=getComputedStyle(el);
        return [Math.round(box.width),Math.round(box.height),style.borderRadius].join(':');
      }),
      previewOverflow: getComputedStyle(document.querySelector('[data-preview-kind="poster"]')).overflowY,
      randomOrder: parseInt(getComputedStyle(document.querySelector('.ovcSourceBtn[data-source="random"]').parentElement).order, 10),
      posterOrder: parseInt(getComputedStyle(document.querySelector('#PostersPreview')).order, 10),
    }));
    if (computed.form < Math.min(1200, computed.available - 8)) throw new Error(`Jellyfin's 54em form cap was not overridden (${computed.form}px).`);
    if (page.viewportSize().width >= 1200 && computed.minCard < 470) throw new Error(`Maintenance card is narrower than its 480px design minimum (${computed.minCard}px).`);
    if (computed.badgeOptions.join(',') !== 'left') throw new Error('Badge placement exposes an unsupported side.');
    if (computed.details < 3) throw new Error('Advanced banner controls are not accordions.');
    if (new Set(computed.tabTops).size !== 1) throw new Error('Purpose navigation is not aligned across the top.');
    if (computed.actionRadii.some(x=>x < 20)) throw new Error(`Run/Restore/Recheck actions are not rounded (${computed.actionRadii.join(', ')}).`);
    if (computed.libraryActionRadii.some(x=>x < 20)) throw new Error(`Library artwork actions are not rounded (${computed.libraryActionRadii.join(', ')}).`);
    if (computed.forceRestoreGap < 4 || computed.forceRestoreGap > 24) throw new Error(`Force Restore switch is detached from its wording (${computed.forceRestoreGap}px gap).`);
    if (computed.searchButtonWidths.some(x=>x > 112)) throw new Error(`Catalogue Search button is oversized (${computed.searchButtonWidths.join(', ')}).`);
    if (computed.runTop >= computed.behaviourTop) throw new Error('Run Now is not the first Automation section.');
    if (Math.abs(computed.applyWidth - computed.runWidth) > 2) throw new Error('Apply and test-title card does not span the full Run Now width.');
    if (computed.runBackground !== 'rgba(0, 0, 0, 0)') throw new Error(`Run Now still has an outer box (${computed.runBackground}).`);
    if (new Set(computed.statusSwitches).size !== 1) throw new Error(`Status visibility switches do not share one shape (${computed.statusSwitches.join(', ')}).`);
    if (computed.previewOverflow === 'auto' || computed.previewOverflow === 'scroll' || computed.randomOrder >= computed.posterOrder) {
      throw new Error(`Preview source controls are not accessible above the artwork (${computed.randomOrder}, ${computed.posterOrder}, ${computed.previewOverflow}).`);
    }
    if (await page.locator('#TrendingTimeWindow option[value="month"]').count() !== 1) throw new Error('Monthly TMDB trending choice is missing.');
    const customSchedule = page.locator('#CustomScheduleTime');
    if (await customSchedule.isChecked()) await customSchedule.click();
    if (await page.locator('#ScheduleTimeRow').isVisible()) throw new Error('Custom schedule fields are visible while custom time is off.');
    await customSchedule.click();
    if (!await page.locator('#ScheduleTimeRow').isVisible()) throw new Error('Custom schedule fields did not appear when custom time was enabled.');
    await customSchedule.click();
    const dryRun = page.locator('#DryRun');
    if (await dryRun.isChecked()) await dryRun.click();
    if (await page.locator('#OvercoatDryRunBanner').isVisible()) throw new Error('Dry Run notice stayed visible while Dry Run was off.');
    await dryRun.click();
    if (!await page.locator('#OvercoatDryRunBanner').isVisible()) throw new Error('Dry Run notice did not appear when Dry Run was enabled.');
    await dryRun.click();
    await page.locator('button[data-tab="recovery"]').click();
    const recoveryWidths = await page.evaluate(() => ({
      restore: document.querySelector('#OvercoatRestoreTile').getBoundingClientRect().width,
      available: document.querySelector('#OvercoatRecoveryActions').getBoundingClientRect().width,
    }));
    if (recoveryWidths.restore < recoveryWidths.available - 2) {
      throw new Error(`Restore Originals is not full width (${recoveryWidths.restore}/${recoveryWidths.available}).`);
    }
    await page.locator('button[data-tab="libraries"]').click();
    const library = page.locator('.ovcLib').first();
    const libraryEnabled = library.locator('.ovcEnabled');
    if (await libraryEnabled.isChecked()) await libraryEnabled.click();
    if (await library.locator('.ovcLibOpts').getAttribute('hidden') === null) throw new Error('Disabled library options did not collapse.');
    await libraryEnabled.click();
    if (await library.locator('.ovcLibOpts').getAttribute('hidden') !== null) throw new Error('Enabled library options did not expand.');
    await page.locator('button[data-tab="design"]').click();
    await page.locator('[data-panel="design"] .ovcSourceBtn[data-source="random"]').click();
    const posterKey = new URL(await page.locator('#PostersPreview').getAttribute('src')).searchParams.get('previewKey');
    await page.locator('[data-for="BannerShape"] button[data-v="square"]').click();
    const keyAfterEdit = new URL(await page.locator('#PostersPreview').getAttribute('src')).searchParams.get('previewKey');
    await page.locator('[data-panel="design"] button[data-surface="wide"]').click();
    const widePresets = page.locator('[data-panel="wide"] .ovcPreset[data-wide="true"]');
    const wideExpectations = [
      ['Clean', 'solid', 'drop', 'default', '1', false],
      ['Glass', 'glass', 'pill', 'default', '1', false],
      ['Neon', 'neon', 'pill', 'default', '1', false],
      ['Ribbon', 'solid', 'drop', 'sans', '1', true],
    ];
    for (const [name, style, shape, font, scale, band] of wideExpectations) {
      await widePresets.filter({hasText:name}).click();
      const actual = await page.evaluate(() => ({
        style: document.querySelector('#WideBannerStyle').value,
        shape: document.querySelector('#WideBannerShape').value,
        font: document.querySelector('#WideBannerFont').value,
        scale: document.querySelector('#WideBannerFontScale').value,
        band: document.querySelector('#WideBannerFullWidth').checked,
      }));
      if (actual.style !== style || actual.shape !== shape || actual.font !== font
          || actual.scale !== scale || actual.band !== band) {
        throw new Error(`Wide Card ${name} preset is out of sync (${JSON.stringify(actual)}).`);
      }
    }
    const wideKeyBefore = new URL(await page.locator('#WidePreview').getAttribute('src')).searchParams.get('previewKey');
    if (!posterKey || posterKey !== keyAfterEdit) throw new Error('Random poster key changed during an edit.');
    await page.locator('[data-panel="wide"] .ovcWideSourceBtn[data-source="random"]').click();
    const rerolledKey = new URL(await page.locator('#WidePreview').getAttribute('src')).searchParams.get('previewKey');
    if (rerolledKey === wideKeyBefore) throw new Error('Explicit Random click did not select a new wide-card key.');
    await page.locator('[data-panel="wide"] button[data-surface="design"]').click();
    const dateDetails = page.locator('[data-panel="design"] details').filter({hasText:'Status dates'});
    const wasOpen = await dateDetails.getAttribute('open');
    await dateDetails.locator('summary').click();
    if ((await dateDetails.getAttribute('open')) === wasOpen) throw new Error('Banner accordion did not toggle.');
    await page.locator('button[data-tab="automation"]').click();
    await page.locator('#OvercoatTitleSearch').fill('Breaking Bad');
    await page.locator('#OvercoatTitleSearchButton').click();
    await page.locator('#OvercoatTitleResults .ovcTitleResult').first().click();
    if (!/locked to 1 exact title/i.test(await page.locator('#OvercoatTargetScope').textContent())) {
      throw new Error('Targeted-run picker did not confirm its exact Jellyfin selection.');
    }
    const removeGeometry = await page.locator('#OvercoatSelectedTitles .ovcSelectedTitle').evaluate(chip => {
      const button = chip.querySelector('.ovcRemoveTitle');
      const outer = chip.getBoundingClientRect();
      const inner = button.getBoundingClientRect();
      return {
        contained: inner.left >= outer.left && inner.top >= outer.top && inner.right <= outer.right && inner.bottom <= outer.bottom,
        width: Math.round(inner.width),
        height: Math.round(inner.height),
        textAlign: getComputedStyle(document.querySelector('#OvercoatTitleSearch')).textAlign
      };
    });
    if (!removeGeometry.contained || removeGeometry.width > 26 || removeGeometry.height > 26 || removeGeometry.textAlign !== 'left') {
      throw new Error(`Selected-title remove control or search alignment is wrong (${JSON.stringify(removeGeometry)}).`);
    }
    await page.locator('button[data-tab="libraries"]').click();
    await page.locator('#OvercoatIgnoreSearch').fill('El Camino');
    await page.locator('#OvercoatIgnoreSearchButton').click();
    await page.locator('#OvercoatIgnoreResults .ovcTitleResult').nth(1).click();
    await page.locator('button[data-tab="automation"]').click();
    await page.locator('#CacheEnabled').click();
    await page.locator('#CacheEnabled').dispatchEvent('input');
    const dirtyText = await page.locator('#OvercoatSaveState').textContent();
    if (!/unsaved/i.test(dirtyText)) throw new Error(`Dirty-state feedback did not appear (${dirtyText}).`);
    const applyRadius = parseFloat(await page.locator('#OvercoatSaveDock .button-submit').evaluate(el=>getComputedStyle(el).borderRadius));
    if (applyRadius < 20) throw new Error(`Apply Changes is not rounded (${applyRadius}px).`);
    await page.locator('#OvercoatConfigForm button[type="submit"]').click();
    await page.waitForTimeout(30);
    if (!/saved/i.test(await page.locator('#OvercoatSaveState').textContent())) throw new Error('Saved confirmation did not appear.');
    const update = await page.evaluate(() => window.__captureUpdates[window.__captureUpdates.length - 1] || null);
    if (!update || update.BannerShape !== 'square' || update.BadgesEnabled !== true || update.Libraries?.length !== 2) {
      throw new Error('Saved configuration did not contain the edited shape, badge switch, and libraries.');
    }
    if (update.LimitToItemIds?.[0] !== '11111111111111111111111111111111'
        || update.IgnoreItemIds?.[0] !== '22222222222222222222222222222222') {
      throw new Error('Exact Jellyfin title selections were not serialized as stable item IDs.');
    }
    if (update.FutureSetting !== 'preserve-me') throw new Error('Saving dropped an unknown future configuration field.');
    await page.locator('button[data-tab="design"]').click();
    const statusTextWidths = await page.evaluate(() => ['LabelNew','LabelAiring','LabelReturning','LabelEnded','LabelCanceled']
      .map(id=>Math.round(document.querySelector('#'+id).getBoundingClientRect().width)));
    if (statusTextWidths.some(width=>width < 160)) throw new Error(`Banner-text fields are too narrow (${statusTextWidths.join(', ')}).`);
    const posterPresets = page.locator('[data-panel="design"] .ovcPreset:not([data-wide])');
    const presetWidths = await posterPresets.evaluateAll(buttons => buttons.map(button => Math.round(button.getBoundingClientRect().width)));
    if (Math.max(...presetWidths) > 130 || Math.max(...presetWidths) > Math.min(...presetWidths) * 1.8) {
      throw new Error(`Poster Quick Look button sizing is inconsistent (${presetWidths.join(', ')}).`);
    }
    await posterPresets.filter({hasText:'Clean'}).click();
    if (await page.locator('#BannerShape').inputValue() !== 'drop'
        || await page.locator('#BannerStyle').inputValue() !== 'solid'
        || await page.locator('#BannerFullWidth').isChecked()) {
      throw new Error('Clean preset did not apply its drop-shaped solid treatment.');
    }
    await posterPresets.filter({hasText:'Ribbon'}).click();
    if (await page.locator('#BannerShape').inputValue() !== 'drop'
        || await page.locator('#BannerFontScale').inputValue() !== '1'
        || !await page.locator('#BannerFullWidth').isChecked()
        || await page.locator('#BannerIcons').isChecked()) {
      throw new Error('Ribbon preset did not update the documented appearance fields.');
    }
    await page.locator('#OvercoatDiscard').click();
    await page.waitForTimeout(100);
    if (await page.locator('#BannerStyle').inputValue() !== 'glass'
        || await page.locator('#BannerShape').inputValue() !== 'square'
        || await page.locator('#BannerFullWidth').isChecked()) {
      throw new Error('Discard did not restore the server-backed configuration.');
    }
    await page.locator('button[data-tab="sources"]').click();
    await page.locator('#TmdbOverrides').fill('Broken | year | id');
    await page.locator('#OvercoatConfigForm button[type="submit"]').click();
    await page.waitForTimeout(30);
    if (await page.locator('#TmdbOverrides').getAttribute('aria-invalid') !== 'true'
        || !await page.locator('#TmdbOverridesError').isVisible()) {
      throw new Error('Invalid TMDB override syntax was not explained inline.');
    }
    await page.locator('#OvercoatDiscard').click();
    await page.waitForTimeout(300);
    await page.locator('button[data-tab="design"]').click();
    const collision = await page.evaluate(() => {
      const a=document.querySelector('#OvercoatSaveDock').getBoundingClientRect();
      const b=document.querySelector('#OvercoatFloatingPreview').getBoundingClientRect();
      return b.width > 0 && !(a.right < b.left || b.right < a.left || a.bottom < b.top || b.bottom < a.top);
    });
    if (collision) throw new Error('Floating preview collides with the save dock.');
    await page.locator('#LabelNew').fill('DRAFT NEW');
    await page.locator('#LabelNew').dispatchEvent('input');
    await page.waitForTimeout(30);
    if (!await page.evaluate(() => !!sessionStorage.getItem('overcoat-ui-draft-v1'))) {
      throw new Error('Unsaved draft was not written to session storage.');
    }
    await page.reload();
    await page.addStyleTag({path:path.join(root, 'Jellyfin.Plugin.Overcoat/Configuration/configPage.css')});
    await page.locator('#OvercoatConfigPage').evaluate(el => { const shell=document.createElement('div'); shell.className='jellyfinViewport'; el.parentNode.insertBefore(shell,el); shell.appendChild(el); });
    await page.locator('#OvercoatConfigPage').evaluate(el => el.dispatchEvent(new Event('viewshow')));
    await page.locator('#OvercoatLibraries .ovcLib').first().waitFor({state:'attached'});
    await page.waitForTimeout(100);
    if (await page.locator('#LabelNew').inputValue() !== 'DRAFT NEW') {
      throw new Error('Unsaved draft did not restore after reloading the configuration page.');
    }
    await page.locator('#OvercoatDiscard').click();
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
