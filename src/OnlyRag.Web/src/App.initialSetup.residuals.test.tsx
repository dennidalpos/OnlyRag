import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import App from "./App";
import {
  createAppStatus,
  createOcrProvisionStatus,
  createOcrStartupAnalysis,
  createRequiredModels
} from "./App.testHelpers";
import { mockApi } from "./test/apiMock";
import {
  createDiagnostics,
  createOllamaInstallStatus,
  createOllamaSettings,
  createOllamaStatus,
  createPerformanceSettings
} from "./test/fixtures";

describe("App initial setup model residual checks", () => {
  it("treats non-placeholder default model values as configured on startup", async () => {
    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      {
        path: "/api/settings/ollama",
        response: createOllamaSettings({
          defaultChatModel: null,
          defaultEmbeddingModel: "missing-embed:latest",
          defaultTranslationModel: "missing-translate:latest"
        })
      },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      { path: "/api/dependencies/ocr/startup-analysis", response: createOcrStartupAnalysis() },
      { path: "/api/dependencies/ocr", response: createOcrProvisionStatus() }
    ]);

    render(<App />);

    expect(await screen.findByText("Modello chat non configurato")).toBeInTheDocument();
    expect(screen.queryByText("Modello embedding non configurato")).not.toBeInTheDocument();
    expect(screen.queryByText("Modello traduzione non configurato")).not.toBeInTheDocument();
    expect(screen.queryByText("Modello embedding non disponibile: missing-embed:latest")).not.toBeInTheDocument();
    expect(screen.queryByText("Modello traduzione non disponibile: missing-translate:latest")).not.toBeInTheDocument();
  });

  it("treats placeholder model values as not configured on startup", async () => {
    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      {
        path: "/api/settings/ollama",
        response: createOllamaSettings({
          defaultChatModel: "Nessun modello selezionato",
          defaultEmbeddingModel: "Nessun modello selezionato",
          defaultTranslationModel: "Nessun modello selezionato"
        })
      },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      { path: "/api/dependencies/ocr/startup-analysis", response: createOcrStartupAnalysis() },
      { path: "/api/dependencies/ocr", response: createOcrProvisionStatus() }
    ]);

    render(<App />);

    expect(await screen.findByText("Modello chat non configurato")).toBeInTheDocument();
    expect(screen.getByText("Modello embedding non configurato")).toBeInTheDocument();
    expect(screen.getByText("Modello traduzione non configurato")).toBeInTheDocument();
  });

  it("opens the wizard for missing default models before model listing completes", async () => {
    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      {
        path: "/api/settings/ollama",
        response: createOllamaSettings({
          defaultChatModel: null,
          defaultEmbeddingModel: null,
          defaultTranslationModel: null
        })
      },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", handler: () => new Promise(() => {}) },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      { path: "/api/dependencies/ocr/startup-analysis", response: createOcrStartupAnalysis() },
      { path: "/api/dependencies/ocr", response: createOcrProvisionStatus() }
    ]);

    render(<App />);

    expect(await screen.findByRole("dialog", { name: "Configurazione iniziale richiesta" })).toBeInTheDocument();
    expect(screen.getByText("Modello chat non configurato")).toBeInTheDocument();
    expect(screen.getByText("Modello embedding non configurato")).toBeInTheDocument();
    expect(screen.getByText("Modello traduzione non configurato")).toBeInTheDocument();
  });

  it("checks missing default models on startup even when no Ollama models are installed", async () => {
    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      {
        path: "/api/settings/ollama",
        response: createOllamaSettings({
          defaultChatModel: null,
          defaultEmbeddingModel: null,
          defaultTranslationModel: null
        })
      },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 0 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: [] } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      { path: "/api/dependencies/ocr/startup-analysis", response: createOcrStartupAnalysis() },
      { path: "/api/dependencies/ocr", response: createOcrProvisionStatus() }
    ]);

    render(<App />);

    expect(await screen.findByText("Nessun modello installato")).toBeInTheDocument();
    expect(screen.getByText("Modello chat non configurato")).toBeInTheDocument();
    expect(screen.getByText("Modello embedding non configurato")).toBeInTheDocument();
    expect(screen.getByText("Modello traduzione non configurato")).toBeInTheDocument();
  });

  it("keeps missing default model checks visible when Ollama is unavailable on startup", async () => {
    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      {
        path: "/api/settings/ollama",
        response: createOllamaSettings({
          defaultChatModel: null,
          defaultEmbeddingModel: null,
          defaultTranslationModel: null
        })
      },
      {
        path: "/api/ollama/status",
        response: createOllamaStatus({
          state: "Unavailable",
          isReachable: false,
          installedModelCount: 0,
          message: "Ollama non raggiungibile.",
          suggestion: "Avvia Ollama."
        })
      },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      { path: "/api/dependencies/ocr/startup-analysis", response: createOcrStartupAnalysis() },
      { path: "/api/dependencies/ocr", response: createOcrProvisionStatus() }
    ]);

    render(<App />);

    expect(await screen.findByText("Ollama non raggiungibile")).toBeInTheDocument();
    expect(screen.getByText("Modello chat non configurato")).toBeInTheDocument();
    expect(screen.getByText("Modello embedding non configurato")).toBeInTheDocument();
    expect(screen.getByText("Modello traduzione non configurato")).toBeInTheDocument();
  });

  it("rechecks models and OCR when leaving settings", async () => {
    let needsSetup = false;
    let startupChecks = 0;

    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      {
        path: "/api/settings/ollama",
        handler: () => ({
          body: createOllamaSettings({
            defaultChatModel: needsSetup ? null : "llama3.2:3b"
          })
        })
      },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      {
        path: "/api/diagnostics",
        handler: () => ({
          body: needsSetup
            ? createDiagnostics({ ocrIsConfigured: false, ocrStatus: "Non configurato" })
            : createDiagnostics()
        })
      },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        handler: () => {
          startupChecks += 1;
          return {
            body: createOcrStartupAnalysis({
              shouldPrompt: needsSetup,
              requiredDiskBytes: 2 * 1024 * 1024 * 1024,
              isOcrConfigured: !needsSetup,
              recommendedRuntimeTarget: "cpu",
              title: needsSetup ? "OCR CPU da installare" : "",
              message: needsSetup ? "Prepara il runtime OCR locale." : ""
            })
          };
        }
      },
      {
        path: "/api/dependencies/ocr",
        handler: () => ({
          body: needsSetup ? createOcrProvisionStatus({ isConfigured: false, message: "OCR non configurato." }) : createOcrProvisionStatus()
        })
      },
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
      { path: "/api/ocr/languages", response: [] },
      {
        path: /\/api\/ollama\/models\/details\?name=.*/,
        response: { name: "llama3.2:3b", numCtx: 8192 }
      }
    ]);

    render(<App />);

    await waitFor(() => {
      expect(startupChecks).toBe(1);
    });
    expect(screen.queryByRole("dialog", { name: "Configurazione iniziale richiesta" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Impostazioni" }));
    await screen.findByRole("heading", { name: "Impostazioni", level: 2 });
    needsSetup = true;
    fireEvent.click(screen.getByRole("button", { name: "Chat" }));

    expect(await screen.findByText("Modello chat non configurato")).toBeInTheDocument();
    expect(screen.getByText("OCR CPU da installare")).toBeInTheDocument();
    expect(startupChecks).toBeGreaterThanOrEqual(2);
  });
});
