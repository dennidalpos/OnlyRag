import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
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
  createOllamaStatus
} from "./test/fixtures";

afterEach(() => {
  vi.useRealTimers();
});

describe("App initial setup", () => {
  it("shows module status badges from diagnostics in the topbar", async () => {
    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      {
        path: "/api/diagnostics",
        response: createDiagnostics({
          qdrant: {
            status: "Offline",
            isReachable: false,
            grpcEndpoint: "http://127.0.0.1:6334",
            isLoopback: true,
            isTls: false,
            hasApiKey: false,
            version: null,
            binaryPath: "C:\\OnlyRag\\qdrant\\qdrant.exe",
            configPath: "C:\\OnlyRag\\qdrant\\config\\qdrant.yaml",
            storageDirectory: "C:\\OnlyRag\\qdrant\\storage",
            processId: null,
            warning: null,
            error: "Connessione rifiutata."
          },
          ocrStatus: "Disponibile",
          ocrIsConfigured: true
        })
      },
      { path: "/api/dependencies/ocr/startup-analysis", response: createOcrStartupAnalysis() },
      { path: "/api/dependencies/ocr", response: createOcrProvisionStatus() }
    ]);

    render(<App />);

    expect(screen.getByRole("link", { name: "Salta al contenuto principale" })).toHaveAttribute(
      "href",
      "#main-workspace"
    );
    expect(screen.getByRole("main", { name: "Coding" })).toBeInTheDocument();

    const status = await screen.findByLabelText("Stato applicazione");
    await waitFor(() => {
      expect(status).toHaveTextContent("Backend");
      expect(status).toHaveTextContent("Ollama");
      expect(status).toHaveTextContent("Qdrant");
      expect(status).toHaveTextContent("Offline");
      expect(status).toHaveTextContent("OCR");
      expect(status).toHaveTextContent("Disponibile");
      expect(status).not.toHaveTextContent("OCR GPU");
      expect(status).toHaveTextContent("Operazioni");
      expect(status).toHaveTextContent("Nessuna");
    });
  });

  it("shows OCR setup in the startup wizard and starts confirmed provisioning", async () => {
    const api = mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        response: createOcrStartupAnalysis({
          shouldPrompt: true,
          isOcrConfigured: false,
          recommendedRuntimeTarget: "cpu",
          title: "OCR CPU da installare",
          message: "Prepara il runtime OCR locale."
        })
      },
      { path: "/api/dependencies/ocr", response: createOcrProvisionStatus({ isConfigured: false }) },
      {
        path: "/api/dependencies/ocr/provision",
        method: "POST",
        response: { started: true, message: "Configurazione OCR avviata." }
      }
    ]);

    render(<App />);

    expect(await screen.findByRole("region", { name: "Notifica di configurazione" })).toBeInTheDocument();
    expect(screen.getByText("OCR CPU da installare")).toBeInTheDocument();


    await userEvent.click(screen.getByRole("button", { name: "Installa OCR CPU" }));

    await waitFor(() => {
      const provisionCall = api.calls.find((call) => call.path === "/api/dependencies/ocr/provision");
      expect(provisionCall).toBeDefined();
      expect(JSON.parse(String(provisionCall?.body))).toEqual({ confirmed: true, runtimeTarget: "cpu" });
    });
  });

  it("shows a repair action for a damaged OCR runtime in the startup wizard", async () => {
    const repairMessage =
      "Runtime OCR locale incompleto o danneggiato. Apri Impostazioni > Diagnostica e premi Ripara OCR per reinstallare PaddleOCR e il runtime PaddlePaddle corretto.";
    const api = mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        response: createOcrStartupAnalysis({
          shouldPrompt: true,
          availableDiskBytes: 143.5 * 1024 * 1024 * 1024,
          requiredDiskBytes: 2 * 1024 * 1024 * 1024,
          isOcrConfigured: false,
          title: "Runtime OCR da riparare",
          message: repairMessage
        })
      },
      { path: "/api/dependencies/ocr", response: createOcrProvisionStatus({ isConfigured: false, message: repairMessage }) },
      {
        path: "/api/dependencies/ocr/provision",
        method: "POST",
        response: { started: true, message: "Configurazione OCR avviata." }
      }
    ]);

    render(<App />);

    expect(await screen.findByText("Runtime OCR da riparare")).toBeInTheDocument();
    expect(screen.getByText(/Premi Ripara OCR per reinstallare PaddleOCR/)).toBeInTheDocument();
    expect(screen.queryByText("unknown")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ripara OCR" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Ripara OCR" }));

    await waitFor(() => {
      const provisionCall = api.calls.find((call) => call.path === "/api/dependencies/ocr/provision");
      expect(provisionCall).toBeDefined();
      expect(JSON.parse(String(provisionCall?.body))).toEqual({ confirmed: true, runtimeTarget: "auto" });
    });
  });

  it("keeps Ollama download behind an explicit confirmed action", async () => {
    const api = mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus({ cliInstalled: false }) },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      { path: "/api/dependencies/ocr/startup-analysis", response: createOcrStartupAnalysis() },
      { path: "/api/dependencies/ocr", response: createOcrProvisionStatus({ resolvedRuntime: "configured" }) },
      {
        path: "/api/dependencies/ollama/install",
        method: "POST",
        response: { started: true, message: "Download Ollama aperto." }
      }
    ]);

    render(<App />);

    await userEvent.click(await screen.findByRole("button", { name: "Apri download Ollama" }));

    await waitFor(() => {
      const installCall = api.calls.find((call) => call.path === "/api/dependencies/ollama/install");
      expect(installCall).toBeDefined();
      expect(JSON.parse(String(installCall?.body))).toEqual({ confirmed: true });
    });
  });
});
