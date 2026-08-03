import { fireEvent, render, screen } from "@testing-library/react";
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
  createPerformanceSettings,
  createRerankerModelInfo
} from "../../test/fixtures";

describe("SettingsSection Reranker Panel", () => {
  function createStandardApiMocks(rerankerInfo = createRerankerModelInfo()) {
    return [
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
          isConfigured: false,
          isRunning: false,
          message: "OCR non configurato",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "cpu",
          runtimeDetail: null,
          startedAtUtc: null,
          updatedAtUtc: null,
          stepKey: null,
          stepLabel: null,
          stepIndex: 0,
          stepCount: 0,
          progressPercent: 0,
          severity: "info",
          canRetry: false,
          selectedRuntime: null
        }
      },
      { path: "/api/rag/reranker/model", response: rerankerInfo }
    ];
  }

  it("renders non-installed state and initiates model download", async () => {
    const onDataChanged = vi.fn(async () => {});
    const api = mockApi([
      ...createStandardApiMocks(createRerankerModelInfo({ isDownloaded: false, isDownloading: false })),
      { path: "/api/rag/reranker/download", method: "POST", response: { success: true } }
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

    expect(await screen.findByText("Modello ONNX Cross-Encoder (Re-Ranker)")).toBeInTheDocument();
    expect(screen.getByText("Non installato (Fallback Euristico)")).toBeInTheDocument();

    const downloadButton = screen.getByRole("button", { name: "Scarica modello" });
    expect(downloadButton).toBeInTheDocument();

    fireEvent.click(downloadButton);

    expect(await screen.findByText("Download del modello ONNX Re-Ranker avviato.")).toBeInTheDocument();
    expect(api.calls).toContainEqual(
      expect.objectContaining({
        path: "/api/rag/reranker/download",
        method: "POST"
      })
    );
  });

  it("renders downloading state with progress bar and allows cancellation", async () => {
    const onDataChanged = vi.fn(async () => {});
    const api = mockApi([
      ...createStandardApiMocks(
        createRerankerModelInfo({ isDownloaded: false, isDownloading: true, downloadProgress: 0.45 })
      ),
      { path: "/api/rag/reranker/download", method: "DELETE", response: { success: true } }
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

    expect(await screen.findByText("In download (45%)")).toBeInTheDocument();

    const cancelButton = screen.getByRole("button", { name: "Annulla download" });
    expect(cancelButton).toBeInTheDocument();

    fireEvent.click(cancelButton);

    expect(await screen.findByText("Download del modello ONNX Re-Ranker annullato.")).toBeInTheDocument();
    expect(api.calls).toContainEqual(
      expect.objectContaining({
        path: "/api/rag/reranker/download",
        method: "DELETE"
      })
    );
  });

  it("renders installed state and allows model deletion", async () => {
    const onDataChanged = vi.fn(async () => {});
    const api = mockApi([
      ...createStandardApiMocks(
        createRerankerModelInfo({ isDownloaded: true, isDownloading: false, fileSizeBytes: 560000000 })
      ),
      { path: "/api/rag/reranker/model", method: "DELETE", response: { deleted: true } }
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

    expect(await screen.findByText("Installato")).toBeInTheDocument();

    const deleteButton = screen.getByRole("button", { name: "Elimina modello" });
    expect(deleteButton).toBeInTheDocument();

    fireEvent.click(deleteButton);

    expect(await screen.findByText("Modello ONNX Re-Ranker eliminato.")).toBeInTheDocument();
    expect(api.calls).toContainEqual(
      expect.objectContaining({
        path: "/api/rag/reranker/model",
        method: "DELETE"
      })
    );
  });
});
