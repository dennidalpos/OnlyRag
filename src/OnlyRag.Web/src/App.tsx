import { useEffect, useRef, useState } from "react";
import {
  apiRequest,
  markBackendOffline,
  markBackendOnline,
  resolveBackendErrorMessage,
  type DependencyActionResponse,
  type DiagnosticsResponse,
  type OcrAutoGpuEnableResponse,
  type OllamaInstallStatus,
  type OllamaModel,
  type OllamaModelsResponse,
  type OllamaSettings,
  type OllamaStatusResponse
} from "./api";
import { initializeAppLifecycleBridge } from "./appLifecycle";
import { AppHeader } from "./components/AppHeader";
import { ChatSection } from "./components/ChatSection";
import { DocumentsSection } from "./components/DocumentsSection";
import { InitialSetupWizard } from "./components/InitialSetupWizard";
import { ImagesSection } from "./components/ImagesSection";
import { JobsSection } from "./components/JobsSection";
import { SectionId, Sidebar } from "./components/Sidebar";
import { SettingsSection } from "./components/SettingsSection";
import { TranslationSection } from "./components/TranslationSection";
import { useOcrStartupPrompt } from "./components/useOcrStartupPrompt";
import {
  formatLastRefresh,
  initialRefreshStatus,
  markRefreshFailure,
  markRefreshSuccess,
  shouldSurfaceRefreshFailure,
  type RefreshStatus
} from "./pollingStatus";

type AppStatusResponse = {
  backend: string;
  database: string;
  jobQueue: string;
  ollama: string;
  startedAtUtc: string;
  lowResourceMode: boolean;
};

type StatusTone = "online" | "offline" | "warning";

export type BackendStatus = {
  backendValue: string;
  backendTone: StatusTone;
  ollamaValue: string;
  ollamaTone: StatusTone;
  jobsValue: string;
  jobsTone: StatusTone;
  lowResourceMode: boolean;
  refreshStatus: RefreshStatus;
};

const sectionLabels: Record<SectionId, string> = {
  chat: "Chat",
  documents: "Documenti",
  images: "Immagini",
  jobs: "Operazioni",
  translation: "Traduzione",
  settings: "Impostazioni"
};

const offlineStatus: BackendStatus = {
  backendValue: "Offline",
  backendTone: "offline",
  ollamaValue: "Offline",
  ollamaTone: "offline",
  jobsValue: "0",
  jobsTone: "offline",
  lowResourceMode: false,
  refreshStatus: initialRefreshStatus
};

export default function App() {
  const [activeSection, setActiveSection] = useState<SectionId>("chat");
  const [backendStatus, setBackendStatus] = useState<BackendStatus>(offlineStatus);
  const [statusChecked, setStatusChecked] = useState(false);
  const [ollamaSettings, setOllamaSettings] = useState<OllamaSettings | null>(null);
  const [ollamaStatus, setOllamaStatus] = useState<OllamaStatusResponse | null>(null);
  const [ollamaInstallStatus, setOllamaInstallStatus] = useState<OllamaInstallStatus | null>(null);
  const [ollamaModels, setOllamaModels] = useState<OllamaModel[]>([]);
  const [diagnostics, setDiagnostics] = useState<DiagnosticsResponse | null>(null);
  const [documentLibraryVersion, setDocumentLibraryVersion] = useState(0);
  const [ollamaLoadError, setOllamaLoadError] = useState<string | null>(null);
  const [isRecheckingOllama, setIsRecheckingOllama] = useState(false);
  const [initialCheckDone, setInitialCheckDone] = useState(false);
  const [ollamaSettingsChecked, setOllamaSettingsChecked] = useState(false);
  const initialSetupCheckInProgressRef = useRef(false);
  const ocrStartupPrompt = useOcrStartupPrompt();

  async function refreshBackendStatus() {
    try {
      const status = await apiRequest<AppStatusResponse>("/api/app/status");
      markBackendOnline();
      setBackendStatus((current) => ({
        ...current,
        backendValue: status.backend,
        backendTone: "online",
        jobsValue: status.jobQueue,
        jobsTone: "online",
        lowResourceMode: status.lowResourceMode,
        refreshStatus: markRefreshSuccess()
      }));
    } catch {
      markBackendOffline();
      setBackendStatus((current) => ({
        ...offlineStatus,
        refreshStatus: markRefreshFailure(
          current.refreshStatus,
          resolveBackendErrorMessage() ??
            "Il backend locale non è raggiungibile. Le operazioni non sono disponibili. Riavviare l'applicazione."
        )
      }));
    } finally {
      setStatusChecked(true);
    }
  }

  async function refreshOllamaData() {
    try {
      const [settings, status] = await Promise.all([
        apiRequest<OllamaSettings>("/api/settings/ollama"),
        apiRequest<OllamaStatusResponse>("/api/ollama/status")
      ]);
      const dependencyStatus = await apiRequest<OllamaInstallStatus>("/api/dependencies/ollama")
        .catch(() => null);

      setOllamaSettings(settings);
      setOllamaSettingsChecked(true);
      setOllamaStatus(status);
      setOllamaInstallStatus(dependencyStatus);
      setBackendStatus((current) => ({
        ...current,
        ollamaValue: formatOllamaBadge(status),
        ollamaTone: getOllamaTone(status)
      }));

      if (status.isReachable) {
        const modelsResponse = await apiRequest<OllamaModelsResponse>("/api/ollama/models");
        setOllamaModels(modelsResponse.models);
        setOllamaLoadError(null);
      } else {
        setOllamaModels([]);
        setOllamaLoadError(status.suggestion ?? status.message);
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : "Impossibile leggere lo stato di Ollama.";
      setOllamaSettingsChecked(true);
      setOllamaStatus(null);
      setOllamaInstallStatus(null);
      setOllamaModels([]);
      setOllamaLoadError(message);
      setBackendStatus((current) => ({
        ...current,
        ollamaValue: "Errore",
        ollamaTone: "offline"
      }));
    }
  }

  async function refreshDiagnostics() {
    const data = await apiRequest<DiagnosticsResponse>("/api/diagnostics");
    setDiagnostics(data);
    return data;
  }

  async function autoEnableOcrGpu() {
    await apiRequest<OcrAutoGpuEnableResponse>("/api/settings/ocr/auto-enable-gpu", {
      method: "POST"
    });
  }

  async function handleInstallOllama() {
    setIsRecheckingOllama(true);
    try {
      await apiRequest<DependencyActionResponse>("/api/dependencies/ollama/install", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
      await refreshOllamaData();
    } finally {
      setIsRecheckingOllama(false);
    }
  }

  async function runInitialSetupChecks({ showBusy = false }: { showBusy?: boolean } = {}) {
    if (initialSetupCheckInProgressRef.current) {
      return;
    }

    initialSetupCheckInProgressRef.current = true;
    if (showBusy) {
      setIsRecheckingOllama(true);
    }

    try {
      await Promise.all([
        refreshBackendStatus(),
        refreshOllamaData(),
        refreshDiagnostics().catch(() => {}),
        ocrStartupPrompt.refresh()
      ]);
    } finally {
      initialSetupCheckInProgressRef.current = false;
      if (showBusy) {
        setIsRecheckingOllama(false);
      }
    }
  }

  async function handleRecheckInitialSetup() {
    await runInitialSetupChecks({ showBusy: true });
  }

  function notifyDocumentLibraryChanged() {
    setDocumentLibraryVersion((current) => current + 1);
  }

  useEffect(() => {
    initializeAppLifecycleBridge();
  }, []);

  useEffect(() => {
    let isCancelled = false;

    async function load() {
      initialSetupCheckInProgressRef.current = true;
      try {
        await refreshBackendStatus();
        if (isCancelled) {
          return;
        }

        await refreshOllamaData();
        if (isCancelled) {
          return;
        }

        const [diagnosticsResult] = await Promise.all([
          refreshDiagnostics().catch(() => null),
          ocrStartupPrompt.refresh()
        ]);
        if (!isCancelled && diagnosticsResult?.ocrGpuCapability.isUsable) {
          await autoEnableOcrGpu().catch(() => {});
        }
        if (!isCancelled) {
          setInitialCheckDone(true);
        }
      } finally {
        initialSetupCheckInProgressRef.current = false;
      }
    }

    void load();

    return () => {
      isCancelled = true;
    };
  }, []);

  useEffect(() => {
    const handle = setInterval(() => {
      void refreshBackendStatus();
    }, 3_000);
    return () => clearInterval(handle);
  }, []);

  useEffect(() => {
    const handle = setInterval(() => {
      void refreshDiagnostics().catch(() => {});
    }, 3_000);
    return () => clearInterval(handle);
  }, []);

  useEffect(() => {
    function recheckWhenAppOpens() {
      if (document.visibilityState === "hidden") {
        return;
      }

      void runInitialSetupChecks();
    }

    function recheckWhenVisible() {
      if (document.visibilityState === "visible") {
        void runInitialSetupChecks();
      }
    }

    window.addEventListener("focus", recheckWhenAppOpens);
    document.addEventListener("visibilitychange", recheckWhenVisible);
    return () => {
      window.removeEventListener("focus", recheckWhenAppOpens);
      document.removeEventListener("visibilitychange", recheckWhenVisible);
    };
  }, []);

  const previousSectionRef = useRef<SectionId>(activeSection);
  useEffect(() => {
    if (previousSectionRef.current === "settings" && activeSection !== "settings") {
      void runInitialSetupChecks();
    }
    previousSectionRef.current = activeSection;
  }, [activeSection]);

  return (
    <div className="desktop-shell">
      <a className="skip-link" href="#main-workspace">
        Salta al contenuto principale
      </a>
      <Sidebar
        activeSection={activeSection}
        sections={sectionLabels}
        onSectionChange={setActiveSection}
        activeJobCount={parseInt(backendStatus.jobsValue, 10) || 0}
        diagnostics={diagnostics}
      />
      <main className="workspace" id="main-workspace" aria-labelledby="workspace-title" tabIndex={-1}>
        <AppHeader currentSection={sectionLabels[activeSection]} backendStatus={backendStatus} diagnostics={diagnostics} />
        <section className={`workspace-content workspace-content--${activeSection}`} aria-labelledby="workspace-title">
          {statusChecked && backendStatus.backendTone === "offline" && (
            <div className="feedback-banner feedback-banner--error feedback-banner--spaced" role="alert">
              {shouldSurfaceRefreshFailure(backendStatus.refreshStatus)
                ? `${backendStatus.refreshStatus.lastErrorMessage ?? "Il backend locale non è raggiungibile."} Ultimo aggiornamento riuscito: ${formatLastRefresh(backendStatus.refreshStatus.lastSuccessfulRefreshAt)}.`
                : resolveBackendErrorMessage() ??
                  "Il backend locale non è raggiungibile. Le operazioni non sono disponibili. Riavviare l'applicazione."}
            </div>
          )}
          <div hidden={activeSection !== "chat"}>
            <ChatSection
              models={ollamaModels}
              defaultModel={ollamaSettings?.defaultChatModel ?? null}
              ollamaStatus={ollamaStatus}
              loadError={ollamaLoadError}
              documentLibraryVersion={documentLibraryVersion}
              isActive={activeSection === "chat"}
            />
          </div>
          {activeSection === "documents" && <DocumentsSection onLibraryChanged={notifyDocumentLibraryChanged} />}
          {activeSection === "images" && <ImagesSection />}
          {activeSection === "jobs" && (
            <JobsSection onJobsChanged={() => void refreshBackendStatus()} />
          )}
          {activeSection === "translation" && (
            <TranslationSection
              models={ollamaModels}
              defaultModel={ollamaSettings?.defaultTranslationModel ?? null}
              ollamaStatus={ollamaStatus}
              loadError={ollamaLoadError}
            />
          )}
          {activeSection === "settings" && (
            <SettingsSection
              settings={ollamaSettings}
              status={ollamaStatus}
              models={ollamaModels}
              initialDiagnostics={diagnostics}
              loadError={ollamaLoadError}
              onDataChanged={async () => {
                await refreshBackendStatus();
                await refreshOllamaData();
                await refreshDiagnostics().catch(() => {});
              }}
            />
          )}
        </section>
      </main>
      {(initialCheckDone || ollamaSettingsChecked) && activeSection !== "settings" && (
        <InitialSetupWizard
          ollamaStatus={ollamaStatus}
          ollamaInstallStatus={ollamaInstallStatus}
          ollamaSettings={ollamaSettings}
          ollamaModels={ollamaModels}
          ocrAnalysis={ocrStartupPrompt.analysis}
          ocrProvisionStatus={ocrStartupPrompt.provisionStatus}
          ocrLastCheckedAt={ocrStartupPrompt.lastCheckedAt}
          isChecking={isRecheckingOllama}
          isConfiguringOcr={ocrStartupPrompt.isConfiguring}
          onOpenSettings={() => setActiveSection("settings")}
          onInstallOllama={() => void handleInstallOllama()}
          onConfigureOcr={(runtimeTarget) => void ocrStartupPrompt.configure(runtimeTarget)}
          onCancelOcr={() => void ocrStartupPrompt.cancel()}
          onRecheck={() => void handleRecheckInitialSetup()}
        />
      )}
    </div>
  );
}

function formatOllamaBadge(status: OllamaStatusResponse): string {
  if (!status.isReachable) {
    return "Offline";
  }

  return status.installedModelCount === 0 ? "Nessun modello" : `${status.installedModelCount} modelli`;
}

function getOllamaTone(status: OllamaStatusResponse): StatusTone {
  if (!status.isReachable) {
    return "offline";
  }

  return status.installedModelCount === 0 ? "warning" : "online";
}
