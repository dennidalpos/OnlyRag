import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { LogViewerModal } from "./LogViewerModal";
import { mockApi } from "../../test/apiMock";

describe("LogViewerModal", () => {
  it("renders modal via portal directly in document.body and displays log entries", async () => {
    mockApi([
      {
        path: "/api/logs?limit=300",
        response: [
          {
            id: "log_1",
            timestampUtc: "2026-08-01T12:00:00Z",
            level: "Information",
            category: "App",
            message: "Applicazione avviata correttamente."
          },
          {
            id: "log_2",
            timestampUtc: "2026-08-01T12:01:00Z",
            level: "Error",
            category: "Ollama",
            message: "Errore di connessione al servizio Ollama.",
            exceptionDetails: "System.Net.Http.HttpRequestException: Connection refused"
          }
        ]
      }
    ]);

    const onClose = vi.fn();
    render(<LogViewerModal onClose={onClose} />);

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Visualizzatore Log Live & Diagnostica")).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText("Applicazione avviata correttamente.")).toBeInTheDocument();
      expect(screen.getByText("Errore di connessione al servizio Ollama.")).toBeInTheDocument();
    });

    expect(screen.getByText("1 Errori")).toBeInTheDocument();
  });

  it("closes when pressing Escape key", async () => {
    mockApi([
      { path: "/api/logs?limit=300", response: [] }
    ]);

    const onClose = vi.fn();
    render(<LogViewerModal onClose={onClose} />);

    fireEvent.keyDown(window, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("allows filtering logs by severity level", async () => {
    mockApi([
      { path: "/api/logs?limit=300", response: [] },
      {
        path: "/api/logs?minLevel=Error&limit=300",
        response: [
          {
            id: "err_1",
            timestampUtc: "2026-08-01T12:05:00Z",
            level: "Error",
            category: "AgentEngine",
            message: "Errore critico durante l'esecuzione del tool."
          }
        ]
      }
    ]);

    const user = userEvent.setup();
    render(<LogViewerModal onClose={vi.fn()} />);

    const select = screen.getByRole("combobox");
    await user.selectOptions(select, "Error");

    await waitFor(() => {
      expect(screen.getByText("Errore critico durante l'esecuzione del tool.")).toBeInTheDocument();
    });
  });
});
