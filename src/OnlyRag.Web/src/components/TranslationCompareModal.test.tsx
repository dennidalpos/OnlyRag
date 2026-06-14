import { createRef, type ComponentProps } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { TranslationCompare, TranslationSummary, TranslationUnit } from "../api";
import { TranslationCompareModal } from "./TranslationCompareModal";

describe("TranslationCompareModal", () => {
  it("exposes the compare dialog and loading state to assistive technology", () => {
    renderCompareModal({
      compareData: null,
      isCompareLoading: true
    });

    expect(screen.getByRole("dialog", { name: "Confronto traduzione" })).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Caricamento confronto...");
  });

  it("marks the active unit and announces save feedback", () => {
    renderCompareModal({
      compareData: createCompare(),
      activeCompareUnit: createUnit(),
      activeCompareUnitId: 10,
      editedTranslationText: "Corrected text",
      saveState: { tone: "info", message: "Correzione salvata." }
    });

    expect(screen.getByRole("button", { name: "Pagina 1 - Paragrafo 1" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("status")).toHaveTextContent("Correzione salvata.");
  });

  it("announces compare save errors assertively", () => {
    renderCompareModal({
      compareData: createCompare(),
      activeCompareUnit: createUnit(),
      activeCompareUnitId: 10,
      saveState: { tone: "error", message: "Salvataggio non riuscito." }
    });

    expect(screen.getByRole("alert")).toHaveTextContent("Salvataggio non riuscito.");
  });

  it("can maximize and restore the compare window", async () => {
    renderCompareModal();
    const dialog = screen.getByRole("dialog", { name: "Confronto traduzione" });

    await userEvent.click(screen.getByRole("button", { name: "Massimizza" }));
    expect(dialog).toHaveClass("modal-frame--maximized");

    await userEvent.click(screen.getByRole("button", { name: "Ripristina" }));
    expect(dialog).not.toHaveClass("modal-frame--maximized");
  });
});

function renderCompareModal(overrides: Partial<ComponentProps<typeof TranslationCompareModal>> = {}) {
  const props: ComponentProps<typeof TranslationCompareModal> = {
    compareDialogRef: createRef<HTMLDivElement>(),
    compareData: createCompare(),
    activeCompareUnit: createUnit(),
    activeCompareUnitId: 10,
    editedTranslationText: "Translated text",
    isCompareLoading: false,
    saveState: null,
    onClose: vi.fn(),
    onSaveCorrection: vi.fn(),
    onComparePageChange: vi.fn(),
    onActiveUnitChange: vi.fn(),
    onEditedTextChange: vi.fn(),
    ...overrides
  };

  return render(<TranslationCompareModal {...props} />);
}

function createSummary(): TranslationSummary {
  return {
    id: 1,
    documentId: 1,
    documentName: "manuale.pdf",
    sourceLanguage: "Italian",
    targetLanguage: "English",
    model: "llama3.2:3b",
    status: "Completed",
    jobId: null,
    unitCount: 1,
    completedUnitCount: 1,
    progressPercent: 100,
    lastError: null,
    createdAtUtc: "2026-05-21T12:00:00Z",
    updatedAtUtc: "2026-05-21T12:05:00Z"
  };
}

function createUnit(overrides: Partial<TranslationUnit> = {}): TranslationUnit {
  return {
    id: 10,
    translationId: 1,
    unitIndex: 0,
    unitKind: "paragraph",
    displayLabel: "Pagina 1 - Paragrafo 1",
    pageNumber: 1,
    sourceText: "Testo originale",
    machineTranslatedText: "Machine text",
    translatedText: "Translated text",
    status: "Completed",
    manuallyEdited: false,
    validationWarnings: null,
    error: null,
    attemptCount: 1,
    createdAtUtc: "2026-05-21T12:00:00Z",
    updatedAtUtc: "2026-05-21T12:05:00Z",
    ...overrides
  };
}

function createCompare(): TranslationCompare {
  return {
    translation: createSummary(),
    currentPage: 1,
    pagePosition: 1,
    pageCount: 1,
    previousPage: null,
    nextPage: null,
    units: [createUnit()]
  };
}
