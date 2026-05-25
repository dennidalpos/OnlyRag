import { useEffect, useState } from "react";
import type { BackendStatus } from "../App";
import type { DiagnosticsResponse } from "../api";
import { formatTime } from "../pollingStatus";

type StatusBadge = {
  label: string;
  value: string;
  tone: "online" | "offline" | "warning" | "neutral";
};

type AppHeaderProps = {
  currentSection: string;
  backendStatus: BackendStatus;
  diagnostics: DiagnosticsResponse | null;
};

export function AppHeader({ currentSection, backendStatus, diagnostics }: AppHeaderProps) {
  const [currentTime, setCurrentTime] = useState(() => new Date());
  const activeJobs = parseInt(backendStatus.jobsValue, 10);
  const statusBadges: StatusBadge[] = [
    { label: "Backend", value: backendStatus.backendValue, tone: backendStatus.backendTone },
    { label: "Ollama", value: backendStatus.ollamaValue, tone: backendStatus.ollamaTone },
    buildQdrantBadge(diagnostics, backendStatus.backendTone),
    buildOcrBadge(diagnostics, backendStatus.backendTone),
    buildOcrGpuBadge(diagnostics, backendStatus.backendTone),
    {
      label: "Operazioni",
      value: formatJobsValue(backendStatus.jobsValue, activeJobs),
      tone: activeJobs > 0 ? backendStatus.jobsTone : "neutral"
    }
  ];

  useEffect(() => {
    const timerId = window.setInterval(() => setCurrentTime(new Date()), 1000);
    return () => window.clearInterval(timerId);
  }, []);

  return (
    <header className="app-header">
      <h1 title={currentSection}>{currentSection}</h1>
      <div className="status-row" role="status" aria-label="Stato applicazione" aria-live="polite" aria-atomic="true">
        {statusBadges.map((badge) => (
          <span className={`status-badge status-badge--${badge.tone}`} key={badge.label} title={`${badge.label} ${badge.value}`}>
            <span>{badge.label}</span>
            <strong>{badge.value}</strong>
          </span>
        ))}
        <span className="status-badge status-badge--neutral" title={`Ora corrente ${formatTime(currentTime.toISOString())}`}>
          <span>Ora</span>
          <strong>{formatTime(currentTime.toISOString())}</strong>
        </span>
      </div>
    </header>
  );
}

function buildQdrantBadge(
  diagnostics: DiagnosticsResponse | null,
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  if (!diagnostics) {
    return {
      label: "Qdrant",
      value: backendTone === "offline" ? "Offline" : "In lettura",
      tone: backendTone === "offline" ? "offline" : "neutral"
    };
  }

  return {
    label: "Qdrant",
    value: diagnostics.qdrant.status,
    tone: diagnostics.qdrant.isReachable ? "online" : "offline"
  };
}

function buildOcrBadge(
  diagnostics: DiagnosticsResponse | null,
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  if (!diagnostics) {
    return {
      label: "OCR",
      value: backendTone === "offline" ? "Offline" : "In lettura",
      tone: backendTone === "offline" ? "offline" : "neutral"
    };
  }

  return {
    label: "OCR",
    value: diagnostics.ocrStatus,
    tone: diagnostics.ocrIsConfigured ? "online" : "offline"
  };
}

function buildOcrGpuBadge(
  diagnostics: DiagnosticsResponse | null,
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  if (!diagnostics) {
    return {
      label: "OCR GPU",
      value: backendTone === "offline" ? "Offline" : "In lettura",
      tone: backendTone === "offline" ? "offline" : "neutral"
    };
  }

  return {
    label: "OCR GPU",
    value: diagnostics.ocrGpuCapability.isUsable ? "Disponibile" : "Non disponibile",
    tone: diagnostics.ocrGpuCapability.isUsable ? "online" : "neutral"
  };
}

function formatJobsValue(jobsValue: string, activeJobs: number): string {
  if (jobsValue === "Disabled") {
    return "Disattivate";
  }

  return activeJobs > 0 ? `${activeJobs} in corso` : "Nessuna";
}
