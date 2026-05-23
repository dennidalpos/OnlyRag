import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SettingsSection } from "./SettingsSection";
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

describe("SettingsSection", () => {
  it("saves trusted non-local Ollama settings and reports success", async () => {
    const onDataChanged = vi.fn(async () => {});
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
        path: "/api/settings/ollama",
        method: "PUT",
        handler: (request) => ({
          body: createOllamaSettings(
            JSON.parse(String(request.body)) as Partial<ReturnType<typeof createOllamaSettings>>
          )
        })
      }
    ]);

    render(
      <SettingsSection
        settings={createOllamaSettings()}
        status={createOllamaStatus()}
        models={[createModel()]}
        loadError={null}
        onDataChanged={onDataChanged}
      />
    );

    await userEvent.clear(screen.getByLabelText("URL Ollama"));
    await userEvent.type(screen.getByLabelText("URL Ollama"), "http://192.168.1.50:11434");
    expect(screen.getByText(/Abilita la fiducia solo per un servizio Ollama/)).toBeInTheDocument();

    await userEvent.click(screen.getByLabelText("Considera attendibile questo endpoint Ollama non locale"));
    await userEvent.click(screen.getByRole("button", { name: "Salva impostazioni" }));

    expect(await screen.findByText("Impostazioni Ollama salvate.")).toBeInTheDocument();
    expect(onDataChanged).toHaveBeenCalledOnce();

    const saveCall = api.calls.find((call) => call.path === "/api/settings/ollama" && call.method === "PUT");
    expect(JSON.parse(String(saveCall?.body))).toMatchObject({
      ollamaBaseUrl: "http://192.168.1.50:11434",
      trustNonLocalEndpoint: true
    });
  });

  it("keeps settings usable when diagnostics are unavailable", async () => {
    mockApi([
      { path: "/api/settings/office-conversion", status: 500, response: { detail: "Office non disponibile." } },
      { path: "/api/office-converter/status", status: 500, response: { detail: "Office non disponibile." } },
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
      { path: "/api/diagnostics", status: 500, response: { detail: "Diagnostica offline." } },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      {
        path: "/api/dependencies/ocr",
        response: {
          isConfigured: false,
          isRunning: false,
          message: "OCR assente.",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "cpu",
          runtimeDetail: "NVIDIA non rilevata."
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

    expect(await screen.findByText("Dati diagnostici non disponibili.")).toBeInTheDocument();
    expect(await screen.findByText("Office non disponibile.")).toBeInTheDocument();
  });

  it("renders bounded settings sections and saves a performance preset", async () => {
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
      {
        path: "/api/settings/performance",
        response: createPerformanceSettings({ profile: "auto", effectiveProfile: "balanced" })
      },
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
        path: "/api/settings/performance",
        method: "PUT",
        handler: (request) => ({ body: JSON.parse(String(request.body)) })
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

    expect(await screen.findByRole("heading", { name: "Connessioni" })).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { name: "Prestazioni" }).length).toBeGreaterThan(0);
    expect(screen.getByRole("heading", { name: "OCR" })).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { name: "Ingestion" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("heading", { name: "Diagnostica" }).length).toBeGreaterThan(0);
    expect(screen.queryByLabelText("Modalità PC poco performante")).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText("Profilo prestazioni"), "power");
    await userEvent.click(screen.getByRole("button", { name: "Salva prestazioni" }));

    const saveCall = api.calls.find((call) => call.path === "/api/settings/performance" && call.method === "PUT");
    expect(JSON.parse(String(saveCall?.body))).toMatchObject({
      profile: "power",
      effectiveProfile: "power",
      maxParallelJobs: 4,
      maxOcrParallelPages: 4,
      embeddingBatchSize: 4,
      translationBatchSize: 2,
      maxContextChunks: 12,
      requestTimeoutSeconds: 120
    });
  });

  it("preserves the selected OCR device when applying a profile preset", async () => {
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
          device: "gpu"
        }
      },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      {
        path: "/api/diagnostics",
        response: createDiagnostics({
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
            packageVersions: {}
          }
        })
      },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      {
        path: "/api/dependencies/ocr",
        response: {
          isConfigured: true,
          isRunning: false,
          message: "OCR configurato.",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "cuda126",
          runtimeDetail: "NVIDIA compatibile."
        }
      },
      {
        path: "/api/settings/ocr",
        method: "PUT",
        handler: (request) => ({ body: JSON.parse(String(request.body)) })
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

    const saveButton = await screen.findByRole("button", { name: "Salva OCR" });
    const profileSelect = document.querySelector("#ocr-profile") as HTMLSelectElement | null;
    expect(profileSelect).not.toBeNull();

    await userEvent.selectOptions(profileSelect!, "accurate");
    await userEvent.click(saveButton);

    const saveCall = api.calls.find((call) => call.path === "/api/settings/ocr" && call.method === "PUT");
    expect(JSON.parse(String(saveCall?.body))).toMatchObject({
      profile: "accurate",
      device: "gpu",
      detectionSideLimit: 1536,
      recognitionBatchSize: 16
    });
  });

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
