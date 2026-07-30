import { defineConfig } from '@playwright/test';
import * as path from 'path';

// shared-e2e folder sits under tests/shared-e2e relative to this file (tests/e2e)
const sharedE2EPath = path.resolve(__dirname, '..', 'shared-e2e');

process.env.PLAYWRIGHT_BASE_URL = 'http://127.0.0.1:5177';
process.env.API_PORT = '8788';
process.env.CATEGORY_NAME = '笔记';

export default defineConfig({
  globalSetup: path.join(sharedE2EPath, 'global-setup.ts'),
  testDir: './tests',
  timeout: 60000,
  expect: { timeout: 15000 },
  fullyParallel: false,
  retries: 1,
  reporter: [['list'], ['json', { outputFile: 'test-results.json' }]],
  use: {
    baseURL: 'http://127.0.0.1:5177',
    headless: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    storageState: './storage-state.json',
    // Ubuntu 26.04 下 Playwright 无法自动下载浏览器，使用系统 Chromium
    // On Linux use system Chromium when PW_CHROMIUM_PATH is not set; on other OS leave undefined so Playwright uses its own browsers
    launchOptions: (() => {
      const exe = process.env.PW_CHROMIUM_PATH || (process.platform === 'linux' ? '/snap/chromium/current/usr/lib/chromium-browser/chrome' : undefined);
      return exe ? { executablePath: exe, args: ['--no-sandbox', '--disable-setuid-sandbox'] } : { args: ['--no-sandbox', '--disable-setuid-sandbox'] };
    })(),
  },
  projects: [
    // 导航系统：页面路由、导航栏、页面间跳转（本地）
    { name: 'navigation', testDir: './tests/navigation', testMatch: /.*\.spec\.ts/ },
    // 搜索功能：搜索页UI、搜索输入、查AI已移除（共享）
    { name: 'search', testDir: './tests/search', testMatch: /.*\.spec\.ts/ },
    // 知识库管理：知识库配置、根路径、Tab切换（共享）
    { name: 'vaults', testDir: path.join(sharedE2EPath, 'tests/vaults'), testMatch: /.*\.spec\.ts/ },
    // AI构建：从问题生成、从笔记拆分、知识库选择（共享）
    { name: 'ai-build', testDir: './tests/generate', testMatch: /.*\.spec\.ts/ },
    // 备份恢复：创建备份、恢复备份、跨平台选项（共享）
    { name: 'backup', testDir: './tests/backup', testMatch: /.*\.spec\.ts/ },
    // 冒烟测试：所有页面能打开、不白屏、不卡spinner（共享）
    { name: 'smoke', testDir: path.join(sharedE2EPath, 'tests/smoke'), testMatch: /.*\.spec\.ts/ },
    // Family 模式：用户类型选择、菜单过滤（共享）
    { name: 'family-mode', testDir: path.join(sharedE2EPath, 'tests/family-mode'), testMatch: /.*\.spec\.ts/ },
    // 知识库浏览：卡片式知识库列表、目录浏览、笔记预览（本地）
    { name: 'browse', testDir: './tests/browse', testMatch: /.*\.spec\.ts/ },
    // 任务管理：任务列表、状态显示、重试、清空（共享）
    { name: 'tasks', testDir: path.join(sharedE2EPath, 'tests/tasks'), testMatch: /.*\.spec\.ts/ },
    // 设置页：AI提供商管理、编辑删除（本地）
    { name: 'settings', testDir: './tests/settings', testMatch: /.*\.spec\.ts/ },
    // 记忆卡片：Anki 卡片生成任务（共享）
    { name: 'anki', testDir: path.join(sharedE2EPath, 'tests/anki'), testMatch: /.*\.spec\.ts/ },
    // 移动端管理：设备注册、发现（本地）
    { name: 'devices', testDir: './tests/family-mode', testMatch: /devices\.spec\.ts/ },
    // 家长看板：家庭统计、学习趋势、答题分布（本地）
    { name: 'dashboard', testDir: './tests/dashboard', testMatch: /.*\.spec\.ts/, use: { storageState: undefined } },
    // 每日一帖：卡片翻转、难度选择、进度显示（本地）
    { name: 'daily-card', testDir: './tests/daily-card', testMatch: /.*\.spec\.ts/ },
    // 成就墙：成就解锁、学习者管理、统计概览（本地）
    { name: 'achievements', testDir: './tests/achievements', testMatch: /.*\.spec\.ts/ },
    // 赛舟榜：榜单显示、Tab切换、排名（本地）
    { name: 'leaderboard', testDir: './tests/leaderboard', testMatch: /.*\.spec\.ts/ },
    // AI对话：消息列表、输入框、发送按钮（本地）
    { name: 'messages', testDir: './tests/messages', testMatch: /.*\.spec\.ts/ },
    // 记忆卡片：知识库选择、搜索、统计（本地）
    { name: 'cards', testDir: './tests/cards', testMatch: /.*\.spec\.ts/ },
  ],
});
