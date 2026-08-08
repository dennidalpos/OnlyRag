import { useEffect, useRef, useState } from "react";
import type { BackendStatus } from "../../App";
import type { DiagnosticsResponse, OcrProvisionStatus } from "../../api";
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
  ocrProvisionStatus?: OcrProvisionStatus | null;
  isInitialChecking?: boolean;
  onOpenJobsDrawer?: () => void;
  onOpenCommandPalette?: () => void;
};

export function AppHeader({
  currentSection,
  backendStatus,
  diagnostics,
  ocrProvisionStatus,
  isInitialChecking = false,
  onOpenJobsDrawer,
  onOpenCommandPalette
}: AppHeaderProps) {
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

  const isOcrRunning = Boolean(ocrProvisionStatus?.isRunning);
  const isRerankerDownloading = Boolean(diagnostics?.reranker?.isDownloading);
  const isLoadingOrProvisioning = isInitialChecking || isOcrRunning || isRerankerDownloading;

  const statusBadges: StatusBadge[] = [
    { label: "Backend", value: backendStatus.backendValue, tone: backendStatus.backendTone },
    { label: "Ollama", value: backendStatus.ollamaValue, tone: backendStatus.ollamaTone },
    buildCloudLlmBadge(diagnostics, backendStatus.backendTone),
    buildQdrantBadge(diagnostics, backendStatus.backendTone, isInitialChecking),
    buildSqliteBadge(backendStatus.backendTone),
    buildRerankerBadge(diagnostics, backendStatus.backendTone),
    buildOcrBadge(diagnostics, ocrProvisionStatus, backendStatus.backendTone, isInitialChecking),
    buildOcrGpuBadge(diagnostics, ocrProvisionStatus, backendStatus.backendTone),
    buildImageBadge(diagnostics, backendStatus.backendTone),
    buildAgentEngineBadge(backendStatus.backendTone),
    buildKnowledgeGraphBadge(backendStatus.backendTone)
  ].filter((badge): badge is StatusBadge => badge !== null);

  let overallLabel: string;
  let overallTone: "online" | "warning" | "offline";

  if (backendStatus.backendTone === "offline") {
    overallLabel = "Offline";
    overallTone = "offline";
  } else if (isLoadingOrProvisioning) {
    overallLabel = isOcrRunning
      ? "OCR in corso..."
      : isRerankerDownloading
        ? "Download AI..."
        : "In caricamento...";
    overallTone = "warning";
  } else {
    const isQdrantStarting =
      diagnostics?.qdrant.status === "Caricamento" ||
      diagnostics?.qdrant.status === "In caricamento..." ||
      diagnostics?.qdrant.status === "Starting";

    const hasCriticalIssues =
      backendStatus.ollamaTone !== "online" ||
      (diagnostics !== null && !diagnostics.ocrIsConfigured) ||
      (diagnostics !== null && !diagnostics.qdrant.isReachable && !isQdrantStarting) ||
      statusBadges.some((b) => b.tone === "offline");

    if (hasCriticalIssues) {
      overallLabel = "Incompleto";
      overallTone = "warning";
    } else {
      overallLabel = "Pronto";
      overallTone = "online";
    }
  }

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (statusMenuRef.current && !statusMenuRef.current.contains(event.target as Node)) {
        setShowStatusMenu(false);
      }
      if (themeMenuRef.current && !themeMenuRef.current.contains(event.target as Node)) {
        setShowThemeMenu(false);
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setShowStatusMenu(false);
        setShowThemeMenu(false);
      }
    }

    if (showStatusMenu || showThemeMenu) {
      document.addEventListener("mousedown", handleClickOutside);
      document.addEventListener("keydown", handleKeyDown);
      return () => {
        document.removeEventListener("mousedown", handleClickOutside);
        document.removeEventListener("keydown", handleKeyDown);
      };
    }
  }, [showStatusMenu, showThemeMenu]);

  return (
    <header className="app-header">
      <div className="flex items-center gap-3">
        <h1 id="workspace-title" title={currentSection}>{currentSection}</h1>
        {onOpenCommandPalette && (
          <button
            type="button"
            id="command-palette-trigger"
            className="status-badge status-badge--neutral hover:border-focus cursor-pointer"
            onClick={onOpenCommandPalette}
            title="Cerca comandi, sezioni e modelli (Ctrl+K)"
            aria-label="Apri tavolozza comandi (Ctrl+K)"
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
              className={`status-badge status-badge--${overallTone}`}
              onClick={() => setShowStatusMenu((prev) => !prev)}
              aria-expanded={showStatusMenu}
              aria-haspopup="true"
              aria-controls="status-menu-popover"
              aria-label="Stato sistema"
            >
              <span>Stato Sistema</span>
              <strong>{overallLabel}</strong>
            </button>
            {showStatusMenu && (
              <div id="status-menu-popover" className="status-menu-popover" role="dialog" aria-label="Salute Moduli AI">
                <div className="status-menu-popover__header">
                  <span className="status-menu-popover__title">Salute Moduli AI</span>
                  <button type="button" className="button-secondary" onClick={() => setShowStatusMenu(false)} aria-label="Chiudi menu stato" style={{ padding: "2px 6px" }}>✕</button>
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
              aria-haspopup="true"
              aria-controls="theme-menu-popover"
            >
              <span>Tema</span>
              <strong>{currentThemeObj.name}</strong>
            </button>
            {showThemeMenu && (
              <div id="theme-menu-popover" className="theme-menu-popover" role="dialog" aria-label="Seleziona Tema Visivo">
                <div className="theme-menu-popover__header">
                  <span className="theme-menu-popover__title">Seleziona Tema Visivo</span>
                  <button type="button" className="button-secondary" onClick={() => setShowThemeMenu(false)} aria-label="Chiudi menu tema" style={{ padding: "2px 6px" }}>✕</button>
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



function buildCloudLlmBadge(
  diagnostics: DiagnosticsResponse | null,
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  if (!diagnostics?.cloudLlm) {
    return {
      label: "LLM Cloud",
      value: backendTone === "offline" ? "Offline" : "In lettura",
      tone: backendTone === "offline" ? "offline" : "neutral"
    };
  }

  return {
    label: "LLM Cloud",
    value: diagnostics.cloudLlm.hasApiKey ? diagnostics.cloudLlm.provider : "Non configurato",
    tone: diagnostics.cloudLlm.hasApiKey ? "online" : "neutral"
  };
}

function buildQdrantBadge(
  diagnostics: DiagnosticsResponse | null,
  backendTone: BackendStatus["backendTone"],
  isInitialChecking: boolean
): StatusBadge {
  if (!diagnostics) {
    return {
      label: "Vettori (Qdrant)",
      value: backendTone === "offline" ? "Offline" : isInitialChecking ? "Caricamento..." : "In lettura",
      tone: backendTone === "offline" ? "offline" : "warning"
    };
  }

  const isStarting =
    diagnostics.qdrant.status === "Caricamento" ||
    diagnostics.qdrant.status === "In caricamento..." ||
    diagnostics.qdrant.status === "Starting";

  return {
    label: "Vettori (Qdrant)",
    value: isStarting ? "Caricamento..." : diagnostics.qdrant.status,
    tone: diagnostics.qdrant.isReachable ? "online" : "warning"
  };
}

function buildSqliteBadge(
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  return {
    label: "Database & FTS5",
    value: backendTone === "offline" ? "Offline" : "Attivo",
    tone: backendTone === "offline" ? "offline" : "online"
  };
}

function buildRerankerBadge(
  diagnostics: DiagnosticsResponse | null,
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  if (!diagnostics?.reranker) {
    return {
      label: "Re-ranker AI",
      value: backendTone === "offline" ? "Offline" : "Euristico (CPU)",
      tone: backendTone === "offline" ? "offline" : "neutral"
    };
  }

  if (diagnostics.reranker.isDownloading) {
    return {
      label: "Re-ranker AI",
      value: "Download...",
      tone: "warning"
    };
  }

  return {
    label: "Re-ranker AI",
    value: diagnostics.reranker.isDownloaded ? "ONNX Cross-Encoder" : "Euristico (CPU)",
    tone: diagnostics.reranker.isDownloaded ? "online" : "neutral"
  };
}

function buildOcrBadge(
  diagnostics: DiagnosticsResponse | null,
  ocrProvisionStatus: OcrProvisionStatus | null | undefined,
  backendTone: BackendStatus["backendTone"],
  isInitialChecking: boolean
): StatusBadge {
  if (ocrProvisionStatus?.isRunning) {
    return {
      label: "OCR (Testo)",
      value: ocrProvisionStatus.stepLabel || "Installazione in corso...",
      tone: "warning"
    };
  }

  if (!diagnostics) {
    return {
      label: "OCR (Testo)",
      value: backendTone === "offline" ? "Offline" : isInitialChecking ? "Caricamento..." : "Da verificare",
      tone: backendTone === "offline" ? "offline" : "warning"
    };
  }

  return {
    label: "OCR (Testo)",
    value: diagnostics.ocrIsConfigured ? diagnostics.ocrStatus : "Non configurato",
    tone: diagnostics.ocrIsConfigured ? "online" : "warning"
  };
}

function buildOcrGpuBadge(
  diagnostics: DiagnosticsResponse | null,
  ocrProvisionStatus: OcrProvisionStatus | null | undefined,
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  if (ocrProvisionStatus?.isRunning) {
    return {
      label: "OCR GPU",
      value: "In corso...",
      tone: "warning"
    };
  }

  if (!diagnostics) {
    return {
      label: "OCR GPU",
      value: backendTone === "offline" ? "Offline" : "In lettura",
      tone: backendTone === "offline" ? "neutral" : "neutral"
    };
  }

  if (diagnostics.ocrGpuCapability.capabilityStatus === "no_nvidia_gpu") {
    return {
      label: "OCR GPU",
      value: "CPU Fallback",
      tone: "neutral"
    };
  }

  return {
    label: "OCR GPU",
    value: diagnostics.ocrGpuCapability.isUsable ? "Disponibile (CUDA)" : "Non disponibile",
    tone: diagnostics.ocrGpuCapability.isUsable ? "online" : "warning"
  };
}

function buildImageBadge(
  diagnostics: DiagnosticsResponse | null,
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  if (!diagnostics?.imageGeneration) {
    return {
      label: "Immagini (ONNX)",
      value: backendTone === "offline" ? "Offline" : "In lettura",
      tone: backendTone === "offline" ? "offline" : "neutral"
    };
  }

  const status = diagnostics.imageGeneration;
  if (!status.isReady) {
    return {
      label: "Immagini (ONNX)",
      value: "Modello mancante",
      tone: "warning"
    };
  }

  if (status.executionProvider === "DirectML") {
    return {
      label: "Immagini (ONNX)",
      value: "DirectML",
      tone: "online"
    };
  }

  if (status.executionProvider === "CPU") {
    return {
      label: "Immagini (ONNX)",
      value: "CPU",
      tone: status.preferredExecutionProvider === "DirectML" ? "warning" : "online"
    };
  }

  return {
    label: "Immagini (ONNX)",
    value: "Pronto",
    tone: "online"
  };
}

function buildAgentEngineBadge(
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  return {
    label: "Agent Engine",
    value: backendTone === "offline" ? "Offline" : "Attivo (MCTS)",
    tone: backendTone === "offline" ? "offline" : "online"
  };
}

function buildKnowledgeGraphBadge(
  backendTone: BackendStatus["backendTone"]
): StatusBadge {
  return {
    label: "Knowledge Graph",
    value: backendTone === "offline" ? "Offline" : "Attivo",
    tone: backendTone === "offline" ? "offline" : "online"
  };
}

function formatJobsValue(jobsValue: string, activeJobs: number): string {
  if (jobsValue === "Disabled") {
    return "Disattivate";
  }

  return activeJobs > 0 ? `${activeJobs} in corso` : "Nessuna";
}
