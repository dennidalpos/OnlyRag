import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
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

    await screen.findByRole("heading", { name: "Crea immagine" });

    await userEvent.click(screen.getByRole("button", { name: "Impostazioni" }));
    expect(screen.getAllByText("LCM SDXL Olive ONNX").length).toBeGreaterThan(0);
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

    await screen.findByRole("heading", { name: "Crea immagine" });
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
        path: "/api/images/models/lcm-sdxl-olive-onnx/download",
        method: "POST",
        response: {
          modelId: "lcm-sdxl-olive-onnx",
          state: "Verified",
          message: "Modello immagini scaricato e verificato."
        }
      },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: true, state: "Ready" }) },
      { path: "/api/images/models", response: [createModelState({ isVerified: true })] }
    ]);

    render(<ImagesSection />);

    await screen.findByRole("heading", { name: "Crea immagine" });
    await userEvent.click(screen.getByRole("button", { name: "Impostazioni" }));
    await userEvent.click(screen.getByRole("button", { name: "Scarica modello" }));
    expect(screen.getByRole("dialog", { name: "Conferma download modello" })).toHaveTextContent("Licenza");
    await userEvent.click(screen.getByRole("button", { name: "Conferma e scarica" }));

    expect(await screen.findByText("Modello immagini scaricato e verificato.")).toBeInTheDocument();
    const downloadCall = api.calls.find((call) => call.path.endsWith("/download"));
    expect(JSON.parse(String(downloadCall?.body))).toEqual({ consentConfirmed: true });
  });

  it("shows progress while a model download is running", async () => {
    let finishDownload: () => void = () => {};
    mockApi([
      { path: "/api/settings/image-generation", response: createImageSettings() },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: false, state: "NotDownloaded" }) },
      { path: "/api/images/models/catalog", response: [createCatalogEntry()] },
      { path: "/api/images/models", response: [createModelState({ isVerified: false })] },
      { path: "/api/images", response: [] },
      {
        path: "/api/images/models/lcm-sdxl-olive-onnx/download",
        method: "POST",
        handler: async () => {
          await new Promise<void>((resolve) => {
            finishDownload = resolve;
          });
          return {
            body: {
              modelId: "lcm-sdxl-olive-onnx",
              state: "Downloaded",
              message: "Modello immagini scaricato. Inserisci lo SHA256 per abilitarne la verifica."
            }
          };
        }
      },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: false, state: "Downloaded" }) },
      { path: "/api/images/models", response: [createModelState({ isVerified: false, isDownloaded: true, state: "Downloaded" })] }
    ]);

    render(<ImagesSection />);

    await screen.findByRole("heading", { name: "Crea immagine" });
    await userEvent.click(screen.getByRole("button", { name: "Impostazioni" }));
    await userEvent.click(screen.getByRole("button", { name: "Scarica modello" }));
    await userEvent.click(screen.getByRole("button", { name: "Conferma e scarica" }));

    expect(screen.getByRole("progressbar", { name: "Download modello in corso..." })).toBeInTheDocument();
    expect(screen.getByText("Download modello in corso...")).toBeInTheDocument();

    finishDownload();
    expect(await screen.findByText("Modello immagini scaricato. Inserisci lo SHA256 per abilitarne la verifica.")).toBeInTheDocument();
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

    await screen.findByRole("heading", { name: "Crea immagine" });
    await userEvent.type(screen.getByLabelText("Prompt"), "Una libreria futuristica");
    await userEvent.click(screen.getByRole("button", { name: "Genera" }));

    expect(await screen.findByText("Immagine generata.")).toBeInTheDocument();
    expect(screen.getByAltText("Una libreria futuristica")).toBeInTheDocument();
    await waitFor(() => expect(createObjectUrl).toHaveBeenCalled());

    const generateCall = api.calls.find((call) => call.path === "/api/images/generate");
    expect(generateCall).toBeDefined();
    expect(JSON.parse(String(generateCall?.body))).toMatchObject({
      prompt: "Una libreria futuristica",
      modelId: "lcm-sdxl-olive-onnx",
      width: 1024,
      height: 1024,
      steps: 6,
      batchSize: 1
    });
  });

  it("opens the generated images folder from the editor", async () => {
    const api = mockApi([
      { path: "/api/settings/image-generation", response: createImageSettings() },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: true, state: "Ready" }) },
      { path: "/api/images/models/catalog", response: [createCatalogEntry()] },
      { path: "/api/images/models", response: [createModelState({ isVerified: true })] },
      { path: "/api/images", response: [] },
      {
        path: "/api/images/open-folder",
        method: "POST",
        response: { message: "Cartella immagini generate aperta." }
      }
    ]);

    render(<ImagesSection />);

    await screen.findByRole("heading", { name: "Crea immagine" });
    await userEvent.click(screen.getByRole("button", { name: "Apri cartella" }));

    expect(await screen.findByText("Cartella immagini generate aperta.")).toBeInTheDocument();
    const openCall = api.calls.find((call) => call.path === "/api/images/open-folder");
    expect(JSON.parse(String(openCall?.body))).toEqual({ confirmed: true });
  });

  it("deletes a generated image from the editor", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    Object.defineProperty(URL, "createObjectURL", {
      configurable: true,
      value: vi.fn(() => "blob:onlyrag-image")
    });
    Object.defineProperty(URL, "revokeObjectURL", {
      configurable: true,
      value: vi.fn()
    });
    mockApi([
      { path: "/api/settings/image-generation", response: createImageSettings() },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: true, state: "Ready" }) },
      { path: "/api/images/models/catalog", response: [createCatalogEntry()] },
      { path: "/api/images/models", response: [createModelState({ isVerified: true })] },
      { path: "/api/images", response: [createGeneratedImage({ prompt: "Prompt lungo non troncato" })] },
      { path: "/api/images/1/file", response: "image-bytes" },
      { path: "/api/images/1", method: "DELETE", response: createGeneratedImage() }
    ]);

    render(<ImagesSection />);

    await screen.findByRole("heading", { name: "Crea immagine" });
    await screen.findAllByText("image.png");
    await userEvent.click(screen.getByRole("button", { name: "Elimina" }));

    expect(await screen.findByText("Immagine eliminata.")).toBeInTheDocument();
    expect(screen.getByText("Nessuna immagine generata.")).toBeInTheDocument();
  });

  it("keeps image settings in a modal with remaining model size", async () => {
    mockApi([
      { path: "/api/settings/image-generation", response: createImageSettings() },
      { path: "/api/images/runtime/status", response: createRuntimeStatus({ isReady: false, state: "NotDownloaded" }) },
      { path: "/api/images/models/catalog", response: [createCatalogEntry({ expectedSizeBytes: 10_000_000 })] },
      { path: "/api/images/models", response: [createModelState({ isVerified: false, expectedSizeBytes: 10_000_000, remainingDownloadBytes: 10_000_000 })] },
      { path: "/api/images", response: [] }
    ]);

    render(<ImagesSection />);

    await screen.findByRole("heading", { name: "Crea immagine" });
    await userEvent.click(screen.getByRole("button", { name: "Impostazioni" }));
    const dialog = screen.getByRole("dialog", { name: "Impostazioni immagini" });

    expect(within(dialog).getByLabelText("Modello integrato")).toBeInTheDocument();
    expect(within(dialog).getByText(/rimanenti/i)).toBeInTheDocument();
  });
});

function createImageSettings() {
  return {
    selectedModelId: "lcm-sdxl-olive-onnx",
    requestTimeoutSeconds: 300,
    preferGpu: true
  };
}

function createRuntimeStatus(overrides: Partial<{ isReady: boolean; state: string }> = {}) {
  return {
    state: overrides.state ?? "Ready",
    isReady: overrides.isReady ?? true,
    executionProvider: "DirectML",
    message: "Provider integrato pronto con DirectML.",
    suggestion: null,
    preferredExecutionProvider: "DirectML",
    modelState: overrides.state ?? "Verified",
    fallbackReason: null
  };
}

function createCatalogEntry(overrides: Partial<ReturnType<typeof createCatalogEntryBase>> = {}) {
  return {
    ...createCatalogEntryBase(),
    ...overrides
  };
}

function createCatalogEntryBase() {
  return {
    id: "lcm-sdxl-olive-onnx",
    displayName: "LCM SDXL Olive ONNX",
    recommendedProfile: "Profilo ONNX DirectML/CPU locale per qualita, bilanciato e performance.",
    modelType: "SDXL Turbo/LCM ONNX",
    modelProfile: "lcm-sdxl-olive",
    supportedResolutions: ["1024x1024", "832x1216", "1216x832"],
    defaultSteps: 6,
    defaultGuidance: 0,
    scheduler: "Euler Ancestral with trailing timestep spacing",
    compatibilityNotes: "DirectML GPU preferred; CPU fallback is supported.",
    downloadUrl: "https://example.test/model.onnx",
    licenseLabel: "OpenRAIL++",
    expectedSizeBytes: 46,
    requiredFiles: ["model.onnx"],
    sha256: "41300f6070c3a7152cc4b92b93c3aee5a868f95e4711973d60060a123074496b",
    isBuiltIn: true
  };
}

function createModelState(overrides: Partial<ImageModelLocalState> = {}): ImageModelLocalState {
  return {
    modelId: "lcm-sdxl-olive-onnx",
    state: overrides.isVerified ? "Verified" : "NotDownloaded",
    isDownloaded: overrides.isVerified ?? false,
    isVerified: false,
    localSizeBytes: 0,
    localDirectory: "C:\\Users\\User\\AppData\\Local\\OnlyRag\\models\\images\\lcm-sdxl-olive-onnx",
    verificationError: "Il modello non e ancora stato scaricato.",
    expectedSizeBytes: 46,
    remainingDownloadBytes: 46,
    ...overrides
  };
}

function createGeneratedImage(overrides: Partial<GeneratedImage> = {}): GeneratedImage {
  return {
    id: 1,
    provider: "integrated",
    prompt: "Prompt",
    negativePrompt: null,
    model: "lcm-sdxl-olive-onnx",
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
