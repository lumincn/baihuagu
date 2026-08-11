const { startAudit, openPage, shot } = require('./login');
(async () => {
  const audit = await startAudit();
  const r = await openPage(audit, '/settings');
  console.log('url:', r.url, 'ok:', r.ok);
  await shot(audit, 'smoke-settings');
  console.log(JSON.stringify(audit.issues, null, 2).slice(0, 2000));
  await audit.browser.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
