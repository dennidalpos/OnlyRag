import { useEffect, useState } from "react";
import {
  apiRequest,
  type DependencyActionResponse,
  type DiagnosticsResponse,
  type OcrAutoGpuEnableResponse,
  type OcrProvisionStatus,
  type OcrStartupAnalysis
} from "../api";

export function useOcrStartupPrompt() {
  const [analysis, setAnalysis] = useState<OcrStartupAnalysis | null>(null);
  const [provisionStatus, setProvisionStatus] = useState<OcrProvisionStatus | null>(null);
  const [lastCheckedAt, setLastCheckedAt] = useState<Date | null>(null);
  const [isConfiguring, setIsConfiguring] = useState(false);

  async function refresh() {
    const [analysisResult, statusResult] = await Promise.all([
      apiRequest<OcrStartupAnalysis>("/api/dependencies/ocr/startup-analysis").catch(() => null),
      apiRequest<OcrProvisionStatus>("/api/dependencies/ocr").catch(() => null)
    ]);
    setAnalysis(analysisResult);
    setProvisionStatus(statusResult);
    setLastCheckedAt(new Date());

    await autoEnableGpuAfterProvisioning(statusResult);
  }

  async function configure(runtimeTarget: "auto" | "cpu" | "nvidia" = "auto") {
    if (!analysis) {
      return;
    }

    setIsConfiguring(true);
    try {
      await apiRequest<DependencyActionResponse>("/api/dependencies/ocr/provision", {
        method: "POST",
        body: JSON.stringify({
          confirmed: true,
          runtimeTarget
        })
      });
      await refresh();
    } finally {
      setIsConfiguring(false);
    }
  }

  async function cancel() {
    setIsConfiguring(true);
    try {
      await apiRequest<DependencyActionResponse>("/api/dependencies/ocr/cancel", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
      await refresh();
    } finally {
      setIsConfiguring(false);
    }
  }

  useEffect(() => {
    if (!provisionStatus?.isRunning) {
      return undefined;
    }

    const interval = window.setInterval(() => {
      void refresh();
    }, 3_000);

    return () => window.clearInterval(interval);
  }, [provisionStatus?.isRunning]);

  return {
    analysis,
    provisionStatus,
    lastCheckedAt,
    isConfiguring,
    refresh,
    configure,
    cancel
  } as const;
}

async function autoEnableGpuAfterProvisioning(status: OcrProvisionStatus | null) {
  if (!status?.isConfigured || status.isRunning) {
    return;
  }

  const diagnostics = await apiRequest<DiagnosticsResponse>("/api/diagnostics").catch(() => null);
  if (!diagnostics?.ocrGpuCapability.isUsable) {
    return;
  }

  await apiRequest<OcrAutoGpuEnableResponse>("/api/settings/ocr/auto-enable-gpu", {
    method: "POST"
  }).catch(() => {});
}
