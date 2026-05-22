import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DocumentsSection } from "./DocumentsSection";
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
              ]
            }
          };
        }
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

    const fileInput = container.querySelector<HTMLInputElement>("input[type='file']");
    expect(fileInput).not.toBeNull();
    await userEvent.upload(fileInput!, new File(["pdf"], "contratto.pdf", { type: "application/pdf" }));

    const dialog = await screen.findByRole("dialog", { name: "Scegli modalità OCR" });
    await userEvent.click(within(dialog).getByRole("button", { name: /Usa testo esistente/i }));

    expect(await screen.findByText("1 file importati. Analisi e indicizzazione in corso.")).toBeInTheDocument();
    expect((await screen.findAllByText("contratto.pdf")).length).toBeGreaterThan(0);

    const importCall = api.calls.find((call) => call.path === "/api/documents/import");
    expect(importCall?.body).toBeInstanceOf(FormData);
    expect((importCall?.body as FormData).get("ocrPolicy")).toBe("Auto");
    expect((importCall?.body as FormData).get("ocrLanguage")).toBe("it");

    await userEvent.click(screen.getByRole("button", { name: "Anteprima" }));
    expect(await screen.findByRole("dialog", { name: "Anteprima documento" })).toBeInTheDocument();
    expect(await screen.findByText("Testo estratto dal contratto")).toBeInTheDocument();
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
});
