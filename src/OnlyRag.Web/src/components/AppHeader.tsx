import { useEffect, useRef, useState } from "react";
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
  onOpenJobsDrawer?: () => void;
};

export function AppHeader({ currentSection, backendStatus, diagnostics, onOpenJobsDrawer }: AppHeaderProps) {
  const [currentTime, setCurrentTime] = useState(() => new Date());
  const [showStatusMenu, setShowStatusMenu] = useState(false);
  const statusMenuRef = useRef<HTMLDivElement | null>(null);
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

  const isOverallHealthy = backendStatus.backendTone === "online" && backendStatus.ollamaTone === "online";

  useEffect(() => {
    const timerId = window.setInterval(() => setCurrentTime(new Date()), 1000);
    return () => window.clearInterval(timerId);
  }, []);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (statusMenuRef.current && !statusMenuRef.current.contains(event.target as Node)) {
        setShowStatusMenu(false);
      }
    }
    if (showStatusMenu) {
      document.addEventListener("mousedown", handleClickOutside);
      return () => document.removeEventListener("mousedown", handleClickOutside);
    }
  }, [showStatusMenu]);

  return (
    <header className="app-header">
      <h1 id="workspace-title" title={currentSection}>{currentSection}</h1>
      <div className="status-row">
        <div className="status-row__states" role="status" aria-label="Stato applicazione" aria-live="polite" aria-atomic="true">
          <div className="status-menu-container" ref={statusMenuRef}>
            <button
              type="button"
              className={`status-badge status-badge--${isOverallHealthy ? "online" : "warning"}`}
              onClick={() => setShowStatusMenu((prev) => !prev)}
              aria-expanded={showStatusMenu}
              aria-label="Stato sistema"
            >
              <span>Stato Sistema</span>
              <strong>{isOverallHealthy ? "Pronto" : "Attenzione"}</strong>
            </button>
            {showStatusMenu && (
              <div className="status-menu-popover">
                <div className="status-menu-popover__header">
                  <span className="status-menu-popover__title">Salute Moduli AI</span>
                  <button type="button" className="button-secondary" onClick={() => setShowStatusMenu(false)} style={{ padding: "2px 6px" }}>✕</button>
                </div>
                {statusBadges.map((badge) => (
                  <div className="status-menu-item" key={badge.label}>
                    <span className="status-menu-item__label">{badge.label}</span>
                    <span className={`status-badge status-badge--${badge.tone}`}>
                      <strong>{badge.value}</strong>
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>
          {/* Accessible hidden text content for screen readers / tests */}
          <div className="sr-only">
            {statusBadges.map((b) => `${b.label}: ${b.value}`).join(", ")}
          </div>
          <span className="sr-only">{jobsBadge.label} {jobsBadge.value}</span>
        </div>
        <div className="status-row__operations">
          <button
            type="button"
            className="button-secondary"
            onClick={onOpenJobsDrawer}
            title="Apri pannello operazioni in background"
          >
            <StatusBadgeView badge={jobsBadge} />
          </button>
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
