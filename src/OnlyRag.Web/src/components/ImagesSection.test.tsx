import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { GeneratedImage, ImageModelLocalState } from "../api";
import { mockApi } from "../test/apiMock";
import { ImagesSection } from "./ImagesSection";

describe("ImagesSection", () => {
  it("hides external provider controls and shows integrated model readiness", async () => {
    mockApi([
      { path: "/api/settings/image-generation", response: createImageSettings() },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: false, state: "NotDownloaded" }) },
      { path: "/api/images/models/catalog", response: [createCatalogEntry()] },
      { path: "/api/images/models", response: [createModelState({ isVerified: false })] },
      { path: "/api/images", response: [] }
    ]);

    render(<ImagesSection />);

    await screen.findByRole("heading", { name: "Generazione immagini" });

    expect(screen.getAllByText("OnlyRag SDXL Turbo").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Scarica modello" })).toBeInTheDocument();
    expect(screen.queryByText(/Automatic1111/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/ComfyUI/i)).not.toBeInTheDocument();
  });

  it("requires a verified model before generation", async () => {
    mockApi([
      { path: "/api/settings/image-generation", response: createImageSettings() },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: false, state: "NotDownloaded" }) },
      { path: "/api/images/models/catalog", response: [createCatalogEntry()] },
      { path: "/api/images/models", response: [createModelState({ isVerified: false })] },
      { path: "/api/images", response: [] }
    ]);

    render(<ImagesSection />);

    await screen.findByRole("heading", { name: "Generazione immagini" });
    await userEvent.type(screen.getByLabelText("Prompt"), "Una libreria futuristica");

    expect(screen.getByRole("button", { name: "Genera" })).toBeDisabled();
    fireEvent.submit(screen.getByLabelText("Prompt").closest("form")!);
    expect(screen.getByRole("alert")).toHaveTextContent("Scarica e verifica il modello");
  });

  it("asks explicit consent before downloading a model", async () => {
    const api = mockApi([
      { path: "/api/settings/image-generation", response: createImageSettings() },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: false, state: "NotDownloaded" }) },
      { path: "/api/images/models/catalog", response: [createCatalogEntry()] },
      { path: "/api/images/models", response: [createModelState({ isVerified: false })] },
      { path: "/api/images", response: [] },
      {
        path: "/api/images/models/onlyrag-sdxl-turbo-directml/download",
        method: "POST",
        response: {
          modelId: "onlyrag-sdxl-turbo-directml",
          state: "Verified",
          message: "Modello immagini scaricato e verificato."
        }
      },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: true, state: "Ready" }) },
      { path: "/api/images/models", response: [createModelState({ isVerified: true })] }
    ]);

    render(<ImagesSection />);

    await screen.findByRole("heading", { name: "Generazione immagini" });
    await userEvent.click(screen.getByRole("button", { name: "Scarica modello" }));
    expect(screen.getByRole("dialog", { name: "Conferma download modello" })).toHaveTextContent("Licenza");
    await userEvent.click(screen.getByRole("button", { name: "Conferma e scarica" }));

    expect(await screen.findByText("Modello immagini scaricato e verificato.")).toBeInTheDocument();
    const downloadCall = api.calls.find((call) => call.path.endsWith("/download"));
    expect(JSON.parse(String(downloadCall?.body))).toEqual({ consentConfirmed: true });
  });

  it("generates an image and shows it in the gallery", async () => {
    const createObjectUrl = vi.fn(() => "blob:onlyrag-image");
    const revokeObjectUrl = vi.fn();
    Object.defineProperty(URL, "createObjectURL", {
      configurable: true,
      value: createObjectUrl
    });
    Object.defineProperty(URL, "revokeObjectURL", {
      configurable: true,
      value: revokeObjectUrl
    });
    const api = mockApi([
      { path: "/api/settings/image-generation", response: createImageSettings() },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: true, state: "Ready" }) },
      { path: "/api/images/models/catalog", response: [createCatalogEntry()] },
      { path: "/api/images/models", response: [createModelState({ isVerified: true })] },
      { path: "/api/images", response: [] },
      {
        path: "/api/images/generate",
        method: "POST",
        response: {
          provider: "integrated",
          message: "Immagine generata.",
          images: [createGeneratedImage({ prompt: "Una libreria futuristica" })]
        }
      },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: true, state: "Ready" }) },
      { path: "/api/images/models", response: [createModelState({ isVerified: true })] },
      { path: "/api/images/1/file", response: "image-bytes" }
    ]);

    render(<ImagesSection />);

    await screen.findByRole("heading", { name: "Generazione immagini" });
    await userEvent.type(screen.getByLabelText("Prompt"), "Una libreria futuristica");
    await userEvent.click(screen.getByRole("button", { name: "Genera" }));

    expect(await screen.findByText("Immagine generata.")).toBeInTheDocument();
    expect(screen.getByAltText("Una libreria futuristica")).toBeInTheDocument();
    await waitFor(() => expect(createObjectUrl).toHaveBeenCalled());

    const generateCall = api.calls.find((call) => call.path === "/api/images/generate");
    expect(generateCall).toBeDefined();
    expect(JSON.parse(String(generateCall?.body))).toMatchObject({
      prompt: "Una libreria futuristica",
      modelId: "onlyrag-sdxl-turbo-directml",
      width: 1024,
      height: 1024,
      steps: 30,
      batchSize: 1
    });
  });
});

function createImageSettings() {
  return {
    selectedModelId: "onlyrag-sdxl-turbo-directml",
    requestTimeoutSeconds: 300,
    preferGpu: true,
    activeExecutionProvider: "CPU"
  };
}

function createRuntimeStatus(overrides: Partial<{ isReady: boolean; state: string }> = {}) {
  return {
    state: overrides.state ?? "Ready",
    isReady: overrides.isReady ?? true,
    executionProvider: "CPU",
    message: "Provider integrato pronto con CPU.",
    suggestion: null
  };
}

function createCatalogEntry() {
  return {
    id: "onlyrag-sdxl-turbo-directml",
    displayName: "OnlyRag SDXL Turbo",
    recommendedProfile: "DirectML GPU consigliato, CPU disponibile per fallback",
    downloadUrl: "https://example.test/model.onnx",
    licenseLabel: "OpenRAIL++",
    expectedSizeBytes: 46,
    requiredFiles: ["model.onnx"],
    sha256: "41300f6070c3a7152cc4b92b93c3aee5a868f95e4711973d60060a123074496b"
  };
}

function createModelState(overrides: Partial<ImageModelLocalState> = {}): ImageModelLocalState {
  return {
    modelId: "onlyrag-sdxl-turbo-directml",
    state: overrides.isVerified ? "Verified" : "NotDownloaded",
    isDownloaded: overrides.isVerified ?? false,
    isVerified: false,
    localSizeBytes: 0,
    localDirectory: "C:\\Users\\User\\AppData\\Local\\OnlyRag\\models\\images\\onlyrag-sdxl-turbo-directml",
    verificationError: "Il modello non e ancora stato scaricato.",
    ...overrides
  };
}

function createGeneratedImage(overrides: Partial<GeneratedImage> = {}): GeneratedImage {
  return {
    id: 1,
    provider: "integrated",
    prompt: "Prompt",
    negativePrompt: null,
    model: "onlyrag-sdxl-turbo-directml",
    width: 1024,
    height: 1024,
    steps: 30,
    batchSize: 1,
    seed: null,
    fileName: "image.png",
    mimeType: "image/png",
    fileSizeBytes: 8,
    createdAtUtc: "2026-06-09T10:00:00Z",
    ...overrides
  };
}
