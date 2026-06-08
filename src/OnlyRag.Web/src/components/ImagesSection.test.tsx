import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { GeneratedImage } from "../api";
import { mockApi } from "../test/apiMock";
import { ImagesSection } from "./ImagesSection";

describe("ImagesSection", () => {
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
      { path: "/api/images/providers/status", response: [createProviderStatus()] },
      { path: "/api/images", response: [] },
      {
        path: "/api/images/generate",
        method: "POST",
        response: {
          provider: "automatic1111",
          message: "Immagine generata.",
          images: [createGeneratedImage({ prompt: "Una libreria futuristica" })]
        }
      },
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
      provider: "automatic1111",
      prompt: "Una libreria futuristica",
      width: 1024,
      height: 1024,
      steps: 30,
      batchSize: 1
    });
  });

  it("validates the prompt before sending a generation request", async () => {
    mockApi([
      { path: "/api/settings/image-generation", response: createImageSettings() },
      { path: "/api/images/providers/status", response: [createProviderStatus()] },
      { path: "/api/images", response: [] }
    ]);

    render(<ImagesSection />);

    await screen.findByRole("heading", { name: "Generazione immagini" });
    fireEvent.submit(screen.getByLabelText("Prompt").closest("form")!);

    expect(screen.getByRole("alert")).toHaveTextContent("Inserisci un prompt");
  });
});

function createImageSettings() {
  return {
    provider: "automatic1111",
    automatic1111BaseUrl: "http://127.0.0.1:7860",
    comfyUiBaseUrl: "http://127.0.0.1:8188",
    requestTimeoutSeconds: 300,
    trustNonLocalEndpoint: false,
    automatic1111Model: null,
    comfyUiWorkflowJson: null
  };
}

function createProviderStatus() {
  return {
    provider: "automatic1111",
    state: "Online",
    isReachable: true,
    baseUrl: "http://127.0.0.1:7860",
    message: "Automatic1111 raggiungibile.",
    suggestion: null
  };
}

function createGeneratedImage(overrides: Partial<GeneratedImage> = {}): GeneratedImage {
  return {
    id: 1,
    provider: "automatic1111",
    prompt: "Prompt",
    negativePrompt: null,
    model: null,
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
