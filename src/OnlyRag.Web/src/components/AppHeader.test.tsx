import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { BackendStatus } from "../App";
import { createDiagnostics } from "../test/fixtures";
import { AppHeader } from "./AppHeader";

describe("AppHeader", () => {
  it("keeps the updating clock outside the application status live region", () => {
    const { container } = render(
      <AppHeader
        currentSection="Chat"
        backendStatus={createBackendStatus()}
        diagnostics={createDiagnostics()}
      />
    );

    const status = screen.getByRole("status", { name: "Stato applicazione" });

    expect(status).toHaveTextContent("Backend");
    expect(status).toHaveTextContent("Ollama");
    expect(status).toHaveTextContent("Operazioni");
    expect(status).not.toHaveTextContent("Ora");
    expect(screen.getByTitle(/Ora corrente/)).toBeInTheDocument();
    expect(container.querySelector(".status-row")).toHaveTextContent(/Operazioni.*Ora/);
  });
});

function createBackendStatus(): BackendStatus {
  return {
    backendValue: "Ready",
    backendTone: "online",
    ollamaValue: "2 modelli",
    ollamaTone: "online",
    jobsValue: "0",
    jobsTone: "online",
    lowResourceMode: false,
    refreshStatus: {
      lastSuccessfulRefreshAt: "2026-05-25T10:00:00Z",
      consecutiveFailureCount: 0,
      lastErrorMessage: null
    }
  };
}
