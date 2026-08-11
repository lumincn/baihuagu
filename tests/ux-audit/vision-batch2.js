const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

// 裁剪 tasks 截图的任务列表区域
const sharp = null; // 无 sharp，用 canvas? 直接传整图给 vision

const jobs = [
  ['shots/i-home-dark.png', '这是深色模式首页截图。检查：1) 深色模式下文字对比度是否可读？2) 有无样式异常（浅色块残留在深色主题、文字看不清）？3) 布局有无问题？'],
  ['shots/m-home.png', '这是 375x812 手机视口首页截图。检查：1) 布局是否拥挤/错位/文字截断？2) 顶部导航和按钮是否可正常点击大小？3) 有无元素重叠或溢出？'],
  ['shots/m-search.png', '这是 375x812 手机视口搜索页截图。检查：1) 搜索框和筛选按钮是否挤在一起？2) 文字有无截断？3) 整体是否可用？'],
  ['shots/m-browse.png', '这是 375x812 手机视口知识库浏览页截图。检查：1) 知识库卡片在手机上如何排布？2) 有无文字截断/重叠？3) 筛选按钮是否太小？'],
  ['shots/m-generate.png', '这是 375x812 手机视口 AI 生成知识库页截图。检查：1) 表单控件是否溢出屏幕？2) 按钮文字是否截断？3) 有无明显问题？'],
  ['shots/pass1-tasks.png', '这是任务管理页截图。请仔细看任务列表区域：1) 各任务卡片之间有无元素重叠、文字挤压？2) 状态徽章（Success/Cancelled）、进度、按钮（重试/删除）排版是否正常？3) 有无文字被截断？'],
  ['shots/pass1-messages.png', '这是 AI 对话页截图。检查：1) 顶部工具栏（模式/提供商/模型选择）排版；2) "本地工具"那行文字是否挤在一起无间距？3) 底部输入框区域布局；4) 有无元素重叠或文字截断？'],
  ['shots/pass1-master-chat.png', '这是虚拟师父页截图。检查：1) 师父卡片（岐伯/入道进度）和"选择一位师父"空状态卡片的排布关系，是否同时显示造成困惑？2) 有无排版问题？'],
  ['shots/pass1-generate.png', '这是 AI 生成知识库页截图。检查：1) 模型选择区（DeepSeek/OpenVINO 卡片，含"付费/默认"标签）排版；2) 表单字段间距；3) 有无元素重叠或文字截断？'],
  ['shots/pass1-browse.png', '这是知识库浏览页截图。检查：1) 知识库卡片排布（7个卡片）是否整齐？2) 卡片内文字层级；3) 有无问题？'],
  ['shots/pass1-search.png', '这是全文搜索页截图。检查：1) 顶部提示条（正在监听剪贴板/Obsidian推荐）排版；2) 行业筛选按钮排布；3) 有无文字截断/重叠？']
];

(async () => {
  for (const [img, q] of jobs) {
    console.log(`### ${path.basename(img)}`);
    try {
      const out = execSync(`python tools\\vision.py "${path.resolve(img)}" "${q.replace(/"/g, "'")}" --model 3b`, {
        cwd: 'C:\\Users\\lumin\\.openclaw\\workspace', timeout: 180000, encoding: 'utf-8', maxBuffer: 1024 * 1024
      });
      fs.appendFileSync('vision-results2.md', `\n\n## ${path.basename(img)}\n${out.trim()}\n`);
      console.log('  done');
    } catch (e) {
      fs.appendFileSync('vision-results2.md', `\n\n## ${path.basename(img)}\n[FAILED ${String(e).slice(0, 150)}]\n`);
      console.log('  FAILED ' + String(e).slice(0, 120));
    }
  }
  console.log('ALL DONE');
})();
