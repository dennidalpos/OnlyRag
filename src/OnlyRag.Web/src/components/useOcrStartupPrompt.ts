import { useState } from "react";
import {
  apiRequest,
  type DependencyActionResponse,
  type OcrStartupAnalysis
} from "../api";

export function useOcrStartupPrompt() {
  const [analysis, setAnalysis] = useState<OcrStartupAnalysis | null>(null);
  const [isDismissed, setIsDismissed] = useState(false);
  const [isConfiguring, setIsConfiguring] = useState(false);

  async function refresh() {
    const result = await apiRequest<OcrStartupAnalysis>("/api/dependencies/ocr/startup-analysis")
      .catch(() => null);
    setAnalysis(result);
  }

  async function configure() {
    if (!analysis) {
      return;
    }

    setIsConfiguring(true);
    try {
      await apiRequest<DependencyActionResponse>("/api/dependencies/ocr/provision", {
        method: "POST",
        body: JSON.stringify({
          confirmed: true,
          runtimeTarget: analysis.recommendedRuntimeTarget
        })
      });
      setIsDismissed(true);
      await refresh();
    } finally {
      setIsConfiguring(false);
    }
  }

  function dismiss() {
    setIsDismissed(true);
  }

  return {
    analysis,
    isDismissed,
    isConfiguring,
    refresh,
    configure,
    dismiss
  } as const;
}
