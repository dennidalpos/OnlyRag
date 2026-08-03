import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { CodingSection } from "./CodingSection";
import { mockApi } from "../../test/apiMock";
import { createModel } from "../../test/fixtures";

describe("CodingSection", () => {
  it("renders correctly with mode switch and windows folder picker", async () => {
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
    expect(screen.getByRole("button", { name: /Lettura \/ Piano/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Agente Scrittura/i })).toBeInTheDocument();
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

    const promptInput = screen.getByPlaceholderText(/Modalità AGENTE SCRITTURA/i);
    await user.type(promptInput, "Analizza il codice");

    const sendBtn = screen.getByRole("button", { name: /Invia \(Ctrl\+Enter\)/i });
    await user.click(sendBtn);

    await waitFor(() => {
      expect(screen.getAllByText(/Analizzo il progetto/i).length).toBeGreaterThan(0);
    });
  });

  it("sends prompt in chat when no project folder is set", async () => {
    const user = userEvent.setup();
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
      },
      {
        path: "/api/agent/run-stream",
        method: "POST",
        response: 'data: {"type":"final_response","content":"Risposta senza cartella!"}\n\ndata: [DONE]\n\n'
      }
    ]);

    render(
      <CodingSection
        models={[createModel({ name: "qwen2.5-coder" })]}
        defaultModel="qwen2.5-coder"
      />
    );

    const promptInput = screen.getByPlaceholderText(/Modalità AGENTE SCRITTURA/i);
    await user.type(promptInput, "Ciao senza cartella");

    const sendBtn = screen.getByRole("button", { name: /Invia \(Ctrl\+Enter\)/i });
    await user.click(sendBtn);

    await waitFor(() => {
      expect(screen.getByText("Risposta senza cartella!")).toBeInTheDocument();
    });
  });

  it("allows clearing the selected workspace folder", async () => {
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
        path: "/api/workspace/clear",
        method: "POST",
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

    expect(await screen.findByText("C:\\Projects\\App")).toBeInTheDocument();

    const removeBtn = screen.getByRole("button", { name: /Rimuovi cartella di progetto/i });
    await user.click(removeBtn);

    await waitFor(() => {
      expect(screen.queryByText("C:\\Projects\\App")).not.toBeInTheDocument();
      expect(screen.getByText("Cartella di progetto rimossa.")).toBeInTheDocument();
    });
  });
});
