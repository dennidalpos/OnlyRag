import { useEffect, useRef, useState } from "react";
import {
  apiRequest,
  markBackendOffline,
  markBackendOnline,
  resolveBackendErrorMessage,
  type DependencyActionResponse,
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
import { JobsSection } from "./components/JobsSection";
import { OllamaSetupGate } from "./components/OllamaSetupGate";
import { SectionId, Sidebar } from "./components/Sidebar";
import { SettingsSection } from "./components/SettingsSection";
import { TranslationSection } from "./components/TranslationSection";
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
  const [ollamaLoadError, setOllamaLoadError] = useState<string | null>(null);
  const [isRecheckingOllama, setIsRecheckingOllama] = useState(false);
  const [initialCheckDone, setInitialCheckDone] = useState(false);

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

  async function handleRecheckOllama() {
    setIsRecheckingOllama(true);
    try {
      await refreshOllamaData();
    } finally {
      setIsRecheckingOllama(false);
    }
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

  useEffect(() => {
    initializeAppLifecycleBridge();
  }, []);

  useEffect(() => {
    let isCancelled = false;

    async function load() {
      await refreshBackendStatus();
      if (isCancelled) {
        return;
      }

      await refreshOllamaData();
      if (!isCancelled) {
        setInitialCheckDone(true);
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
    }, 10_000);
    return () => clearInterval(handle);
  }, []);

  const previousSectionRef = useRef<SectionId>(activeSection);
  useEffect(() => {
    if (previousSectionRef.current === "settings" && activeSection !== "settings") {
      void refreshOllamaData();
    }
    previousSectionRef.current = activeSection;
  }, [activeSection]);

  return (
    <div className="desktop-shell">
      <Sidebar
        activeSection={activeSection}
        sections={sectionLabels}
        onSectionChange={setActiveSection}
        activeJobCount={parseInt(backendStatus.jobsValue, 10) || 0}
      />
      <main className="workspace">
        <AppHeader currentSection={sectionLabels[activeSection]} backendStatus={backendStatus} />
        <section className={`workspace-content workspace-content--${activeSection}`} aria-label={sectionLabels[activeSection]}>
          {statusChecked && backendStatus.backendTone === "offline" && (
            <div className="feedback-banner feedback-banner--error feedback-banner--spaced" role="alert">
              {shouldSurfaceRefreshFailure(backendStatus.refreshStatus)
                ? `${backendStatus.refreshStatus.lastErrorMessage ?? "Il backend locale non è raggiungibile."} Ultimo aggiornamento riuscito: ${formatLastRefresh(backendStatus.refreshStatus.lastSuccessfulRefreshAt)}.`
                : resolveBackendErrorMessage() ??
                  "Il backend locale non è raggiungibile. Le operazioni non sono disponibili. Riavviare l'applicazione."}
            </div>
          )}
          {activeSection === "chat" && (
            <ChatSection
              models={ollamaModels}
              defaultModel={ollamaSettings?.defaultChatModel ?? null}
              ollamaStatus={ollamaStatus}
              loadError={ollamaLoadError}
            />
          )}
          {activeSection === "documents" && <DocumentsSection />}
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
              loadError={ollamaLoadError}
              onDataChanged={async () => {
                await refreshBackendStatus();
                await refreshOllamaData();
              }}
            />
          )}
        </section>
      </main>
      {initialCheckDone && activeSection !== "settings" && (
        <OllamaSetupGate
          ollamaStatus={ollamaStatus}
          ollamaInstallStatus={ollamaInstallStatus}
          ollamaSettings={ollamaSettings}
          ollamaModels={ollamaModels}
          isChecking={isRecheckingOllama}
          onOpenSettings={() => setActiveSection("settings")}
          onInstallOllama={() => void handleInstallOllama()}
          onRecheck={() => void handleRecheckOllama()}
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
