import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { createDiagnostics } from "../test/fixtures";
import { Sidebar } from "./Sidebar";

describe("Sidebar", () => {
  it("uses localized text for CUDA support", () => {
    render(
      <Sidebar
        activeSection="chat"
        sections={{
          chat: "Chat",
          documents: "Documenti",
          jobs: "Operazioni",
          translation: "Traduzione",
          settings: "Impostazioni"
        }}
        onSectionChange={vi.fn()}
        diagnostics={createDiagnostics({
          ocrGpuCapability: {
            isUsable: true,
            status: "GPU OCR utilizzabile",
            blockReason: null,
            runtimeDetail: "NVIDIA compatibile.",
            engineVersion: "3.5.0",
            nvidiaName: "NVIDIA RTX",
            driverVersion: "596.49",
            compiledWithCuda: true,
            cudaDeviceCount: 1,
            activeDevice: "gpu:0",
            packageVersions: {},
            capabilityStatus: "usable"
          }
        })}
      />
    );

    expect(screen.getByText("CUDA Paddle").closest(".sidebar-metric")).toHaveTextContent("Sì");
  });
});
