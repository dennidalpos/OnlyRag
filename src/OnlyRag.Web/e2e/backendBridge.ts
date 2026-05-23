import type { Page } from "@playwright/test";

export const mockApiBaseUrl = "http://127.0.0.1:49152";
export const realBackendBaseUrl = "http://127.0.0.1:49153";
export const realBackendSessionToken = "playwright-token";

export async function installBackendBridge(
  page: Page,
  isRunning: boolean,
  baseUrl = mockApiBaseUrl,
  apiToken = "test-token"
) {
  await page.addInitScript(
    ({ apiBaseUrl, isBackendRunning, token }) => {
      window.__ONLYRAG_BACKEND__ = {
        isRunning: isBackendRunning,
        baseUrl: apiBaseUrl,
        apiToken: token,
        apiTokenHeaderName: "X-OnlyRag-Session-Token",
        errorMessage: isBackendRunning ? null : "Il backend locale non è raggiungibile."
      };
    },
    { apiBaseUrl: baseUrl, isBackendRunning: isRunning, token: apiToken }
  );
}
