import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  expect: {
    timeout: 5_000
  },
  fullyParallel: false,
  reporter: [["list"]],
  use: {
    ...devices["Desktop Edge"],
    baseURL: "http://127.0.0.1:5173",
    channel: "msedge",
    trace: "retain-on-failure"
  },
  webServer: [
    {
      command:
        "dotnet run --project ../../tests/OnlyRag.PlaywrightBackendHost/OnlyRag.PlaywrightBackendHost.csproj -- --port 49153 --session-token playwright-token",
      url: "http://127.0.0.1:49153/health",
      reuseExistingServer: false,
      timeout: 120_000
    },
    {
      command: "npm run dev -- --port 5173",
      url: "http://127.0.0.1:5173",
      reuseExistingServer: !process.env.CI,
      timeout: 30_000
    }
  ]
});
