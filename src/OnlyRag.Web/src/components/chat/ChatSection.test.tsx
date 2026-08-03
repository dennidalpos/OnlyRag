import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { ChatSection } from "./ChatSection";
import { chatStorageKeys, loadChatSession } from "./ChatSection.storage";
import { mockApi } from "../../test/apiMock";
import { createDocument, createModel, createOllamaStatus } from "../../test/fixtures";

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
        path: "/api/chat/stream",
        method: "POST",
        response: `data: ${JSON.stringify({
          eventType: "meta",
          conversationId: "conversation-1",
          model: "llama3.2:3b",
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
        })}\n\ndata: ${JSON.stringify({
          eventType: "chunk",
          content: "Il manuale descrive il flusso RAG locale."
        })}\n\ndata: [DONE]\n\n`
      },
      {
        path: "/api/documents/1/preview?page=1&pageSize=1",
        response: {
          documentId: 1,
          originalFileName: "manuale.pdf",
          mimeType: "application/pdf",
          fileExtension: ".pdf",
          fileSizeBytes: 2048,
          pageCount: 2,
          chunkCount: 4,
          status: "Indexed",
          pageStart: 1,
          pageSize: 1,
          returnedPageCount: 1,
          pages: [
            {
              pageNumber: 1,
              textContent: "Pagina originale della fonte",
              ocrStatus: null,
              ocrEngine: null,
              ocrConfidence: null,
              ocrError: null
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
    await userEvent.click(screen.getByText("manuale.pdf - Pagina 1"));
    await userEvent.click(screen.getByRole("button", { name: "Apri pagina" }));

    expect(await screen.findByRole("dialog", { name: "Anteprima documento" })).toBeInTheDocument();
    expect(screen.getByText("Pagina originale della fonte")).toBeInTheDocument();

    const chatCall = api.calls.find((call) => call.path === "/api/chat/stream");
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
        path: "/api/chat/stream",
        method: "POST",
        response: `data: ${JSON.stringify({
          eventType: "error",
          content: "Backend offline durante la generazione."
        })}\n\ndata: [DONE]\n\n`
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
        selectedModel: "invalid-model",
        selectedDocumentIds: ["invalid-id"],
        conversationId: "c1",
        messages: [{ invalid: true }]
      })
    );

    const session = loadChatSession();
    expect(session).toBeNull();
  });
});
