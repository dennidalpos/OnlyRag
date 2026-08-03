import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SettingsSection } from "./SettingsSection";
import { mockApi } from "../../test/apiMock";
import {
  createDiagnostics,
  createModel,
  createOcrLanguage,
  createOllamaInstallStatus,
  createOllamaSettings,
  createOllamaStatus,
  createPerformanceSettings
} from "../../test/fixtures";

describe("SettingsSection", () => {
  it("saves trusted non-local Ollama settings and reports success", async () => {
    const onDataChanged = vi.fn(async () => {});
    const api = mockApi([
      { path: "/api/settings/pdf-export", response: { libreOfficePath: null, conversionTimeoutSeconds: 120 } },
      {
        path: "/api/pdf-export/status",
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
    expect(screen.getAllByTitle(/Abilita la fiducia solo su reti attendibili/).length).toBeGreaterThan(0);

    await userEvent.click(screen.getByLabelText("Considera attendibile questo endpoint Ollama non locale"));
    await userEvent.click(screen.getByRole("button", { name: "Salva impostazioni" }));

    expect(await screen.findByText("Impostazioni Ollama salvate.")).toBeInTheDocument();
    expect(onDataChanged).toHaveBeenCalledOnce();

    const saveCall = api.calls.find((call) => call.path === "/api/settings/ollama" && call.method === "PUT");
    expect(JSON.parse(String(saveCall?.body))).toMatchObject({
      ollamaBaseUrl: "http://192.168.1.50:11434",
      trustNonLocalEndpoint: true
    });
  }, 15000);

  it("keeps settings usable when diagnostics are unavailable", async () => {
    mockApi([
      { path: "/api/settings/pdf-export", status: 500, response: { detail: "Export PDF non disponibile." } },
      { path: "/api/pdf-export/status", status: 500, response: { detail: "Export PDF non disponibile." } },
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
    expect(await screen.findByText("Export PDF non disponibile.")).toBeInTheDocument();
  });

  it("shows a repair action when the OCR runtime is damaged", async () => {
    mockApi([
      { path: "/api/settings/pdf-export", response: { libreOfficePath: null, conversionTimeoutSeconds: 120 } },
      {
        path: "/api/pdf-export/status",
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
          ocrStatus: "Non configurato",
          ocrIsConfigured: false,
          ocrGpuCapability: {
            isUsable: false,
            status: "Runtime OCR da riparare",
            blockReason:
              "Runtime OCR locale incompleto o danneggiato. Apri Impostazioni > Diagnostica e premi Ripara OCR per reinstallare PaddleOCR e il runtime PaddlePaddle corretto.",
            runtimeDetail: "NVIDIA compatibile.",
            engineVersion: "3.5.0",
            nvidiaName: "NVIDIA RTX",
            driverVersion: "596.49",
            compiledWithCuda: false,
            cudaDeviceCount: 0,
            activeDevice: null,
            packageVersions: {},
            capabilityStatus: "runtime_missing"
          }
        })
      },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      {
        path: "/api/dependencies/ocr",
        response: {
          isConfigured: false,
          isRunning: false,
          message:
            "Runtime OCR locale incompleto o danneggiato. Apri Impostazioni > Diagnostica e premi Ripara OCR per reinstallare PaddleOCR e il runtime PaddlePaddle corretto.",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "cuda129",
          runtimeDetail: "NVIDIA compatibile."
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

    expect((await screen.findAllByText(/Runtime OCR locale incompleto o danneggiato/)).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Ripara OCR" })).toBeInTheDocument();
    expect(screen.queryByText(/paddle.base/)).not.toBeInTheDocument();
  });

  it("renders bounded settings sections and saves a performance preset", async () => {
    const api = mockApi([
      { path: "/api/settings/pdf-export", response: { libreOfficePath: null, conversionTimeoutSeconds: 120 } },
      {
        path: "/api/pdf-export/status",
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

    expect(await screen.findByRole("heading", { name: "Connessione & Modelli" })).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { name: "Ricerca RAG & Prestazioni" }).length).toBeGreaterThan(0);
    expect(screen.getByRole("heading", { name: "OCR & Documenti" })).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { name: "Manutenzione & Diagnostica" }).length).toBeGreaterThan(0);
    expect(screen.getByRole("combobox", { name: "Dispositivo" })).toHaveAccessibleDescription(
      "CPU e' piu compatibile. GPU usa il runtime NVIDIA preparato da Installa OCR quando disponibile."
    );
    expect(screen.getByRole("slider", { name: "DPI PDF" })).toHaveAccessibleDescription(
      "Risoluzione usata per convertire pagine PDF in immagini prima dell'OCR. Valori bassi sono piu veloci, valori alti leggono meglio testi piccoli."
    );
    expect(screen.getByRole("slider", { name: "Chunk contesto" })).toHaveAccessibleDescription(
      "Limita quanti chunk recuperati entrano nella risposta RAG; num_ctx Ollama resta configurato nei modelli."
    );
    expect(screen.queryByText(/Chunk contesto limita quanti chunk/)).not.toBeInTheDocument();
    expect(screen.queryByText(/La lingua documento si sceglie durante import/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Ripristina default aggiorna solo le impostazioni/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Modalità PC poco performante")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Lingua OCR")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Salva impostazioni" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Salva modelli predefiniti" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Salva prestazioni" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Salva ingestione" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Salva OCR" })).toBeDisabled();

    await userEvent.clear(screen.getByLabelText("URL Ollama"));
    await userEvent.type(screen.getByLabelText("URL Ollama"), "http://127.0.0.1:11434");
    expect(screen.getByRole("button", { name: "Salva impostazioni" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Salva modelli predefiniti" })).toBeEnabled();

    fireEvent.change(screen.getByRole("slider", { name: /Dimensione chunk/ }), { target: { value: "850" } });
    expect(screen.getByRole("button", { name: "Salva ingestione" })).toBeEnabled();

    fireEvent.click(screen.getByLabelText("Classificazione orientamento documento"));
    expect(screen.getByRole("button", { name: "Salva OCR" })).toBeEnabled();

    const perfPresetGroup = screen.getByRole("group", { name: "Preset Prestazioni" });
    const powerPresetButton = within(perfPresetGroup).getByRole("button", { name: /Alto/ });
    await userEvent.click(powerPresetButton);
    expect(screen.getByRole("button", { name: "Salva prestazioni" })).toBeEnabled();
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
      { path: "/api/settings/pdf-export", response: { libreOfficePath: null, conversionTimeoutSeconds: 120 } },
      {
        path: "/api/pdf-export/status",
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
            packageVersions: {},
            capabilityStatus: "usable"
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
    const ocrPresetButtons = screen.getAllByRole("button", { name: /Alto/ });
    const ocrAltoButton = ocrPresetButtons[ocrPresetButtons.length - 1];

    await userEvent.click(ocrAltoButton);
    await userEvent.click(saveButton);

    const saveCall = api.calls.find((call) => call.path === "/api/settings/ocr" && call.method === "PUT");
    expect(JSON.parse(String(saveCall?.body))).toMatchObject({
      profile: "accurate",
      device: "gpu",
      detectionSideLimit: 1536,
      recognitionBatchSize: 16
    });
  });
});
