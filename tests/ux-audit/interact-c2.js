// C组交互审计 v2（容错版）：剩余检查
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');
const results = [];
const log = (name, data) => { results.push({ name, ...data }); console.log('STEP', name, JSON.stringify(data).slice(0, 400)); };
const T = (p) => p.catch(e => 'ERR:' + String(e).slice(0, 120));

(async () => {
  const audit = await startAudit();
  const { page, issues } = audit;
  page.on('dialog', d => { log('dialog', { msg: d.message().slice(0, 100) }); d.dismiss().catch(() => {}); });

  // ---------- /local-models 各 tab 完整内容 ----------
  await openPage(audit, '/local-models', { waitMs: 4000 });
  for (const tab of ['📊 概览', '🦙 Ollama', '💜 LM Studio', '⚡ llama.cpp', '🧿 OpenVINO']) {
    const t = page.getByRole('button', { name: new RegExp(tab.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')) }).first();
    if (await T(t.count())) { await T(t.click()); await page.waitForTimeout(3500); }
    const body = await T(page.locator('body').innerText());
    const main = (body || '').split('百花（寻芳居） - 家庭知识港湾')[1] || '';
    log('lm_tab_' + tab, { main: main.replace(/\s+/g, ' ').slice(0, 700) });
    await shot(audit, 'C_local-models_' + tab.replace(/[^\w\u4e00-\u9fa5]/g, ''));
  }

  // ---------- /model-benchmark select 结构 ----------
  await openPage(audit, '/model-benchmark', { waitMs: 2500 });
  const selHtml = await T(page.locator('select').nth(0).evaluate(el => el.outerHTML));
  log('mb_select0_html', { html: (selHtml || '').slice(0, 400) });
  const opts0 = await T(page.locator('select').nth(0).locator('option').evaluateAll(os => os.map(o => ({ v: o.value, t: o.textContent.trim() }))));
  log('mb_select0_opts', { opts: opts0 });
  // 用 value 选择
  const deepseekVal = (opts0 || []).find(o => o.t.includes('DeepSeek'))?.v;
  if (deepseekVal) {
    await T(page.locator('select').nth(0).selectOption(deepseekVal));
    await page.waitForTimeout(1500);
  }
  const opts1 = await T(page.locator('select').nth(1).locator('option').evaluateAll(os => os.map(o => ({ v: o.value, t: o.textContent.trim() }))));
  log('mb_select1_after', { opts: opts1 });
  await shot(audit, 'C_model-benchmark_selected');

  // ---------- /openclaw（等初始加载完成） ----------
  await openPage(audit, '/openclaw', { waitMs: 9000 });
  const refBtn = page.getByRole('button', { name: /^刷新$/ }).first();
  log('openclaw_refresh_enabled', { disabled: await T(refBtn.isDisabled()) });
  await T(refBtn.click());
  await page.waitForTimeout(1500);
  const ocBody = await T(page.locator('body').innerText());
  log('openclaw_after_refresh', { tail: (ocBody || '').split('任务历史')[1]?.replace(/\s+/g, ' ').slice(0, 300) });
  const errBtn = page.getByRole('button', { name: /查看错误/ }).first();
  if (await T(errBtn.count())) { await T(errBtn.click()); await page.waitForTimeout(1500); }
  const modalBody = await T(page.locator('body').innerText());
  log('openclaw_err_modal', { tail: (modalBody || '').split('任务历史')[1]?.replace(/\s+/g, ' ').slice(-400) });
  await shot(audit, 'C_openclaw_err_modal');

  // ---------- /ai-drawing 重新检测 ----------
  await openPage(audit, '/ai-drawing', { waitMs: 2500 });
  const rdBtn = page.getByRole('button', { name: /重新检测/ }).first();
  if (await T(rdBtn.count())) { await T(rdBtn.click()); await page.waitForTimeout(1500); }
  const adBody = await T(page.locator('body').innerText());
  log('ai_drawing_redetect', { tail: (adBody || '').split('百花（寻芳居）')[1]?.replace(/\s+/g, ' ').slice(0, 400) });
  await shot(audit, 'C_ai-drawing_redetect');

  // ---------- /hardware-benchmark 刷新 ----------
  await openPage(audit, '/hardware-benchmark', { waitMs: 2500 });
  const hbBtn = page.getByRole('button', { name: /刷新硬件信息/ }).first();
  if (await T(hbBtn.count())) { await T(hbBtn.click()); await page.waitForTimeout(2000); }
  const hbBody = await T(page.locator('body').innerText());
  log('hardware_after_refresh', { tail: (hbBody || '').split('百花（寻芳居）')[1]?.replace(/\s+/g, ' ').slice(0, 600) });
  await shot(audit, 'C_hardware-benchmark_refreshed');

  // ---------- /stock-advisor 完整结构 ----------
  await openPage(audit, '/stock-advisor', { waitMs: 2500 });
  const saBody = await T(page.locator('body').innerText());
  log('stock_advisor_full', { tail: (saBody || '').split('百花（寻芳居）')[1]?.replace(/\s+/g, ' ').slice(0, 1000) });
  await shot(audit, 'C_stock-advisor_full');

  // ---------- /code-agent ----------
  await openPage(audit, '/code-agent', { waitMs: 2500 });
  const caBody = await T(page.locator('body').innerText());
  log('code_agent_full', { tail: (caBody || '').split('百花（寻芳居）')[1]?.replace(/\s+/g, ' ').slice(0, 800) });
  await shot(audit, 'C_code-agent_full');

  // ---------- /log-settings ----------
  await openPage(audit, '/log-settings', { waitMs: 2500 });
  const lsBody = await T(page.locator('body').innerText());
  log('log_settings_full', { tail: (lsBody || '').split('百花（寻芳居）')[1]?.replace(/\s+/g, ' ').slice(0, 900) });
  await shot(audit, 'C_log-settings_full');

  // ---------- /image-recognition ----------
  await openPage(audit, '/image-recognition', { waitMs: 2500 });
  const irBody = await T(page.locator('body').innerText());
  log('image_recognition_full', { tail: (irBody || '').split('百花（寻芳居）')[1]?.replace(/\s+/g, ' ').slice(0, 600) });
  await shot(audit, 'C_image-recognition_full');

  fs.writeFileSync('interact-c2.json', JSON.stringify(results, null, 2), 'utf8');
  console.log('==== ISSUES TOTAL ====');
  console.log(JSON.stringify(issues, null, 2).slice(0, 4000));
  await audit.browser.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
