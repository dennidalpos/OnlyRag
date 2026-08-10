import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import type { BackendStatus } from "../../App";
import { ThemeProvider } from "../../context/ThemeContext";
import { createDiagnostics } from "../../test/fixtures";
import { AppHeader } from "./AppHeader";

describe("AppHeader", () => {
  it("keeps the updating clock outside the application status live region", () => {
    const { container } = render(
      <ThemeProvider>
        <AppHeader
          currentSection="Chat"
          backendStatus={createBackendStatus()}
          diagnostics={createDiagnostics()}
        />
      </ThemeProvider>
    );

    const status = screen.getByRole("status", { name: "Stato applicazione" });

    expect(status).toHaveTextContent("Backend");
    expect(status).toHaveTextContent("Ollama");
    expect(status).toHaveTextContent("Immagini");
    expect(status).toHaveTextContent("Operazioni");
    expect(status).not.toHaveTextContent("Ora");
    expect(screen.getByTitle(/Ora corrente/)).toBeInTheDocument();
    expect(container.querySelector(".status-row__operations")).toHaveTextContent(/Operazioni.*Ora/);
  });

  it("opens theme switcher menu and changes active theme", async () => {
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <AppHeader
          currentSection="Chat"
          backendStatus={createBackendStatus()}
          diagnostics={createDiagnostics()}
        />
      </ThemeProvider>
    );

    const themeButton = screen.getByRole("button", { name: "Cambia tema visivo" });
    await user.click(themeButton);

    expect(screen.getByText("Seleziona Tema Visivo")).toBeInTheDocument();
    const cyberOption = screen.getByRole("button", { name: /Cyberpunk Neon/ });
    await user.click(cyberOption);

    expect(document.documentElement.getAttribute("data-theme")).toBe("cyber");
  });

  it("shows detailed module failures and probe durations in the health monitor", async () => {
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <AppHeader
          currentSection="Chat"
          backendStatus={createBackendStatus()}
          diagnostics={createDiagnostics({
            modules: [
              { module: "Qdrant", state: "timeout", durationMs: 2000, error: "Timeout dopo 2 s." },
              { module: "Ollama", state: "online", durationMs: 42, error: null }
            ]
          })}
        />
      </ThemeProvider>
    );

    await user.click(screen.getByRole("button", { name: "Stato sistema" }));

    expect(screen.getByText("Timeout")).toBeInTheDocument();
    expect(screen.getByText("2000 ms")).toBeInTheDocument();
    expect(screen.getAllByText("Attivo").length).toBeGreaterThan(0);
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
