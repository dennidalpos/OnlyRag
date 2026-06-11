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
  const jobsBadge: StatusBadge = {
    label: "Operazioni",
    value: formatJobsValue(backendStatus.jobsValue, activeJobs),
    tone: activeJobs > 0 ? backendStatus.jobsTone : "neutral"
  };
  const statusBadges: StatusBadge[] = [
    { label: "Backend", value: backendStatus.backendValue, tone: backendStatus.backendTone },
    { label: "Ollama", value: backendStatus.ollamaValue, tone: backendStatus.ollamaTone },
    buildQdrantBadge(diagnostics, backendStatus.backendTone),
    buildOcrBadge(diagnostics, backendStatus.backendTone),
    buildOcrGpuBadge(diagnostics, backendStatus.backendTone),
    buildImageBadge(diagnostics, backendStatus.backendTone)
  ].filter((badge): badge is StatusBadge => badge !== null);

  useEffect(() => {
    const timerId = window.setInterval(() => setCurrentTime(new Date()), 1000);
    return () => window.clearInterval(timerId);
  }, []);

  return (
    <header className="app-header">
      <h1 id="workspace-title" title={currentSection}>{currentSection}</h1>
      <div className="status-row">
        <div className="status-row__states" role="status" aria-label="Stato applicazione" aria-live="polite" aria-atomic="true">
          {statusBadges.map((badge) => (
            <StatusBadgeView badge={badge} key={badge.label} />
          ))}
          <span className="sr-only">{jobsBadge.label} {jobsBadge.value}</span>
        </div>
        <div className="status-row__operations">
          <StatusBadgeView badge={jobsBadge} />
          <span
            className="status-badge status-badge--neutral status-row__clock"
            title={`Ora corrente ${formatTime(currentTime.toISOString())}`}
          >
            <span>Ora</span>
            <strong>{formatTime(currentTime.toISOString())}</strong>
          </span>
        </div>
      </div>
    </header>
  );
}

function StatusBadgeView({ badge }: { badge: StatusBadge }) {
  return (
    <span className={`status-badge status-badge--${badge.tone}`} title={`${badge.label} ${badge.value}`}>
      <span>{badge.label}</span>
      <strong>{badge.value}</strong>
    </span>
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
): StatusBadge | null {
  if (!diagnostics) {
    return {
      label: "OCR GPU",
      value: backendTone === "offline" ? "Offline" : "In lettura",
      tone: backendTone === "offline" ? "offline" : "neutral"
    };
  }

  if (diagnostics.ocrGpuCapability.capabilityStatus === "no_nvidia_gpu") {
    return null;
  }

  return {
    label: "OCR GPU",
    value: diagnostics.ocrGpuCapability.isUsable ? "Disponibile" : "Non disponibile",
    tone: diagnostics.ocrGpuCapability.isUsable ? "online" : "neutral"
  };
}

function buildImageBadge(
  diagnostics: DiagnosticsResponse | null,
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  if (!diagnostics?.imageGeneration) {
    return {
      label: "Immagini",
      value: backendTone === "offline" ? "Offline" : "In lettura",
      tone: backendTone === "offline" ? "offline" : "neutral"
    };
  }

  const status = diagnostics.imageGeneration;
  if (!status.isReady) {
    return {
      label: "Immagini",
      value: "Modello mancante",
      tone: "warning"
    };
  }

  if (status.executionProvider === "DirectML") {
    return {
      label: "Immagini",
      value: "DirectML",
      tone: "online"
    };
  }

  if (status.executionProvider === "CPU") {
    return {
      label: "Immagini",
      value: "CPU",
      tone: status.preferredExecutionProvider === "DirectML" ? "warning" : "online"
    };
  }

  return {
    label: "Immagini",
    value: "Pronto",
    tone: "online"
  };
}

function formatJobsValue(jobsValue: string, activeJobs: number): string {
  if (jobsValue === "Disabled") {
    return "Disattivate";
  }

  return activeJobs > 0 ? `${activeJobs} in corso` : "Nessuna";
}
