// C组交互审计：安全交互（弹窗自动取消，不触发真实 AI/删除）
const { startAudit, openPage, shot, BASE } = require('./login');
const fs = require('fs');

const results = [];
const log = (name, data) => { results.push({ name, ...data }); console.log('STEP', name, JSON.stringify(data).slice(0, 300)); };
const safe = (fn) => { try { return fn(); } catch (e) { return 'ERR:' + String(e).slice(0, 200); } };

(async () => {
  const audit = await startAudit();
  const { page, issues } = audit;
  page.on('dialog', d => { log('dialog', { msg: d.message().slice(0, 120), type: d.type() }); d.dismiss().catch(() => {}); });

  // ---------- /qr-tool ----------
  await openPage(audit, '/qr-tool', { waitMs: 3000 });
  const qr0 = await safe(async () => ({
    canvases: await page.locator('.qr-code-wrapper canvas, .qr-code-wrapper img').count(),
    errText: await page.locator('.qr-code-wrapper').allInnerTexts(),
    wrapperCount: await page.locator('.qr-code-wrapper').count(),
  }));
  log('qr_initial', qr0);
  // 服务器配对码 刷新
  const beforeCe = issues.consoleErrors.length;
  const srvRefresh = page.getByRole('button', { name: /刷新/ }).first();
  await srvRefresh.click().catch(e => log('qr_srv_click_err', { e: String(e).slice(0, 100) }));
  await page.waitForTimeout(1800);
  log('qr_after_srv_refresh', {
    canvases: await safe(() => page.locator('.qr-code-wrapper canvas, .qr-code-wrapper img').count()),
    errText: await safe(() => page.locator('.qr-code-wrapper').allInnerTexts()),
    newCe: issues.consoleErrors.slice(beforeCe),
  });
  await shot(audit, 'C_qr-tool_after_refresh');
  // AI key 刷新（卡片头按钮）
  const aiKeyBtn = page.getByRole('button', { name: /主 AI API Key/ }).first();
  await aiKeyBtn.click().catch(() => {});
  await page.waitForTimeout(1500);
  log('qr_ai_key', {
    canvases: await safe(() => page.locator('.qr-code-wrapper canvas, .qr-code-wrapper img').count()),
    errText: await safe(() => page.locator('.qr-code-wrapper').allInnerTexts()),
  });
  // 通用二维码
  const genCard = page.locator('.card', { hasText: '通用二维码' }).first();
  await genCard.locator('.card-header').click().catch(() => {});
  await page.waitForTimeout(600);
  await genCard.locator('textarea').fill('测试二维码内容-https://example.com');
  await genCard.getByRole('button', { name: '生成二维码' }).click();
  await page.waitForTimeout(1500);
  log('qr_general', {
    canvases: await safe(() => page.locator('.qr-code-wrapper canvas, .qr-code-wrapper img').count()),
    errText: await safe(() => page.locator('.qr-code-wrapper').allInnerTexts()),
  });
  await shot(audit, 'C_qr-tool_general');

  // ---------- /settings ----------
  await openPage(audit, '/settings', { waitMs: 2500 });
  await shot(audit, 'C_settings_top');
  // 添加AI提供商
  await page.getByRole('button', { name: /添加AI提供商/ }).click();
  await page.waitForTimeout(1000);
  log('settings_add_provider', { h4: await safe(() => page.locator('h4').first().innerText()) });
  await shot(audit, 'C_settings_add_provider');
  // 取消
  const cancelBtn = page.getByRole('button', { name: /取消|返回/ }).first();
  if (await cancelBtn.count()) { await cancelBtn.click(); await page.waitForTimeout(800); }
  // 编辑第一个 provider
  await page.getByRole('button', { name: '编辑' }).first().click();
  await page.waitForTimeout(1000);
  await shot(audit, 'C_settings_edit_provider');
  log('settings_edit', { h4: await safe(() => page.locator('h4').first().innerText()), hasApiKeyField: await safe(() => page.locator('input[type=password], input[placeholder*=sk-]').count()) });
  const cancelBtn2 = page.getByRole('button', { name: /取消|返回/ }).first();
  if (await cancelBtn2.count()) { await cancelBtn2.click(); await page.waitForTimeout(800); }
  // 删除 provider（弹窗会被自动取消）
  const delBtn = page.getByRole('button', { name: '删除' }).first();
  log('settings_del_provider', { exists: await delBtn.count() });
  if (await delBtn.count()) { await delBtn.click(); await page.waitForTimeout(1200); }
  log('settings_del_after', { providersStillShown: await safe(() => page.locator('.ai-provider, table').count()) });
  await shot(audit, 'C_settings_after_del_cancel');

  // ---------- /local-models ----------
  await openPage(audit, '/local-models', { waitMs: 5000 });
  const lm = await safe(() => page.locator('body').innerText());
  log('local_models_overview', { stillRefreshing: lm.includes('刷新中...'), hasOpenVinoModels: lm.includes('Qwen2.5-14B'), head: lm.slice(lm.indexOf('概览'), lm.indexOf('概览') + 400) });
  await shot(audit, 'C_local-models_overview');
  // 切到 Ollama / LM Studio / llama.cpp / OpenVINO 四个 tab
  for (const tab of ['🦙 Ollama', '💜 LM Studio', '⚡ llama.cpp', '🧿 OpenVINO']) {
    const t = page.getByRole('button', { name: new RegExp(tab.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')) }).first();
    if (await t.count()) { await t.click(); await page.waitForTimeout(1200); }
    const body = await safe(() => page.locator('body').innerText());
    log('local_models_tab_' + tab, { len: (body || '').length, snippet: (body || '').replace(/\s+/g, ' ').slice(0, 220) });
  }
  await shot(audit, 'C_local-models_openvino_tab');

  // ---------- /prompt-templates ----------
  await openPage(audit, '/prompt-templates', { waitMs: 2500 });
  await page.getByRole('button', { name: /新建模板/ }).click();
  await page.waitForTimeout(1000);
  await shot(audit, 'C_prompt-templates_new');
  log('pt_new', { body: (await safe(() => page.locator('body').innerText())).replace(/\s+/g, ' ').slice(-400) });
  const ptCancel = page.getByRole('button', { name: /取消|返回/ }).first();
  if (await ptCancel.count()) { await ptCancel.click(); await page.waitForTimeout(800); }
  const delTmpl = page.getByRole('button', { name: '删除' }).first();
  if (await delTmpl.count()) { await delTmpl.click(); await page.waitForTimeout(1200); }
  log('pt_delete_dialog', {});
  await shot(audit, 'C_prompt-templates_after_del_cancel');

  // ---------- /model-benchmark ----------
  await openPage(audit, '/model-benchmark', { waitMs: 2500 });
  const mb0 = await safe(() => page.locator('body').innerText());
  log('mb_initial', { hasStartBtn: mb0.includes('开始测试'), hasRankEmpty: mb0.includes('暂无数据') });
  // 选 provider -> 模型下拉是否填充
  const provSel = page.locator('select').nth(0);
  await provSel.selectOption({ label: 'DeepSeek (官方)' }).catch(e => log('mb_prov_select_err', { e: String(e).slice(0, 100) }));
  await page.waitForTimeout(1200);
  const modelOpts = await safe(() => page.locator('select').nth(1).locator('option').allInnerTexts());
  log('mb_models_after_provider', { modelOpts });
  await shot(audit, 'C_model-benchmark_provider_selected');
  // 切编程大模型 tab
  await page.getByRole('button', { name: /编程大模型/ }).click();
  await page.waitForTimeout(1200);
  const mbCoding = await safe(() => page.locator('body').innerText());
  log('mb_coding_tab', { snippet: mbCoding.replace(/\s+/g, ' ').slice(0, 300) });
  await shot(audit, 'C_model-benchmark_coding_tab');

  // ---------- /log-errors ----------
  await openPage(audit, '/log-errors', { waitMs: 2500 });
  await shot(audit, 'C_log-errors_page');
  // 刷新本地日志（安全）
  await page.getByRole('button', { name: /LogErrors_RefreshLocal/ }).click();
  await page.waitForTimeout(1500);
  // 清理本地日志 -> 弹窗自动取消
  await page.getByRole('button', { name: /LogErrors_ClearLocal/ }).click();
  await page.waitForTimeout(1200);
  const leBody = await safe(() => page.locator('body').innerText());
  log('log_errors_after', { hasEntries: leBody.includes('WARN') || leBody.includes('ERR'), snippet: leBody.replace(/\s+/g, ' ').slice(0, 260) });
  await shot(audit, 'C_log-errors_after_clear_cancel');

  // ---------- /openclaw ----------
  await openPage(audit, '/openclaw', { waitMs: 2500 });
  await page.getByRole('button', { name: /^刷新$/ }).click();
  await page.waitForTimeout(1500);
  const ocBody = await safe(() => page.locator('body').innerText());
  log('openclaw_refresh', { hasTask: ocBody.includes('f3597d7d') || /任务/.test(ocBody), snippet: ocBody.replace(/\s+/g, ' ').slice(0, 240) });
  await shot(audit, 'C_openclaw_refreshed');
  const errBtn = page.getByRole('button', { name: /查看错误/ }).first();
  if (await errBtn.count()) { await errBtn.click(); await page.waitForTimeout(1200); }
  log('openclaw_err_modal', { body: (await safe(() => page.locator('body').innerText())).replace(/\s+/g, ' ').slice(-300) });
  await shot(audit, 'C_openclaw_err_modal');

  // ---------- /ai-drawing：重新检测（安全） ----------
  await openPage(audit, '/ai-drawing', { waitMs: 2500 });
  await page.getByRole('button', { name: /重新检测/ }).click();
  await page.waitForTimeout(1500);
  const adBody = await safe(() => page.locator('body').innerText());
  log('ai_drawing_redetect', { snippet: adBody.replace(/\s+/g, ' ').slice(-250) });
  await shot(audit, 'C_ai-drawing_redetect');

  // ---------- /stock-advisor 页面结构（不触发分析） ----------
  await openPage(audit, '/stock-advisor', { waitMs: 2500 });
  const saBody = await safe(() => page.locator('body').innerText());
  const saMain = saBody.slice(saBody.indexOf('股票 AI 建议'));
  log('stock_advisor_structure', { tail: saMain.replace(/\s+/g, ' ').slice(0, 900) });
  await shot(audit, 'C_stock-advisor_full');

  // ---------- /code-agent 页面结构 ----------
  await openPage(audit, '/code-agent', { waitMs: 2500 });
  const caBody = await safe(() => page.locator('body').innerText());
  const caMain = caBody.slice(caBody.indexOf('编程 Agent'));
  log('code_agent_structure', { tail: caMain.replace(/\s+/g, ' ').slice(0, 700) });
  await shot(audit, 'C_code-agent_full');

  // ---------- /log-settings ----------
  await openPage(audit, '/log-settings', { waitMs: 2500 });
  const lsBody = await safe(() => page.locator('body').innerText());
  log('log_settings_structure', { tail: lsBody.replace(/\s+/g, ' ').slice(lsBody.indexOf('日志配置'), lsBody.indexOf('日志配置') + 800) });
  await shot(audit, 'C_log-settings_full');

  // ---------- /image-recognition ----------
  await openPage(audit, '/image-recognition', { waitMs: 2500 });
  const irBody = await safe(() => page.locator('body').innerText());
  log('image_recognition_structure', { tail: irBody.replace(/\s+/g, ' ').slice(irBody.indexOf('图片识别'), irBody.indexOf('图片识别') + 500) });
  await shot(audit, 'C_image-recognition_full');

  fs.writeFileSync('interact-c.json', JSON.stringify(results, null, 2), 'utf8');
  console.log('==== ISSUES ====');
  console.log(JSON.stringify(issues, null, 2).slice(0, 3000));
  await audit.browser.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
