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
    expect(screen.getByText(/Smart Router:/i)).toBeInTheDocument();
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

    const promptInput = screen.getByPlaceholderText(/Inserisci l'obiettivo o la domanda/i);
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

    const promptInput = screen.getByPlaceholderText(/Inserisci l'obiettivo o la domanda/i);
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

  it("triggers multi-agent orchestration inline when a complex task is submitted", async () => {
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
          fileCount: 5,
          lastVerifiedAt: "2026-07-24T12:00:00Z"
        }
      },
      {
        path: "/api/workspace/files",
        method: "GET",
        response: []
      },
      {
        path: "/api/agent/orchestrate",
        method: "POST",
        response: {
          orchestrationId: "orch_test_123",
          overallGoal: "Crea modulo autenticazione con test",
          isCompleted: false,
          hasFailed: false,
          subtasks: [
            {
              subtaskId: "sub_planner",
              role: "Planner Agent",
              goal: "Analisi requisiti",
              dependsOnSubtaskIds: [],
              status: "Running"
            }
          ],
          messages: [
            {
              messageId: "msg_1",
              senderRole: "Orchestrator",
              recipientRole: "Planner Agent",
              messageText: "Inizializzato flusso multi-agente",
              sentAtUtc: new Date().toISOString()
            }
          ],
          startedAtUtc: new Date().toISOString()
        }
      },
      {
        path: "/api/agent/orchestrate/orch_test_123",
        method: "GET",
        response: {
          orchestrationId: "orch_test_123",
          overallGoal: "Crea modulo autenticazione con test",
          isCompleted: true,
          hasFailed: false,
          subtasks: [
            {
              subtaskId: "sub_planner",
              role: "Planner Agent",
              goal: "Analisi requisiti",
              dependsOnSubtaskIds: [],
              status: "Completed"
            }
          ],
          messages: [
            {
              messageId: "msg_1",
              senderRole: "Orchestrator",
              recipientRole: "Planner Agent",
              messageText: "Inizializzato flusso multi-agente",
              sentAtUtc: new Date().toISOString()
            }
          ],
          startedAtUtc: new Date().toISOString()
        }
      },
      {
        path: "/api/agent/run-stream",
        method: "POST",
        response: 'data: {"type":"final_response","content":"Modulo creato!"}\n\ndata: [DONE]\n\n'
      }
    ]);

    render(
      <CodingSection
        models={[createModel({ name: "qwen2.5-coder" })]}
        defaultModel="qwen2.5-coder"
      />
    );

    const promptInput = screen.getByPlaceholderText(/Inserisci l'obiettivo o la domanda/i);
    await user.type(promptInput, "Crea modulo autenticazione con test unitari");

    const sendBtn = screen.getByRole("button", { name: /Invia \(Ctrl\+Enter\)/i });
    await user.click(sendBtn);

    await waitFor(() => {
      expect(screen.getByText("Orchestrazione Multi-Agenti")).toBeInTheDocument();
      expect(screen.getByText("orch_test_123")).toBeInTheDocument();
      expect(screen.getByText("Planner Agent")).toBeInTheDocument();
    });
  });

  it("handles dropped files and processes them into coding pipeline", async () => {
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

    const fakeFile = new File(["const x = 1;"], "test_script.js", { type: "text/javascript" });
    const droppedFiles = [fakeFile] as unknown as FileList;
    let handled = false;

    render(
      <CodingSection
        models={[createModel({ name: "qwen2.5-coder" })]}
        defaultModel="qwen2.5-coder"
        droppedFiles={droppedFiles}
        onHandledDroppedFiles={() => {
          handled = true;
        }}
      />
    );

    await waitFor(() => {
      expect(handled).toBe(true);
      expect(screen.getByText("1 file inserito/i nella pipeline Coding.")).toBeInTheDocument();
    });
  });
});
