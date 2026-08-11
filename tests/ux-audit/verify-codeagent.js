// CodeAgent 增强下拉验证：提供方(第2个) → OpenVINO，检查模型(第3个)选中
const { startAudit, openPage, shot } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/code-agent', { waitMs: 3000 });
    const triggers = audit.page.locator('.enhanced-select-trigger');
    const count = await triggers.count();
    console.log('增强下拉数量:', count);
    // 用 JS 直接驱动原生 select（Blazor @bind 监听 change）
    const info = await audit.page.evaluate(async () => {
      const selects = Array.from(document.querySelectorAll('select[data-enhanced]'));
      const labels = selects.map(s => Array.from(s.options).map(o => o.text).join('|'));
      // 找提供方 select（含 OpenVINO 选项）
      const provIdx = labels.findIndex(t => t.includes('OpenVINO'));
      if (provIdx < 0) return { provIdx: -1, labels };
      const prov = selects[provIdx];
      const ovOpt = Array.from(prov.options).find(o => o.text.includes('OpenVINO'));
      prov.value = ovOpt.value;
      prov.dispatchEvent(new Event('change', { bubbles: true }));
      await new Promise(r => setTimeout(r, 1200));
      // 模型 select = 提供方之后的下一个
      const model = selects[provIdx + 1];
      if (!model) return { provIdx, labels, modelMissing: true };
      const modelOpts = Array.from(model.options).map(o => o.text);
      const selected = model.options[model.selectedIndex]?.text || '';
      return { provIdx, labels, modelOpts, selected };
    });
    if (info.provIdx < 0) {
      console.log('❌ 无 OpenVINO 提供方，标签:', JSON.stringify(info.labels));
    } else {
      console.log('提供方选项:', JSON.stringify(info.labels[info.provIdx]));
      console.log('模型选项:', JSON.stringify(info.modelOpts));
      console.log('切换后选中模型:', JSON.stringify(info.selected), info.selected.includes('云端') ? '❌ 选中云端!' : '✅');
      const hasCloudMark = info.modelOpts.some(t => t.includes('云端'));
      console.log('模型含云端标识:', hasCloudMark ? '✅' : '❌');
      await shot(audit, 'fix3b-codeagent');
    }
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
