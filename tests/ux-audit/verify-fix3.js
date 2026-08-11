// 第三批优化验证：场景切换跳转、ai-drawing ORB、CodeAgent 云端标识、vision 启动/停止、local-models 加载文案、note Anki 按钮
const { startAudit, openPage, shot } = require('./login');

(async () => {
  const audit = await startAudit();
  let pass = 0, fail = 0;
  const check = (name, ok, extra = '') => {
    console.log(`${ok ? '✅' : '❌'} ${name}${extra ? ' — ' + extra : ''}`);
    ok ? pass++ : fail++;
  };
  try {
    // 1. 场景切换真实跳转
    await openPage(audit, '/messages', { waitMs: 2500 });
    const familyBtn = audit.page.locator('.scene-btn', { hasText: '家庭' }).first();
    if (await familyBtn.count()) {
      await familyBtn.click();
      await audit.page.waitForTimeout(2500);
      const url = audit.page.url();
      check('场景切换跳转到家庭首页', url.includes('/family'), url);
      await shot(audit, 'fix3-scene');
    } else check('场景切换跳转到家庭首页', false, '未找到家庭场景按钮');

    // 2. ai-drawing 无 ORB 拦截
    await openPage(audit, '/ai-drawing', { waitMs: 3500 });
    await audit.page.waitForTimeout(1500);
    const orbErr = audit.issues.failedRequests.filter(r => r.includes('ERR_BLOCKED_BY_ORB') || r.includes('orb'));
    check('ai-drawing 无 ORB 拦截错误', orbErr.length === 0, orbErr.slice(0, 2).join('; '));
    await shot(audit, 'fix3-aidrawing');

    // 3. CodeAgent 云端标识 + 切换重置
    await openPage(audit, '/code-agent', { waitMs: 3000 });
    const providerOpts = await audit.page.locator('select').nth(0).locator('option').allInnerTexts().catch(() => []);
    const ovIndex = providerOpts.findIndex(t => t.includes('OpenVINO'));
    let codeAgentOk = false, extra3 = '';
    if (ovIndex >= 0) {
      await audit.page.locator('select').nth(0).selectOption({ index: ovIndex });
      await audit.page.waitForTimeout(800);
      const modelOpts = await audit.page.locator('select').nth(1).locator('option').allInnerTexts().catch(() => []);
      const hasCloudMark = modelOpts.some(t => t.includes('云端'));
      const selectedVal = await audit.page.locator('select').nth(1).inputValue().catch(() => '');
      // 选中值不应是云端模型（除非云端模型就是该提供方默认）
      const selectedIsCloud = modelOpts.find(t => t.startsWith(selectedVal))?.includes('云端');
      codeAgentOk = hasCloudMark && !selectedIsCloud;
      extra3 = `选项: ${modelOpts.slice(0, 5).join(',')} | 选中: ${selectedVal}${selectedIsCloud ? '(云端!)' : ''}`;
    } else extra3 = '无 OpenVINO 提供方';
    check('CodeAgent 云端标识+切换重置', codeAgentOk, extra3);
    await shot(audit, 'fix3-codeagent');

    // 4. /note 有生成 Anki 卡片按钮
    await openPage(audit, '/note?id=' + encodeURIComponent('病因病机/风邪与过敏的关系') + '&vaultId=' + encodeURIComponent('中医抗敏'), { waitMs: 3000 });
    const ankiBtn = audit.page.locator('button', { hasText: '生成 Anki 卡片' }).first();
    check('/note 有 Anki 卡片入口', (await ankiBtn.count()) > 0);
    await shot(audit, 'fix3-note-anki');

    // 5. image-recognition 运行状态区（启动→停止闭环）
    await openPage(audit, '/image-recognition', { waitMs: 3000 });
    const downAlert = await audit.page.locator('.alert-warning', { hasText: '视觉服务' }).count();
    check('vision 未运行时显示警告+启动按钮', downAlert > 0, `警告区=${downAlert}`);
    // 启动服务（真实拉起 OpenVINO 视觉服务）
    const startBtn = audit.page.locator('button', { hasText: '启动服务' }).first();
    if (await startBtn.count()) {
      await startBtn.click();
      await audit.page.waitForTimeout(1500);
      // 等待运行状态出现（启动可能需几十秒）
      let runningAlert = await audit.page.locator('.alert-success', { hasText: '运行中' }).count();
      for (let i = 0; i < 60 && runningAlert === 0; i++) {
        await audit.page.waitForTimeout(1000);
        runningAlert = await audit.page.locator('.alert-success', { hasText: '运行中' }).count();
      }
      const runningText = runningAlert > 0 ? await audit.page.locator('.alert-success').innerText().catch(() => '') : '';
      check('vision 启动后显示运行状态+端口', runningAlert > 0 && runningText.includes('127.0.0.1'), runningText.slice(0, 50));
      await shot(audit, 'fix3-vision-running');
      // 停止服务
      const stopBtn = audit.page.locator('button', { hasText: '停止服务' }).first();
      if (await stopBtn.count()) {
        await stopBtn.click();
        await audit.page.waitForTimeout(2500);
        const backToDown = await audit.page.locator('.alert-warning', { hasText: '视觉服务' }).count();
        check('vision 停止按钮生效', backToDown > 0);
        await shot(audit, 'fix3-vision-stopped');
      } else check('vision 停止按钮生效', false, '未找到停止按钮');
    } else check('vision 未运行时显示警告+启动按钮', false, '未找到启动按钮');

    // 6. local-models 加载文案
    await openPage(audit, '/local-models', { waitMs: 1500 });
    const loadingText = await audit.page.locator('.loading-spinner').innerText().catch(() => '');
    check('local-models 加载有具体文案', loadingText.includes('检测') || loadingText.includes('扫描'), loadingText.replace(/\n/g, ' ').slice(0, 40));
    await shot(audit, 'fix3-localmodels-loading');
    // 等加载完确认页面正常
    await audit.page.waitForTimeout(20000);
    const overviewVisible = await audit.page.locator('.hardware-section, .loading-spinner').first().isVisible().catch(() => false);
    const stillLoading = await audit.page.locator('.loading-spinner').isVisible().catch(() => false);
    check('local-models 加载完成后显示内容', !stillLoading, stillLoading ? '仍在加载' : '已显示内容');

    console.log(`\n结果: ${pass} 通过 / ${fail} 失败`);
    console.log('console 错误:', audit.issues.consoleErrors.length ? audit.issues.consoleErrors.slice(0, 3) : '无');
    console.log('page 错误:', audit.issues.pageErrors.length ? audit.issues.pageErrors.slice(0, 3) : '无');
  } finally {
    await audit.browser.close();
  }
  process.exit(fail > 0 ? 1 : 0);
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
