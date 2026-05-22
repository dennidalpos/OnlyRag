import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { ChatSection } from "./ChatSection";
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
  });
});
