import { defineConfig, devices } from '@playwright/test';

// 冒烟测试配置：先启动 TaskRunner + WebUI，再跑测试
export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.ts',
  fullyParallel: false,
  retries: 1,
  timeout: 30000,
  expect: { timeout: 10000 },
  reporter: 'list',
  use: {
    baseURL: 'http://127.0.0.1:5177',
    trace: 'on-first-retry',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  webServer: [
    {
      command: 'dotnet run --project ../../services/TaskRunner.Family --no-launch-profile',
      port: 8788,
      reuseExistingServer: true,
      timeout: 60000,
    },
    {
      command: 'dotnet run --project ../../services/WebUI.Family --no-launch-profile',
      port: 5177,
      reuseExistingServer: true,
      timeout: 60000,
    },
  ],
});
