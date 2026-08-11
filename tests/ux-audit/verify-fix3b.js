// 第三批验证 v2：ORB、CodeAgent(修正选择器)、vision 状态、local-models 文案
const { startAudit, openPage, shot } = require('./login');

(async () => {
  const audit = await startAudit();
  let pass = 0, fail = 0;
  const check = (name, ok, extra = '') => {
    console.log(`${ok ? '✅' : '❌'} ${name}${extra ? ' — ' + extra : ''}`);
    ok ? pass++ : fail++;
  };
  try {
    // 1. ai-drawing ORB 已消除
    await openPage(audit, '/ai-drawing', { waitMs: 3500 });
    await audit.page.waitForTimeout(1500);
    const orbErr = audit.issues.failedRequests.filter(r => r.includes('ORB'));
    check('ai-drawing 无 ORB 拦截', orbErr.length === 0, orbErr.slice(0, 2).join('; '));
    await shot(audit, 'fix3b-aidrawing');

    // 2. CodeAgent（select0=语言, select1=提供方, select2=模型）
    await openPage(audit, '/code-agent', { waitMs: 3000 });
    const providerOpts = await audit.page.locator('select').nth(1).locator('option').allInnerTexts().catch(() => []);
    const ovIndex = providerOpts.findIndex(t => t.includes('OpenVINO'));
    let ok2 = false, extra2 = '';
    if (ovIndex >= 0) {
      await audit.page.locator('select').nth(1).selectOption({ index: ovIndex });
      await audit.page.waitForTimeout(800);
      const modelOpts = await audit.page.locator('select').nth(2).locator('option').allInnerTexts().catch(() => []);
      const hasCloudMark = modelOpts.some(t => t.includes('云端'));
      const selectedVal = await audit.page.locator('select').nth(2).inputValue().catch(() => '');
      const selectedLabel = modelOpts.find(t => t.startsWith(selectedVal)) || '';
      ok2 = hasCloudMark && !selectedLabel.includes('云端');
      extra2 = `模型选项[${modelOpts.length}]: ${modelOpts.slice(0, 4).join('|')} 选中=${selectedLabel}`;
    } else extra2 = '无 OpenVINO 提供方: ' + providerOpts.join(',');
    check('CodeAgent 云端标识+切换重置', ok2, extra2);
    await shot(audit, 'fix3b-codeagent');

    // 3. local-models 加载文案（骨架期）
    await openPage(audit, '/local-models', { waitMs: 700 });
    await audit.page.waitForTimeout(300);
    let spinnerText = await audit.page.locator('.loading-spinner').innerText().catch(() => '');
    if (!spinnerText) { await audit.page.waitForTimeout(800); spinnerText = await audit.page.locator('.loading-spinner').innerText().catch(() => ''); }
    const hasDetail = spinnerText.includes('检测') || spinnerText.includes('扫描') || spinnerText.includes('刷新');
    check('local-models 加载骨架有文案', hasDetail, JSON.stringify(spinnerText.replace(/\n/g, ' ').slice(0, 40)));
    await shot(audit, 'fix3b-localmodels');
    // 等加载完确认不再卡骨架
    let stillLoading = true;
    for (let i = 0; i < 40 && stillLoading; i++) {
      await audit.page.waitForTimeout(1000);
      stillLoading = await audit.page.locator('.loading-spinner').isVisible().catch(() => false);
    }
    check('local-models 加载完成', !stillLoading, stillLoading ? '40s 后仍在加载' : '已显示内容');
    await shot(audit, 'fix3b-localmodels-done');

    console.log(`\n结果: ${pass} 通过 / ${fail} 失败`);
    console.log('console 错误:', audit.issues.consoleErrors.length ? audit.issues.consoleErrors.slice(0, 3) : '无');
    console.log('page 错误:', audit.issues.pageErrors.length ? audit.issues.pageErrors.slice(0, 3) : '无');
  } finally {
    await audit.browser.close();
  }
  process.exit(fail > 0 ? 1 : 0);
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
