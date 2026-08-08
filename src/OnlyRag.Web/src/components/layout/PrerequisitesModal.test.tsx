import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { PrerequisitesModal } from "./PrerequisitesModal";
import type { OcrProvisionStatus } from "../../api";

describe("PrerequisitesModal", () => {
  it("does not render when isOpen is false", () => {
    render(
      <PrerequisitesModal
        isOpen={false}
        onClose={vi.fn()}
        ocrAnalysis={null}
        ocrProvisionStatus={null}
        isConfiguring={false}
        onConfigureOcr={vi.fn()}
        onCancelOcr={vi.fn()}
      />
    );

    ariaExpectNull();
  });

  function ariaExpectNull() {
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  }

  it("renders correctly when open and displays status & progress", () => {
    const status: OcrProvisionStatus = {
      isConfigured: false,
      isRunning: true,
      message: "Installazione componenti OCR in corso...",
      lastError: null,
      runtimeTarget: "auto",
      resolvedRuntime: "cpu",
      runtimeDetail: "Installazione wheel PaddlePaddle...",
      startedAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
      stepKey: "paddle-install",
      stepLabel: "Installazione PaddleOCR",
      stepIndex: 6,
      stepCount: 8,
      progressPercent: 75,
      severity: "running",
      canRetry: false,
      selectedRuntime: null,
      isAutomaticRepair: false
    };

    render(
      <PrerequisitesModal
        isOpen={true}
        onClose={vi.fn()}
        ocrAnalysis={null}
        ocrProvisionStatus={status}
        isConfiguring={true}
        onConfigureOcr={vi.fn()}
        onCancelOcr={vi.fn()}
      />
    );

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText(/Installazione PaddleOCR/i)).toBeInTheDocument();
    expect(screen.getByText(/Passo 6\/8 \(75%\)/i)).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toBeInTheDocument();
    expect(screen.getByText("Annulla Operazione")).toBeInTheDocument();
  });

  it("triggers configuration when start button is clicked", async () => {
    const user = userEvent.setup();
    const handleConfigure = vi.fn();

    render(
      <PrerequisitesModal
        isOpen={true}
        onClose={vi.fn()}
        ocrAnalysis={null}
        ocrProvisionStatus={null}
        isConfiguring={false}
        onConfigureOcr={handleConfigure}
        onCancelOcr={vi.fn()}
      />
    );

    const startBtn = screen.getByRole("button", { name: /Avvia Installazione OCR Paddle/i });
    await user.click(startBtn);

    expect(handleConfigure).toHaveBeenCalledWith("auto");
  });

  it("calls onClose when close button is clicked even if installation is running", async () => {
    const user = userEvent.setup();
    const handleClose = vi.fn();

    render(
      <PrerequisitesModal
        isOpen={true}
        onClose={handleClose}
        ocrAnalysis={null}
        ocrProvisionStatus={{
          isConfigured: false,
          isRunning: true,
          message: "In corso...",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "cpu",
          runtimeDetail: null,
          startedAtUtc: null,
          updatedAtUtc: null,
          stepKey: "run",
          stepLabel: "In corso",
          stepIndex: 1,
          stepCount: 8,
          progressPercent: 10,
          severity: "running",
          canRetry: false,
          selectedRuntime: null,
          isAutomaticRepair: false
        }}
        isConfiguring={true}
        onConfigureOcr={vi.fn()}
        onCancelOcr={vi.fn()}
      />
    );

    const closeBtn = screen.getByRole("button", { name: "Chiudi" });
    await user.click(closeBtn);
    expect(handleClose).toHaveBeenCalledTimes(1);
  });

  it("renders all minimum required module status cards and permits switching OCR flows", async () => {
    const user = userEvent.setup();

    render(
      <PrerequisitesModal
        isOpen={true}
        onClose={vi.fn()}
        ocrAnalysis={null}
        ocrProvisionStatus={null}
        isConfiguring={false}
        onConfigureOcr={vi.fn()}
        onCancelOcr={vi.fn()}
        ollamaInstalled={false}
        libreOfficeInstalled={false}
      />
    );

    expect(screen.getByText("Motore AI LLM (Ollama)")).toBeInTheDocument();
    expect(screen.getByText("Esportazione PDF (LibreOffice)")).toBeInTheDocument();
    expect(screen.getByText(/Spazio su Disco/i)).toBeInTheDocument();

    const visionTab = screen.getByRole("button", { name: /OCR Vision/i });
    await user.click(visionTab);
    expect(screen.getByText(/Zero Python Richiesto/i)).toBeInTheDocument();
  });
});
