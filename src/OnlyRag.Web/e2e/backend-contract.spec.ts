import { expect, test } from "@playwright/test";
import { installBackendBridge, realBackendBaseUrl, realBackendSessionToken } from "./backendBridge";

test("real backend contract: app status, settings, and empty document list reach the UI", async ({ page }) => {
  await installBackendBridge(page, true, realBackendBaseUrl, realBackendSessionToken);

  await page.goto("/");

  const status = page.getByLabel("Stato applicazione");
  await expect(status).toContainText("Backend");
  await expect(status).toContainText("Running", { timeout: 15000 });

  await page.getByRole("button", { name: "Documenti" }).click();
  await expect(page.getByText("Nessun documento presente. Importa un file per iniziare.")).toBeVisible({ timeout: 10000 });

  await page.getByRole("button", { name: "Apri Impostazioni" }).click();
  await expect(page.getByLabel("URL Ollama")).toHaveValue("http://localhost:11434");
});
