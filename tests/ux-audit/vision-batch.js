// Batch vision analysis of all pass1 screenshots
const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const shots = [
  ['home', '/'], ['search', '/search'], ['browse', '/browse'], ['note', '/note'],
  ['messages', '/messages'], ['assistant', '/assistant'], ['master-chat', '/master-chat'],
  ['master-stage', '/master-stage'], ['generate', '/generate'], ['cards', '/cards'], ['tasks', '/tasks']
];

const Q = '这是网页截图。请检查并回答：1) 布局有无明显问题（元素重叠、文字截断、错位、大片空白、内容溢出）？2) 主要信息层级是否清晰（标题/卡片/按钮的对比度与间距）？3) 按钮可点击区域是否过小或文案异常？4) 有无中英文混杂、错别字、占位文案、异常符号？5) 滚动条外是否有内容被裁掉（页面是否超高/超宽）？请具体指出位置和现象。';

(async () => {
  for (const [name, route] of shots) {
    const img = path.resolve(`shots/pass1-${name}.png`);
    console.log(`### ANALYZING ${route} (${name})`);
    try {
      const out = execSync(`python tools\\vision.py "${img}" "${Q.replace(/"/g, "'")}" --model 3b`, {
        cwd: 'C:\\Users\\lumin\\.openclaw\\workspace', timeout: 180000, encoding: 'utf-8', maxBuffer: 1024 * 1024
      });
      fs.appendFileSync('vision-results.md', `\n\n## ${route} (${name})\n${out.trim()}\n`);
      console.log(`  -> done, ${out.length} chars`);
    } catch (e) {
      fs.appendFileSync('vision-results.md', `\n\n## ${route} (${name})\n[VISION FAILED: ${String(e).slice(0,200)}]\n`);
      console.log(`  -> FAILED ${String(e).slice(0, 150)}`);
    }
  }
  console.log('ALL DONE');
})();
