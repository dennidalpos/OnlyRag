import { useEffect, useRef, useState } from "react";
import type { BackendStatus } from "../../App";
import type { DiagnosticsResponse } from "../../api";
import { formatTime } from "../../pollingStatus";
import { useTheme } from "../../context/ThemeContext";

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
  onOpenCommandPalette?: () => void;
};

export function AppHeader({ currentSection, backendStatus, diagnostics, onOpenJobsDrawer, onOpenCommandPalette }: AppHeaderProps) {
  const { theme, setTheme, themes } = useTheme();
  const [showStatusMenu, setShowStatusMenu] = useState(false);
  const [showThemeMenu, setShowThemeMenu] = useState(false);
  const statusMenuRef = useRef<HTMLDivElement | null>(null);
  const themeMenuRef = useRef<HTMLDivElement | null>(null);
  const activeJobs = parseInt(backendStatus.jobsValue, 10);

  const currentThemeObj = themes.find((t) => t.id === theme) || themes[0];

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
    function handleClickOutside(event: MouseEvent) {
      if (statusMenuRef.current && !statusMenuRef.current.contains(event.target as Node)) {
        setShowStatusMenu(false);
      }
      if (themeMenuRef.current && !themeMenuRef.current.contains(event.target as Node)) {
        setShowThemeMenu(false);
      }
    }
    if (showStatusMenu || showThemeMenu) {
      document.addEventListener("mousedown", handleClickOutside);
      return () => document.removeEventListener("mousedown", handleClickOutside);
    }
  }, [showStatusMenu, showThemeMenu]);

  return (
    <header className="app-header">
      <div className="flex items-center gap-3">
        <h1 id="workspace-title" title={currentSection}>{currentSection}</h1>
        {onOpenCommandPalette && (
          <button
            type="button"
            className="status-badge status-badge--neutral hover:border-focus cursor-pointer"
            onClick={onOpenCommandPalette}
            title="Cerca comandi, sezioni e modelli (Ctrl+K)"
          >
            <span>Cerca</span>
            <strong className="text-xs px-1 rounded bg-card border border-light text-muted">Ctrl+K</strong>
          </button>
        )}
      </div>

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
          <div className="theme-switcher-container" ref={themeMenuRef}>
            <button
              type="button"
              className="status-badge status-badge--neutral status-badge--theme-switcher"
              onClick={() => setShowThemeMenu((prev) => !prev)}
              title={`Tema visivo attuale: ${currentThemeObj.name}. Clicca per cambiare tema`}
              aria-label="Cambia tema visivo"
              aria-expanded={showThemeMenu}
            >
              <span>{currentThemeObj.icon}</span>
              <strong>{currentThemeObj.name}</strong>
            </button>
            {showThemeMenu && (
              <div className="theme-menu-popover">
                <div className="theme-menu-popover__header">
                  <span className="theme-menu-popover__title">Seleziona Tema Visivo</span>
                  <button type="button" className="button-secondary" onClick={() => setShowThemeMenu(false)} style={{ padding: "2px 6px" }}>✕</button>
                </div>
                {themes.map((t) => (
                  <button
                    key={t.id}
                    type="button"
                    className={`theme-menu-item ${t.id === theme ? "theme-menu-item--active" : ""}`}
                    onClick={() => {
                      setTheme(t.id);
                      setShowThemeMenu(false);
                    }}
                  >
                    <span className="theme-menu-item__icon">{t.icon}</span>
                    <div className="theme-menu-item__info">
                      <span className="theme-menu-item__name">{t.name}</span>
                      <span className="theme-menu-item__desc">{t.description}</span>
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>
          <button
            type="button"
            className={`status-badge status-badge--${jobsBadge.tone}`}
            onClick={onOpenJobsDrawer}
            title="Apri pannello operazioni in background"
          >
            <span>{jobsBadge.label}</span>
            <strong>{jobsBadge.value}</strong>
          </button>
          <span
            className="status-badge status-badge--neutral status-row__clock"
            title={`Ora corrente ${formatTime(new Date().toISOString())}`}
          >
            <span>Ora</span>
            <strong>{formatTime(new Date().toISOString())}</strong>
          </span>
        </div>
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
