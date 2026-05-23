import { createRef } from "react";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { TranslationSummary } from "../api";
import { createDocument, createModel, createOllamaStatus } from "../test/fixtures";
import { TranslationListCard, TranslationStartCard } from "./TranslationSection.views";

describe("TranslationStartCard", () => {
  it("shows Italian labels while preserving backend language values", () => {
    render(
      <TranslationStartCard
        documents={[createDocument()]}
        selectedDocumentId={1}
        selectedDocument={createDocument()}
        selectedLanguage="English"
        selectedModel="llama3.2:3b"
        models={[createModel()]}
        ollamaStatus={createOllamaStatus()}
        loadError={null}
        isStarting={false}
        canStart
        onDocumentChange={vi.fn()}
        onLanguageChange={vi.fn()}
        onModelChange={vi.fn()}
        onStartTranslation={vi.fn()}
      />
    );

    expect(screen.getByRole("option", { name: "Inglese" })).toHaveValue("English");
    expect(screen.getByRole("option", { name: "Giapponese" })).toHaveValue("Japanese");
    expect(screen.queryByRole("option", { name: "English" })).not.toBeInTheDocument();
  });
});

describe("TranslationListCard", () => {
  it("exposes the empty translation state as a status", () => {
    render(
      <TranslationListCard
        translations={[]}
        selectedTranslationId={null}
        detailsPanelRef={createRef<HTMLDivElement>()}
        onSelectTranslation={vi.fn()}
        onOpenCompare={vi.fn()}
      />
    );

    expect(screen.getByRole("status")).toHaveTextContent("Nessuna traduzione per il documento selezionato.");
  });

  it("marks the selected translation details action", () => {
    render(
      <TranslationListCard
        translations={[createTranslationSummary()]}
        selectedTranslationId={1}
        detailsPanelRef={createRef<HTMLDivElement>()}
        onSelectTranslation={vi.fn()}
        onOpenCompare={vi.fn()}
      />
    );

    expect(screen.getByLabelText("Traduzioni esistenti")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Dettagli" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("Inglese")).toBeInTheDocument();
    expect(screen.queryByText("English")).not.toBeInTheDocument();
  });
});

function createTranslationSummary(): TranslationSummary {
  return {
    id: 1,
    documentId: 1,
    documentName: "manuale.pdf",
    sourceLanguage: "Italian",
    targetLanguage: "English",
    model: "llama3.2:3b",
    status: "Completed",
    jobId: null,
    unitCount: 4,
    completedUnitCount: 4,
    progressPercent: 100,
    lastError: null,
    createdAtUtc: "2026-05-21T12:00:00Z",
    updatedAtUtc: "2026-05-21T12:05:00Z"
  };
}
