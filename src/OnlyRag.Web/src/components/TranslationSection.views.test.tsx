import { createRef } from "react";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { TranslationSummary } from "../api";
import { TranslationListCard } from "./TranslationSection.views";

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
