import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import type { OcrSettings } from "../api";
import { mockApi } from "../test/apiMock";
import {
  createDiagnostics,
  createModel,
  createOcrLanguage,
  createOllamaInstallStatus,
  createOllamaSettings,
  createOllamaStatus,
  createPerformanceSettings
} from "../test/fixtures";
import { SettingsSection } from "./SettingsSection";

describe("SettingsSection OCR provisioning", () => {
  it("refreshes diagnostics and auto-enables OCR GPU after provisioning completes", async () => {
    let provisionStarted = false;
    let autoGpuApplied = false;
    const usableGpuCapability = {
      isUsable: true,
      status: "GPU OCR utilizzabile",
      blockReason: null,
      runtimeDetail: "NVIDIA compatibile.",
      engineVersion: "3.3.1",
      nvidiaName: "NVIDIA RTX",
      driverVersion: "596.49",
      compiledWithCuda: true,
      cudaDeviceCount: 1,
      activeDevice: "gpu:0",
      packageVersions: {}
    };
    const api = mockApi([
      { path: "/api/settings/office-conversion", response: { libreOfficePath: null, conversionTimeoutSeconds: 120 } },
      {
        path: "/api/office-converter/status",
        response: {
          state: "Missing",
          isAvailable: false,
          executablePath: null,
          message: "LibreOffice non installato.",
          suggestion: null,
          conversionTimeoutSeconds: 120
        }
      },
      { path: "/api/settings/performance", response: createPerformanceSettings() },
      { path: "/api/settings/ingestion", response: { chunkSizeTokens: 800, overlapTokens: 120 } },
      {
        path: "/api/settings/ocr-processing",
        response: { language: "it", maxRetries: 2, pageTimeoutSeconds: 180, lowConfidenceThreshold: 0.55 }
      },
      {
        path: "/api/settings/ocr",
        handler: () => ({
          body: createOcrSettings({
            device: autoGpuApplied ? "gpu" : "cpu",
            recognitionBatchSize: autoGpuApplied ? 12 : 6
          })
        })
      },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      {
        path: "/api/diagnostics",
        handler: () => ({
          body: provisionStarted
            ? createDiagnostics({ ocrGpuCapability: usableGpuCapability })
            : createDiagnostics({ ocrIsConfigured: false, ocrStatus: "Non configurato" })
        })
      },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      {
        path: "/api/dependencies/ocr",
        handler: () => ({
          body: {
            isConfigured: provisionStarted,
            isRunning: false,
            message: provisionStarted ? "OCR configurato." : "OCR non configurato.",
            lastError: null,
            runtimeTarget: "auto",
            resolvedRuntime: provisionStarted ? "cuda129" : "cpu",
            runtimeDetail: provisionStarted ? "NVIDIA compatibile." : "Runtime scelto automaticamente."
          }
        })
      },
      {
        path: "/api/dependencies/ocr/provision",
        method: "POST",
        handler: () => {
          provisionStarted = true;
          return { body: { started: true, message: "Configurazione OCR avviata." } };
        }
      },
      {
        path: "/api/settings/ocr/auto-enable-gpu",
        method: "POST",
        handler: () => {
          autoGpuApplied = true;
          return {
            body: {
              applied: true,
              message: "OCR GPU abilitata automaticamente.",
              settings: createOcrSettings({ device: "gpu", recognitionBatchSize: 12 })
            }
          };
        }
      }
    ]);

    render(
      <SettingsSection
        settings={createOllamaSettings()}
        status={createOllamaStatus()}
        models={[createModel()]}
        loadError={null}
        onDataChanged={async () => {}}
      />
    );

    await userEvent.click(await screen.findByRole("button", { name: "Configura OCR" }));

    expect(await screen.findByText("Configurazione OCR avviata.")).toBeInTheDocument();
    expect(api.calls.some((call) => call.path === "/api/settings/ocr/auto-enable-gpu")).toBe(true);
    expect(api.calls.filter((call) => call.path === "/api/settings/ocr")).toHaveLength(2);
  });
});

function createOcrSettings(overrides: Partial<OcrSettings> = {}): OcrSettings {
  return {
    profile: "balanced",
    pdfDpi: 220,
    modelPreset: "PP-OCRv5",
    modelVersion: "PP-OCRv5",
    detectionSideLimit: 1152,
    detectionThreshold: 0.3,
    detectionBoxThreshold: 0.6,
    detectionUnclipRatio: 1.5,
    recognitionScoreThreshold: 0.5,
    useTextlineOrientation: true,
    useDocumentOrientationClassification: false,
    useDocumentUnwarping: false,
    recognitionBatchSize: 6,
    cpuThreads: 2,
    device: "cpu",
    ...overrides
  };
}
