import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { ChatSection } from "./ChatSection";
import { chatStorageKeys, loadChatSession } from "./ChatSection.storage";
import { mockApi } from "../test/apiMock";
import { createDocument, createModel, createOllamaStatus } from "../test/fixtures";

describe("ChatSection", () => {
  it("restores WebView storage, sends selected document context, and persists the response", async () => {
    window.localStorage.setItem("onlyrag.chat.draft", "Riassumi il manuale");
    window.sessionStorage.setItem(
      "onlyrag.chat.session",
      JSON.stringify({
        conversationId: "conversation-1",
        messages: [],
        selectedDocumentIds: [1]
      })
    );

    const api = mockApi([
      { path: "/api/documents", response: [createDocument()] },
      {
        path: "/api/chat",
        method: "POST",
        response: {
          conversationId: "conversation-1",
          model: "llama3.2:3b",
          answer: "Il manuale descrive il flusso RAG locale.",
          usedDocuments: true,
          notices: [{ code: "limited-context", message: "Contesto limitato ai documenti selezionati." }],
          sources: [
            {
              documentId: 1,
              documentName: "manuale.pdf",
              pageStart: 1,
              pageEnd: 1,
              chunkId: 10,
              snippet: "Flusso RAG locale",
              score: 0.92
            }
          ]
        }
      }
    ]);

    render(
      <ChatSection
        models={[createModel()]}
        defaultModel="llama3.2:3b"
        ollamaStatus={createOllamaStatus()}
        loadError={null}
      />
    );

    const message = await screen.findByRole("textbox", { name: "Messaggio" });
    await waitFor(() => expect(message).toHaveValue("Riassumi il manuale"));
    expect(await screen.findByText("manuale.pdf")).toBeInTheDocument();
    expect(screen.getByRole("checkbox")).toBeChecked();

    await userEvent.click(screen.getByRole("button", { name: "Invia" }));

    expect(await screen.findByText("Il manuale descrive il flusso RAG locale.")).toBeInTheDocument();
    expect(screen.getByText("Contesto limitato ai documenti selezionati.")).toBeInTheDocument();
    expect(screen.getByText("Flusso RAG locale")).toBeInTheDocument();

    const chatCall = api.calls.find((call) => call.path === "/api/chat");
    expect(JSON.parse(String(chatCall?.body))).toMatchObject({
      message: "Riassumi il manuale",
      model: "llama3.2:3b",
      selectedDocumentIds: [1],
      conversationId: "conversation-1",
      useDocuments: true
    });
    expect(window.localStorage.getItem("onlyrag.chat.draft")).toBeNull();
    expect(window.sessionStorage.getItem("onlyrag.chat.session")).toContain("Il manuale descrive");
  });

  it("shows backend errors while keeping the stored draft intact", async () => {
    window.localStorage.setItem("onlyrag.chat.draft", "Domanda da riprovare");
    mockApi([
      { path: "/api/documents", response: [createDocument()] },
      {
        path: "/api/chat",
        method: "POST",
        status: 503,
        response: { detail: "Backend offline durante la generazione." }
      }
    ]);

    render(
      <ChatSection
        models={[createModel()]}
        defaultModel="llama3.2:3b"
        ollamaStatus={createOllamaStatus()}
        loadError={null}
      />
    );

    await screen.findByText("manuale.pdf");
    await userEvent.click(screen.getByRole("button", { name: "Invia" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Backend offline durante la generazione.");
  });

  it("ignores corrupted WebView session storage", async () => {
    window.sessionStorage.setItem("onlyrag.chat.session", "{not-valid-json");
    mockApi([{ path: "/api/documents", response: [createDocument()] }]);

    render(
      <ChatSection
        models={[createModel()]}
        defaultModel="llama3.2:3b"
        ollamaStatus={createOllamaStatus()}
        loadError={null}
      />
    );

    expect(await screen.findByText("manuale.pdf")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Messaggio" })).toHaveValue("");
    expect(screen.getByText("Inizia una conversazione.")).toBeInTheDocument();
    expect(window.sessionStorage.getItem("onlyrag.chat.session")).not.toBe("{not-valid-json");
  });

  it("clears invalid or oversized WebView chat storage", () => {
    window.sessionStorage.setItem(
      chatStorageKeys.session,
      JSON.stringify({
        conversationId: "conversation-1",
        messages: [{ id: "bad", role: "system", content: "unsafe", sources: [] }],
        selectedDocumentIds: [1],
        selectedModel: "llama3.2:3b"
      })
    );

    expect(loadChatSession()).toBeNull();
    expect(window.sessionStorage.getItem(chatStorageKeys.session)).toBeNull();

    window.localStorage.setItem(chatStorageKeys.draft, "x".repeat(16_001));
    mockApi([{ path: "/api/documents", response: [] }]);

    render(
      <ChatSection
        models={[createModel()]}
        defaultModel="llama3.2:3b"
        ollamaStatus={createOllamaStatus()}
        loadError={null}
      />
    );

    expect(screen.getByRole("textbox", { name: "Messaggio" })).toHaveValue("");
    expect(window.localStorage.getItem(chatStorageKeys.draft)).toBeNull();
  });

  it("preserves a valid chat-specific model on load and applies a changed saved default", async () => {
    window.sessionStorage.setItem(
      "onlyrag.chat.session",
      JSON.stringify({
        conversationId: null,
        messages: [],
        selectedDocumentIds: [],
        selectedModel: "mistral:7b"
      })
    );
    mockApi([{ path: "/api/documents", response: [] }]);

    const { rerender } = render(
      <ChatSection
        models={[
          createModel({ name: "llama3.2:3b", model: "llama3.2:3b" }),
          createModel({ name: "mistral:7b", model: "mistral:7b" }),
          createModel({ name: "qwen2.5:7b", model: "qwen2.5:7b" })
        ]}
        defaultModel="llama3.2:3b"
        ollamaStatus={createOllamaStatus({ installedModelCount: 3 })}
        loadError={null}
      />
    );

    const select = await screen.findByLabelText("Modello chat");
    expect(select).toHaveValue("mistral:7b");

    rerender(
      <ChatSection
        models={[
          createModel({ name: "llama3.2:3b", model: "llama3.2:3b" }),
          createModel({ name: "mistral:7b", model: "mistral:7b" }),
          createModel({ name: "qwen2.5:7b", model: "qwen2.5:7b" })
        ]}
        defaultModel="qwen2.5:7b"
        ollamaStatus={createOllamaStatus({ installedModelCount: 3 })}
        loadError={null}
      />
    );

    expect(select).toHaveValue("qwen2.5:7b");
    await waitFor(() =>
      expect(window.sessionStorage.getItem("onlyrag.chat.session")).toContain("qwen2.5:7b")
    );
  });
});
