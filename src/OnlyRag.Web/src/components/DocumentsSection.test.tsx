import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DocumentsSection } from "./DocumentsSection";
import { ActionButton, DocumentDetailCard, isOcrCandidate } from "./DocumentsSection.helpers";
import { mockApi } from "../test/apiMock";
import {
  createDocument,
  createEmbeddingStatus,
  createOcrLanguage,
  createOcrStatus,
  createPipelineStatus,
  createVectorHealth
} from "../test/fixtures";

describe("DocumentsSection", () => {
  it("imports OCR candidates through the policy dialog and opens the preview modal", async () => {
    let documents: ReturnType<typeof createDocument>[] = [];
    const api = mockApi([
      { path: "/api/diagnostics/vector-health", response: createVectorHealth() },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      {
        path: "/api/settings/ocr-processing",
        response: { language: "it", maxRetries: 2, pageTimeoutSeconds: 180, lowConfidenceThreshold: 0.55 }
      },
      { path: "/api/documents", handler: () => ({ body: documents }) },
      { path: "/api/documents/1", handler: () => ({ body: documents[0] }) },
      { path: "/api/documents/1/embedding-status", response: createEmbeddingStatus() },
      { path: "/api/documents/1/ocr-status", response: createOcrStatus() },
      { path: "/api/documents/1/pipeline-status", response: createPipelineStatus() },
      {
        path: "/api/documents/import",
        method: "POST",
        handler: () => {
          documents = [createDocument({ originalFileName: "contratto.pdf" })];
          return {
            body: {
              documents: [
                {
                  document: documents[0],
                  deduplicated: false,
                  message: "Importato."
                }
              ],
              results: [
                {
                  fileName: "contratto.pdf",
                  document: documents[0],
                  deduplicated: false,
                  succeeded: true,
                  message: "Importato.",
                  errorCode: null
                }
              ],
              hasFailures: false
            }
          };
        }
      },
      {
        path: "/api/settings/ocr-processing",
        method: "PUT",
        handler: (request) => ({ body: JSON.parse(String(request.body)) })
      },
      {
        path: "/api/documents/1/preview?page=1&pageSize=1",
        response: {
          documentId: 1,
          originalFileName: "contratto.pdf",
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
              textContent: "Testo estratto dal contratto",
              ocrStatus: "Complete",
              ocrEngine: "PaddleOCR",
              ocrConfidence: 0.94,
              ocrError: null
            }
          ]
        }
      }
    ]);

    const { container } = render(<DocumentsSection />);
    expect(await screen.findByText("Nessun documento presente. Importa un file per iniziare.")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Nessun documento presente.");

    const fileInput = container.querySelector<HTMLInputElement>("input[type='file']");
    expect(fileInput).not.toBeNull();
    await userEvent.upload(fileInput!, new File(["pdf"], "contratto.pdf", { type: "application/pdf" }));

    const dialog = await screen.findByRole("dialog", { name: "Scegli modalità OCR" });
    await userEvent.click(within(dialog).getByRole("button", { name: /Usa testo esistente/i }));

    expect(await screen.findByText("1 file importato. Analisi e indicizzazione in corso.")).toBeInTheDocument();
    expect((await screen.findAllByText("contratto.pdf")).length).toBeGreaterThan(0);

    const importCall = api.calls.find((call) => call.path === "/api/documents/import");
    expect(importCall?.body).toBeInstanceOf(FormData);
    expect((importCall?.body as FormData).get("ocrPolicy")).toBe("Auto");
    expect((importCall?.body as FormData).get("ocrLanguage")).toBe("it");
    await waitFor(() => {
      const languageSaveCall = api.calls.find(
        (call) => call.path === "/api/settings/ocr-processing" && call.method === "PUT"
      );
      expect(JSON.parse(String(languageSaveCall?.body))).toMatchObject({ language: "it" });
    });

    await userEvent.click(screen.getByRole("button", { name: "Anteprima" }));
    expect(await screen.findByRole("dialog", { name: "Anteprima documento" })).toBeInTheDocument();
    expect(screen.getByText((_content, element) => element?.textContent === "Stato: Pronto")).toBeInTheDocument();
    expect(await screen.findByText("Testo estratto dal contratto")).toBeInTheDocument();
  });

  it("shows OCR languages as friendly names with technical codes", async () => {
    mockApi([
      { path: "/api/diagnostics/vector-health", response: createVectorHealth() },
      {
        path: "/api/ocr/languages",
        response: [
          createOcrLanguage({ code: "it", label: "Italiano", isDefault: true }),
          createOcrLanguage({ code: "ku", label: "Curdo", scriptGroup: "Avanzate", isDefault: false })
        ]
      },
      {
        path: "/api/settings/ocr-processing",
        response: { language: "it", maxRetries: 2, pageTimeoutSeconds: 180, lowConfidenceThreshold: 0.55 }
      },
      { path: "/api/documents", response: [] }
    ]);

    const { container } = render(<DocumentsSection />);
    await screen.findByText("Nessun documento presente. Importa un file per iniziare.");

    const fileInput = container.querySelector<HTMLInputElement>("input[type='file']");
    await userEvent.upload(fileInput!, new File(["pdf"], "scansione.pdf", { type: "application/pdf" }));

    const dialog = await screen.findByRole("dialog", { name: "Scegli modalità OCR" });
    expect(within(dialog).getByRole("option", { name: "Italiano (it)" })).toBeInTheDocument();
    expect(within(dialog).getByRole("option", { name: "Curdo (ku)" })).toBeInTheDocument();
    expect(within(dialog).queryByRole("option", { name: "it - Italiano" })).not.toBeInTheDocument();
  });

  it("surfaces import failures as an error state", async () => {
    mockApi([
      { path: "/api/diagnostics/vector-health", response: createVectorHealth() },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      { path: "/api/documents", response: [] },
      {
        path: "/api/documents/import",
        method: "POST",
        status: 500,
        response: { detail: "Import documento non riuscito dal backend." }
      }
    ]);

    const { container } = render(<DocumentsSection />);
    await screen.findByText("Nessun documento presente. Importa un file per iniziare.");

    const fileInput = container.querySelector<HTMLInputElement>("input[type='file']");
    await userEvent.upload(fileInput!, new File(["text"], "note.txt", { type: "text/plain" }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent("Import documento non riuscito dal backend.");
    });
  });

  it("summarizes partial import failures without dropping successful files", async () => {
    let documents: ReturnType<typeof createDocument>[] = [];
    mockApi([
      { path: "/api/diagnostics/vector-health", response: createVectorHealth() },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      { path: "/api/documents", handler: () => ({ body: documents }) },
      { path: "/api/documents/1", handler: () => ({ body: documents[0] }) },
      { path: "/api/documents/1/embedding-status", response: createEmbeddingStatus() },
      { path: "/api/documents/1/ocr-status", response: createOcrStatus() },
      { path: "/api/documents/1/pipeline-status", response: createPipelineStatus() },
      {
        path: "/api/documents/import",
        method: "POST",
        handler: () => {
          documents = [createDocument({ originalFileName: "ok.txt", fileExtension: ".txt" })];
          return {
            body: {
              documents: [{ document: documents[0], deduplicated: false, message: "Importato." }],
              results: [
                {
                  fileName: "ok.txt",
                  document: documents[0],
                  deduplicated: false,
                  succeeded: true,
                  message: "Importato.",
                  errorCode: null
                },
                {
                  fileName: "bad.json",
                  document: null,
                  deduplicated: false,
                  succeeded: false,
                  message: "Formato non supportato.",
                  errorCode: "document_import_invalid"
                }
              ],
              hasFailures: true
            }
          };
        }
      }
    ]);

    const { container } = render(<DocumentsSection />);
    await screen.findByText("Nessun documento presente. Importa un file per iniziare.");

    const fileInput = container.querySelector<HTMLInputElement>("input[type='file']");
    await userEvent.upload(fileInput!, [
      new File(["ok"], "ok.txt", { type: "text/plain" }),
      new File(["bad"], "bad.json", { type: "application/json" })
    ]);

    expect(await screen.findByText(/1 file importato.*1 file non importato: bad\.json/)).toBeInTheDocument();
    expect((await screen.findAllByText("ok.txt")).length).toBeGreaterThan(0);
  });

  it("surfaces repeated polling failures while keeping the last successful document state", async () => {
    vi.useFakeTimers();
    let documentReads = 0;
    try {
      mockApi([
        { path: "/api/diagnostics/vector-health", response: createVectorHealth() },
        { path: "/api/ocr/languages", response: [createOcrLanguage()] },
        {
          path: "/api/documents",
          handler: async () => {
            documentReads += 1;
            if (documentReads > 1) {
              throw new TypeError("offline");
            }

            return { body: [createDocument()] };
          }
        },
        { path: "/api/documents/1/embedding-status", response: createEmbeddingStatus() },
        { path: "/api/documents/1/ocr-status", response: createOcrStatus() },
        { path: "/api/documents/1/pipeline-status", response: createPipelineStatus() }
      ]);

      render(<DocumentsSection />);

      await act(async () => {
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(screen.getAllByText("manuale.pdf").length).toBeGreaterThan(0);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(10_000);
      });

      expect(screen.getAllByRole("alert").some((alert) => alert.textContent?.includes("Stato non aggiornato"))).toBe(true);
      expect(screen.getAllByText("manuale.pdf").length).toBeGreaterThan(0);
    } finally {
      vi.useRealTimers();
    }
  });

  it("does not keep a stale selected detail when polling no longer returns the document", async () => {
    vi.useFakeTimers();
    let documents = [createDocument()];
    try {
      mockApi([
        { path: "/api/diagnostics/vector-health", response: createVectorHealth() },
        { path: "/api/ocr/languages", response: [createOcrLanguage()] },
        {
          path: "/api/documents",
          handler: () => ({ body: documents })
        },
        { path: "/api/documents/1/embedding-status", response: createEmbeddingStatus() },
        { path: "/api/documents/1/ocr-status", response: createOcrStatus() },
        { path: "/api/documents/1/pipeline-status", response: createPipelineStatus() }
      ]);

      render(<DocumentsSection />);
      await act(async () => {
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(screen.getAllByText("manuale.pdf").length).toBeGreaterThan(0);

      documents = [];
      await act(async () => {
        await vi.advanceTimersByTimeAsync(5_000);
      });

      expect(screen.getByText("Nessun documento presente. Importa un file per iniziare.")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Elimina" })).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it("connects document action tooltips to the action accessible description", () => {
    render(
      <ActionButton
        label="Ricostruisci indice"
        tooltip="Ricrea testo, chunk e indice del documento."
        disabled={false}
        variant="recovery"
        onClick={() => {}}
      />
    );

    expect(screen.getByRole("button", { name: "Ricostruisci indice" })).toHaveAccessibleDescription(
      "Ricrea testo, chunk e indice del documento."
    );
  });

  it("announces persisted document detail errors", () => {
    render(
      <DocumentDetailCard
        document={createDocument({ lastError: "OCR non completato sulla pagina 2." })}
        pipelineStatus={null}
        embeddingStatus={null}
        ocrStatus={null}
        activeJob={null}
        isBusy={false}
        canPreview={false}
        onReindex={vi.fn()}
        onEmbed={vi.fn()}
        onOcr={vi.fn()}
        onDelete={vi.fn()}
        onPreview={vi.fn()}
      />
    );

    expect(screen.getByRole("alert")).toHaveTextContent("OCR non completato sulla pagina 2.");
  });

  it("matches supported image extensions for document OCR actions", () => {
    for (const fileExtension of [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif", ".webp"]) {
      expect(isOcrCandidate(createDocument({ fileExtension }))).toBe(true);
    }

    expect(isOcrCandidate(createDocument({ fileExtension: ".txt" }))).toBe(false);
  });
});
