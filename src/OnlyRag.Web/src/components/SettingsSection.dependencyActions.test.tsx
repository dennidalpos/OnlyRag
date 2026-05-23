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
                  runtimeDetail: "Puoi ripetere Configura OCR quando vuoi."
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
