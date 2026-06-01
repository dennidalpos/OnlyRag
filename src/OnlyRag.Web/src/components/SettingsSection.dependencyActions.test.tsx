import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
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

describe("SettingsSection dependency actions", () => {
  it("allows cancelling a running OCR runtime configuration", async () => {
    let ocrStatusReads = 0;
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
        response: {
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
          device: "cpu"
        }
      },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      { path: "/api/diagnostics", response: createDiagnostics({ ocrIsConfigured: false, ocrStatus: "Non configurato" }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      {
        path: "/api/dependencies/ocr",
        handler: () => ({
          body:
            ocrStatusReads++ === 0
              ? {
                  isConfigured: false,
                  isRunning: true,
                  message: "Configurazione OCR runtime cpu in corso.",
                  lastError: null,
                  runtimeTarget: "auto",
                  resolvedRuntime: "cpu",
                  runtimeDetail: null
                }
              : {
                  isConfigured: false,
                  isRunning: false,
                  message: "Configurazione OCR annullata.",
                  lastError: null,
                  runtimeTarget: "auto",
                  resolvedRuntime: "cancelled",
                  runtimeDetail: "Puoi ripetere Installa OCR quando vuoi."
                }
        })
      },
      {
        path: "/api/dependencies/ocr/cancel",
        method: "POST",
        response: { started: true, message: "Annullamento configurazione OCR richiesto." }
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

    await userEvent.click(await screen.findByRole("button", { name: "Annulla OCR" }));

    expect(await screen.findByText("Annullamento configurazione OCR richiesto.")).toBeInTheDocument();
    const cancelCall = api.calls.find((call) => call.path === "/api/dependencies/ocr/cancel");
    expect(cancelCall?.method).toBe("POST");
    expect(JSON.parse(String(cancelCall?.body))).toEqual({ confirmed: true });
  });

  it("uses automatic OCR provisioning from a single Settings action", async () => {
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
        response: {
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
          device: "cpu"
        }
      },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      {
        path: "/api/diagnostics",
        response: createDiagnostics({
          ocrIsConfigured: false,
          ocrStatus: "Non configurato",
          ocrGpuCapability: {
            isUsable: true,
            status: "GPU OCR utilizzabile",
            blockReason: null,
            runtimeDetail: "NVIDIA compatibile.",
            engineVersion: "3.3.0",
            nvidiaName: "NVIDIA RTX",
            driverVersion: "596.49",
            compiledWithCuda: true,
            cudaDeviceCount: 1,
            activeDevice: "gpu:0",
            packageVersions: {},
            capabilityStatus: "usable"
          },
          systemTelemetry: {
            cpu: { logicalProcessorCount: 8, usagePercent: 12.5 },
            memory: { totalBytes: 16 * 1024 * 1024 * 1024, availableBytes: 9 * 1024 * 1024 * 1024 },
            systemDisk: { name: "C:\\", totalBytes: 512 * 1024 * 1024 * 1024, availableBytes: 240 * 1024 * 1024 * 1024 },
            gpu: {
              name: "NVIDIA RTX",
              driverVersion: "596.49",
              usagePercent: 8,
              memoryTotalBytes: 8 * 1024 * 1024 * 1024,
              memoryAvailableBytes: 6 * 1024 * 1024 * 1024
            }
          }
        })
      },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      {
        path: "/api/dependencies/ocr",
        response: {
          isConfigured: false,
          isRunning: false,
          message: "OCR non configurato.",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "cpu",
          runtimeDetail: "Runtime scelto automaticamente."
        }
      },
      {
        path: "/api/dependencies/ocr/provision",
        method: "POST",
        response: { started: true, message: "Configurazione OCR avviata." }
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

    await userEvent.click(await screen.findByRole("button", { name: "Installa OCR GPU" }));
    expect(await screen.findByText("Configurazione OCR avviata.")).toBeInTheDocument();

    const provisionCalls = api.calls.filter((call) => call.path === "/api/dependencies/ocr/provision");
    expect(provisionCalls).toHaveLength(1);
    expect(JSON.parse(String(provisionCalls[0]?.body))).toEqual({ confirmed: true, runtimeTarget: "auto" });
    expect(screen.queryByRole("button", { name: "Configura OCR NVIDIA" })).not.toBeInTheDocument();
  });

  it("renders cached diagnostics while Settings refreshes fresh diagnostics", async () => {
    mockApi([
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
        response: {
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
          device: "cpu"
        }
      },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      { path: "/api/diagnostics", response: createDiagnostics({ appVersion: "0.2.0" }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      {
        path: "/api/dependencies/ocr",
        response: {
          isConfigured: true,
          isRunning: false,
          message: "OCR configurato.",
          lastError: null,
          runtimeTarget: "cpu",
          resolvedRuntime: "cpu",
          runtimeDetail: null
        }
      }
    ]);

    render(
      <SettingsSection
        settings={createOllamaSettings()}
        status={createOllamaStatus()}
        models={[createModel()]}
        initialDiagnostics={createDiagnostics({ appVersion: "0.1.9" })}
        loadError={null}
        onDataChanged={async () => {}}
      />
    );

    expect(screen.getByText("v0.1.9")).toBeInTheDocument();
    expect(await screen.findByText("v0.2.0")).toBeInTheDocument();
    expect(screen.queryByText("Dati diagnostici non disponibili.")).not.toBeInTheDocument();
  });

  it("requests app data reset only after explicit UI confirmation", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
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
        response: {
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
          device: "cpu"
        }
      },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      {
        path: "/api/dependencies/ocr",
        response: {
          isConfigured: true,
          isRunning: false,
          message: "OCR configurato.",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "configured",
          runtimeDetail: null
        }
      },
      {
        path: "/api/app/reset-on-next-startup",
        method: "POST",
        response: { message: "Reset dati pianificato." }
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

    await userEvent.click(await screen.findByRole("button", { name: "Reset dati al riavvio" }));

    expect(await screen.findByText("Reset dati pianificato.")).toBeInTheDocument();
    const resetCall = api.calls.find((call) => call.path === "/api/app/reset-on-next-startup");
    expect(JSON.parse(String(resetCall?.body))).toEqual({ confirmed: true });
  });
});
