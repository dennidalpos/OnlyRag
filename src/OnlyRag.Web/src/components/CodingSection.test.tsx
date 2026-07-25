import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { CodingSection } from "./CodingSection";
import { mockApi } from "../test/apiMock";
import { createModel } from "../test/fixtures";

describe("CodingSection", () => {
  it("renders correctly with audit presets, mode switch, and windows folder picker", async () => {
    mockApi([
      {
        path: "/api/workspace/config",
        method: "GET",
        response: {
          rootPath: null,
          isAuthorized: false,
          canRead: false,
          canWrite: false,
          fileCount: 0,
          lastVerifiedAt: null
        }
      }
    ]);

    render(
      <CodingSection
        models={[createModel({ name: "qwen2.5-coder" })]}
        defaultModel="qwen2.5-coder"
      />
    );

    expect(screen.getByRole("heading", { level: 2, name: /Coding & Vibe Hub/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Sfoglia Cartella/i })).toBeInTheDocument();
    expect(screen.getByText(/Audit UI\/UX/i)).toBeInTheDocument();
    expect(screen.getByText(/Audit Flussi & Logica/i)).toBeInTheDocument();
    expect(screen.getByText(/Audit Documentazione/i)).toBeInTheDocument();
    expect(screen.getByText(/Audit Build & Pacchetti/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /📖 Lettura \/ Piano/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /✍️ Scrittura/i })).toBeInTheDocument();
  });

  it("sends prompt and renders message stream in vibe chat", async () => {
    const user = userEvent.setup();
    mockApi([
      {
        path: "/api/workspace/config",
        method: "GET",
        response: {
          rootPath: "C:\\Projects\\App",
          isAuthorized: true,
          canRead: true,
          canWrite: true,
          fileCount: 10,
          lastVerifiedAt: "2026-07-24T12:00:00Z"
        }
      },
      {
        path: "/api/workspace/files",
        method: "GET",
        response: []
      },
      {
        path: "/api/coding/generate-stream",
        method: "POST",
        response: 'data: {"chunk":"public class Calculator {}"}\n\ndata: [DONE]\n\n'
      }
    ]);

    render(
      <CodingSection
        models={[createModel({ name: "qwen2.5-coder" })]}
        defaultModel="qwen2.5-coder"
      />
    );

    const modeBtn = screen.getByRole("button", { name: /✍️ Scrittura/i });
    await user.click(modeBtn);

    const promptInput = screen.getByPlaceholderText(/Modalità SCRITTURA/i);
    await user.type(promptInput, "Crea Calculator");

    const sendBtn = screen.getByRole("button", { name: /Invia \(Ctrl\+Enter\)/i });
    await user.click(sendBtn);

    await waitFor(() => {
      expect(screen.getByText(/public class Calculator/i)).toBeInTheDocument();
    });
  });

  it("sends prompt in Agent mode and renders agent thought events", async () => {
    const user = userEvent.setup();
    mockApi([
      {
        path: "/api/workspace/config",
        method: "GET",
        response: {
          rootPath: "C:\\Projects\\App",
          isAuthorized: true,
          canRead: true,
          canWrite: true,
          fileCount: 10,
          lastVerifiedAt: "2026-07-24T12:00:00Z"
        }
      },
      {
        path: "/api/workspace/files",
        method: "GET",
        response: []
      },
      {
        path: "/api/agent/run-stream",
        method: "POST",
        response: 'data: {"type":"thought","content":"[Agent Engine] Analizzo il progetto..."}\n\ndata: {"type":"thought_chunk","content":"Elaborazione in corso"}\n\ndata: {"type":"final_response","content":"Completato!"}\n\ndata: [DONE]\n\n'
      }
    ]);

    render(
      <CodingSection
        models={[createModel({ name: "qwen2.5-coder" })]}
        defaultModel="qwen2.5-coder"
      />
    );

    const promptInput = screen.getByPlaceholderText(/Modalità AGENTE AUTONOMO/i);
    await user.type(promptInput, "Analizza il codice");

    const sendBtn = screen.getByRole("button", { name: /Invia \(Ctrl\+Enter\)/i });
    await user.click(sendBtn);

    await waitFor(() => {
      expect(screen.getByText(/Analizzo il progetto/i)).toBeInTheDocument();
    });
  });
});
