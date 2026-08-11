// 第二批优化验证：log-errors 键、qr-tool 时序、master-stage 空态、听书崩溃、search 竞态、frontmatter、打卡清单
const { startAudit, openPage, shot } = require('./login');

(async () => {
  const audit = await startAudit();
  let pass = 0, fail = 0;
  const check = (name, ok, extra = '') => {
    console.log(`${ok ? '✅' : '❌'} ${name}${extra ? ' — ' + extra : ''}`);
    ok ? pass++ : fail++;
  };
  try {
    // 1. /log-errors 资源键
    await openPage(audit, '/log-errors', { waitMs: 2500 });
    let txt = await audit.page.locator('body').innerText().catch(() => '');
    const hasKeyName = /LogErrors_[A-Za-z]+/.test(txt);
    check('log-errors 不再显示原始键名', !hasKeyName, hasKeyName ? '仍有键名: ' + (txt.match(/LogErrors_[A-Za-z]+/g) || []).join(',') : '标题=' + txt.split('\n').find(l => l.includes('错误') || l.includes('日志'))?.trim());
    await shot(audit, 'fix2-logerrors');

    // 2. /qr-tool 时序（进入即两个二维码 + 通用二维码一次成功）
    await openPage(audit, '/qr-tool', { waitMs: 3500 });
    await audit.page.waitForTimeout(1500);
    const qrImgs = await audit.page.locator('#server-qr-container canvas, #aikey-qr-container canvas').count();
    const qrErrText = await audit.page.locator('#server-qr-container, #aikey-qr-container').innerText().catch(() => '');
    check('qr-tool 自动二维码生成', qrImgs >= 2 && !qrErrText.includes('失败'), `canvas=${qrImgs}`);
    // 通用二维码首次生成（卡片默认折叠，先展开）
    await audit.page.locator('.card-header', { hasText: '通用二维码' }).click();
    await audit.page.waitForTimeout(600);
    await audit.page.locator('textarea.form-control').fill('ABC123');
    await audit.page.waitForTimeout(300);
    const genBtn = audit.page.locator('button', { hasText: '生成二维码' }).first();
    await genBtn.click();
    await audit.page.waitForTimeout(2000);
    const genCanvas = await audit.page.locator('#general-qr-container canvas').count();
    const genErr = await audit.page.locator('#general-qr-container').innerText().catch(() => '');
    check('qr-tool 通用二维码一次成功', genCanvas >= 1 && !genErr.includes('失败'), `canvas=${genCanvas} err='${genErr}'`);
    await shot(audit, 'fix2-qrtool');

    // 3. /master-stage 空态
    await openPage(audit, '/master-stage', { waitMs: 2500 });
    txt = await audit.page.locator('body').innerText().catch(() => '');
    check('master-stage 无参显示空态', txt.includes('师父') && !txt.includes('师父：（空白）') && !/师父：\s*$/.test(txt), txt.split('\n').filter(l => l.includes('师父')).join('|').slice(0, 80));
    await shot(audit, 'fix2-masterstage');

    // 4. 听书不崩溃（本机无语音包 → 提示错误而非崩溃）
    await openPage(audit, '/browse', { waitMs: 3000 });
    const listenBtn = audit.page.locator('button', { hasText: '听' }).first();
    if (await listenBtn.count()) {
      await listenBtn.click();
      await audit.page.waitForTimeout(2500);
      const playBtn = audit.page.locator('.modal button', { hasText: '播放' }).first();
      if (await playBtn.count()) {
        await playBtn.click();
        await audit.page.waitForTimeout(3000);
        const alertTxt = await audit.page.locator('.alert-warning').innerText().catch(() => '');
        // 页面仍可交互（点击一个 tab/链接不失效）：检查模态还在 + 无崩溃
        const modalAlive = await audit.page.locator('.modal').isVisible().catch(() => false);
        const circuitDead = await audit.page.locator('#blazor-error-ui').isVisible().catch(() => false);
        check('听书播放不崩溃且页面可交互', modalAlive && !circuitDead, `提示='${alertTxt.slice(0, 40)}'`);
        await shot(audit, 'fix2-listen');
      } else check('听书播放不崩溃', false, '未找到播放按钮');
    } else check('听书播放不崩溃', false, '未找到听按钮');

    // 5. /search 加载竞态（打开后立即搜索 → 应提示加载中而非"请先创建知识库"）
    await openPage(audit, '/search', { waitMs: 800 }); // 不等加载完成
    const searchInput = audit.page.locator('.search-box input').first();
    await searchInput.fill('中医');
    await audit.page.keyboard.press('Enter');
    await audit.page.waitForTimeout(1200);
    txt = await audit.page.locator('body').innerText().catch(() => '');
    const loadingHint = txt.includes('知识库列表加载中');
    const wrongHint = txt.includes('请先创建知识库');
    check('search 加载期提示正确', loadingHint && !wrongHint, loadingHint ? '显示加载中提示' : (wrongHint ? '误报请先创建知识库' : txt.split('\n').find(l => l.includes('搜索'))?.trim().slice(0, 50)));
    await shot(audit, 'fix2-search');
    // 加载完成后搜索应正常
    await audit.page.waitForTimeout(6000);
    await searchInput.fill('中医');
    await audit.page.keyboard.press('Enter');
    await audit.page.waitForTimeout(3000);
    txt = await audit.page.locator('body').innerText().catch(() => '');
    check('search 加载完成后正常出结果', !txt.includes('请先创建知识库') && (txt.includes('搜索结果') || txt.includes('条')), txt.split('\n').find(l => l.includes('搜索'))?.trim().slice(0, 60));

    // 6. /note frontmatter（打开一个 AI 生成笔记，正文不应含 ai_generated）
    await openPage(audit, '/note?id=' + encodeURIComponent('病因病机/风邪与过敏的关系') + '&vaultId=' + encodeURIComponent('中医抗敏'), { waitMs: 3000 });
    txt = await audit.page.locator('.note-body').innerText().catch(() => '');
    check('note 阅读视图无 frontmatter', !txt.includes('ai_generated') && !txt.includes('ai_provider'), txt.slice(0, 60).replace(/\n/g, ' '));
    await shot(audit, 'fix2-note');

    // 7. 打卡清单卡片标题
    await openPage(audit, '/checkin', { waitMs: 3000 });
    txt = await audit.page.locator('body').innerText().catch(() => '');
    const hexId = /卡片 [0-9A-F]{8,}/.test(txt);
    check('打卡清单不显示原始卡片ID', !hexId, hexId ? '仍显示ID' : (txt.split('\n').filter(l => l.includes('卡片') || l.includes('每日')).slice(0, 3).join('|').slice(0, 80)));
    await shot(audit, 'fix2-checkin');

    console.log(`\n结果: ${pass} 通过 / ${fail} 失败`);
    console.log('console 错误:', audit.issues.consoleErrors.length ? audit.issues.consoleErrors.slice(0, 3) : '无');
    console.log('page 错误:', audit.issues.pageErrors.length ? audit.issues.pageErrors.slice(0, 3) : '无');
  } finally {
    await audit.browser.close();
  }
  process.exit(fail > 0 ? 1 : 0);
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
