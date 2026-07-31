import { useEffect, useRef, useState } from "react";
import {
  apiRequest,
  resolveBackendErrorMessage,
  type DependencyActionResponse,
  type OcrAutoGpuEnableResponse,
  type OllamaStatusResponse
} from "./api";
import { initializeAppLifecycleBridge } from "./appLifecycle";
import { AppHeader } from "./components/AppHeader";
import { ChatSection } from "./components/ChatSection";
import { CodingSection } from "./components/CodingSection";
import { DocumentsSection } from "./components/DocumentsSection";
import { ImagesSection } from "./components/ImagesSection";
import { JobsDrawer } from "./components/JobsDrawer";
import { SectionId, Sidebar } from "./components/Sidebar";
import { SettingsSection } from "./components/SettingsSection";
import { SetupBanner } from "./components/SetupBanner";
import { TranslationSection } from "./components/TranslationSection";
import { useOcrStartupPrompt } from "./components/useOcrStartupPrompt";
import { QueryProvider } from "./context/QueryProvider";
import { ThemeProvider, useTheme } from "./context/ThemeContext";
import { useBackendStatusQuery } from "./hooks/useBackendStatusQuery";
import { useDiagnosticsQuery } from "./hooks/useDiagnosticsQuery";
import { useOllamaStatusQuery } from "./hooks/useOllamaStatusQuery";
import {
  formatLastRefresh,
  initialRefreshStatus,
  markRefreshFailure,
  markRefreshSuccess,
  shouldSurfaceRefreshFailure,
  type RefreshStatus
} from "./pollingStatus";

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
  translation: "Traduzione",
  coding: "Coding",
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

export function AppContent() {
  const { theme } = useTheme();
  const [activeSection, setActiveSection] = useState<SectionId>("coding");
  const [documentLibraryVersion, setDocumentLibraryVersion] = useState(0);
  const [isRecheckingOllama, setIsRecheckingOllama] = useState(false);
  const [initialCheckDone, setInitialCheckDone] = useState(false);
  const [isJobsDrawerOpen, setIsJobsDrawerOpen] = useState(false);
  const initialSetupCheckInProgressRef = useRef(false);
  const ocrStartupPrompt = useOcrStartupPrompt();

  const backendQuery = useBackendStatusQuery();
  const ollamaQuery = useOllamaStatusQuery();
  const diagnosticsQuery = useDiagnosticsQuery();

  const backendStatus: BackendStatus = backendQuery.data
    ? {
        backendValue: backendQuery.data.backend,
        backendTone: "online",
        ollamaValue: ollamaQuery.data?.status ? formatOllamaBadge(ollamaQuery.data.status) : "Offline",
        ollamaTone: ollamaQuery.data?.status ? getOllamaTone(ollamaQuery.data.status) : "offline",
        jobsValue: backendQuery.data.jobQueue,
        jobsTone: "online",
        lowResourceMode: backendQuery.data.lowResourceMode,
        refreshStatus: markRefreshSuccess()
      }
    : {
        ...offlineStatus,
        refreshStatus: markRefreshFailure(
          initialRefreshStatus,
          resolveBackendErrorMessage() ??
            "Il backend locale non è raggiungibile. Le operazioni non sono disponibili. Riavviare l'applicazione."
        )
      };

  const statusChecked = !backendQuery.isLoading;
  const ollamaSettings = ollamaQuery.data?.settings ?? null;
  const ollamaStatus = ollamaQuery.data?.status ?? null;
  const ollamaInstallStatus = ollamaQuery.data?.installStatus ?? null;
  const ollamaModels = ollamaQuery.data?.models ?? [];
  const ollamaLoadError = ollamaQuery.data?.loadError ?? null;
  const ollamaSettingsChecked = Boolean(ollamaQuery.data?.settings || ollamaQuery.data?.status || ollamaQuery.isFetched);
  const diagnostics = diagnosticsQuery.data ?? null;

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
      await ollamaQuery.refetch();
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
        backendQuery.refetch(),
        ollamaQuery.refetch(),
        diagnosticsQuery.refetch(),
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
        await backendQuery.refetch();
        if (isCancelled) return;

        await ollamaQuery.refetch();
        if (isCancelled) return;

        const [diagnosticsResult] = await Promise.all([
          diagnosticsQuery.refetch().then((res) => res.data ?? null).catch(() => null),
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

  useEffect(() => {
    function handleGlobalKeyDown(event: KeyboardEvent) {
      if (event.ctrlKey || event.metaKey) {
        switch (event.key) {
          case "1":
            event.preventDefault();
            setActiveSection("chat");
            setIsJobsDrawerOpen(false);
            break;
          case "2":
            event.preventDefault();
            setActiveSection("coding");
            setIsJobsDrawerOpen(false);
            break;
          case "3":
            event.preventDefault();
            setActiveSection("documents");
            setIsJobsDrawerOpen(false);
            break;
          case "4":
            event.preventDefault();
            setActiveSection("translation");
            setIsJobsDrawerOpen(false);
            break;
          case "5":
            event.preventDefault();
            setActiveSection("images");
            setIsJobsDrawerOpen(false);
            break;
          case "6":
            event.preventDefault();
            setActiveSection("settings");
            setIsJobsDrawerOpen(false);
            break;
        }
      }
    }

    window.addEventListener("keydown", handleGlobalKeyDown);
    return () => window.removeEventListener("keydown", handleGlobalKeyDown);
  }, []);

  const previousSectionRef = useRef<SectionId>(activeSection);
  useEffect(() => {
    if (previousSectionRef.current === "settings" && activeSection !== "settings") {
      void runInitialSetupChecks();
    }
    previousSectionRef.current = activeSection;
  }, [activeSection]);

  return (
    <div className="desktop-shell" data-theme={theme}>
      <a className="skip-link" href="#main-workspace">
        Salta al contenuto principale
      </a>
      <Sidebar
        activeSection={activeSection}
        sections={sectionLabels}
        onSectionChange={(section) => {
          setActiveSection(section);
          setIsJobsDrawerOpen(false);
        }}
        activeJobCount={parseInt(backendStatus.jobsValue, 10) || 0}
        diagnostics={diagnostics}
      />
      <main className="workspace" id="main-workspace" aria-labelledby="workspace-title" tabIndex={-1}>
        <AppHeader
          currentSection={sectionLabels[activeSection]}
          backendStatus={backendStatus}
          diagnostics={diagnostics}
          onOpenJobsDrawer={() => setIsJobsDrawerOpen(true)}
        />
        <section key={activeSection} className={`workspace-content workspace-content--${activeSection} workspace-section-animate`} aria-labelledby="workspace-title">
          {statusChecked && backendStatus.backendTone === "offline" && (
            <div className="feedback-banner feedback-banner--error feedback-banner--spaced" role="alert">
              {shouldSurfaceRefreshFailure(backendStatus.refreshStatus)
                ? `${backendStatus.refreshStatus.lastErrorMessage ?? "Il backend locale non è raggiungibile."} Ultimo aggiornamento riuscito: ${formatLastRefresh(backendStatus.refreshStatus.lastSuccessfulRefreshAt)}.`
                : resolveBackendErrorMessage() ??
                  "Il backend locale non è raggiungibile. Le operazioni non sono disponibili. Riavviare l'applicazione."}
            </div>
          )}
          {(initialCheckDone || ollamaSettingsChecked) && activeSection !== "settings" && (
            <SetupBanner
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
          <div hidden={activeSection !== "chat"} className="chat-section-wrapper">
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
          {activeSection === "translation" && (
            <TranslationSection
              models={ollamaModels}
              defaultModel={ollamaSettings?.defaultTranslationModel ?? null}
              ollamaStatus={ollamaStatus}
              loadError={ollamaLoadError}
            />
          )}
          <div hidden={activeSection !== "coding"} className="coding-section-wrapper">
            <CodingSection
              models={ollamaModels}
              defaultModel={ollamaSettings?.defaultCodingModel ?? ollamaSettings?.defaultChatModel ?? null}
              loadError={ollamaLoadError}
              isActive={activeSection === "coding"}
            />
          </div>
          {activeSection === "settings" && (
            <SettingsSection
              settings={ollamaSettings}
              status={ollamaStatus}
              models={ollamaModels}
              initialDiagnostics={diagnostics}
              loadError={ollamaLoadError}
              onDataChanged={async () => {
                await backendQuery.refetch();
                await ollamaQuery.refetch();
                await diagnosticsQuery.refetch().catch(() => {});
              }}
            />
          )}
        </section>
      </main>
      <JobsDrawer
        isOpen={isJobsDrawerOpen}
        onClose={() => setIsJobsDrawerOpen(false)}
        onJobsChanged={() => void backendQuery.refetch()}
      />
    </div>
  );
}

export default function App() {
  return (
    <QueryProvider>
      <ThemeProvider>
        <AppContent />
      </ThemeProvider>
    </QueryProvider>
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
