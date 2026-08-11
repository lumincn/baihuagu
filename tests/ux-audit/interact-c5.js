// 收尾检查：openclaw 字符计数 / image-recognition 模型切换 / log-settings 表单
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');
const results = [];
const log = (name, data) => { results.push({ name, ...data }); console.log('STEP', name, JSON.stringify(data).slice(0, 300)); };
const T = (p) => p.catch(e => 'ERR:' + String(e).slice(0, 100));

(async () => {
  const audit = await startAudit();
  const { page } = audit;

  // openclaw 字符计数
  await openPage(audit, '/openclaw', { waitMs: 8000 });
  const ta = page.locator('textarea').first();
  if (await T(ta.count())) {
    await T(ta.fill('测试任务内容，字符计数测试 abc'));
    await page.waitForTimeout(600);
    const counter = await T(page.locator('body').innerText());
    const m = (counter || '').match(/\d+\s*字符/);
    log('openclaw_counter', { text: m ? m[0] : (counter || '').split('百花（寻芳居）')[1]?.slice(0, 80) });
  }
  const submitDisabled = await T(page.getByRole('button', { name: /发送任务/ }).isDisabled());
  log('openclaw_submit_disabled_with_text', { disabled: submitDisabled });

  // image-recognition 模型切换（安全）
  await openPage(audit, '/image-recognition', { waitMs: 2500 });
  const chips = await T(page.locator('.enhanced-select-chip').allInnerTexts());
  log('ir_chips', { chips });
  const seven = page.locator('.enhanced-select-chip', { hasText: '7B' }).first();
  if (await T(seven.count())) { await T(seven.click()); await page.waitForTimeout(800); }
  const selVal = await T(page.locator('select').first().evaluate(el => el.value));
  log('ir_model_selected', { value: selVal });
  await shot(audit, 'C_image-recognition_model7b');

  // log-settings：密码字段类型 + 占位符
  await openPage(audit, '/log-settings', { waitMs: 2500 });
  const pwType = await T(page.locator('input[type=password]').count());
  const phs = await T(page.locator('input').evaluateAll(ins => ins.map(i => ({ ph: i.placeholder, type: i.type }))));
  log('log_settings_inputs', { pwCount: pwType, inputs: phs });

  fs.writeFileSync('interact-c5.json', JSON.stringify(results, null, 2), 'utf8');
  await audit.browser.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
