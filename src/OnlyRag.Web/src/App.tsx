import { lazy, Suspense, useCallback, useEffect, useRef, useState } from "react";
import { resolveBackendErrorMessage } from "./api";
import { AppHeader } from "./components/layout/AppHeader";
import { JobsDrawer } from "./components/layout/JobsDrawer";
import { SectionId, Sidebar } from "./components/layout/Sidebar";
import { SetupBanner } from "./components/layout/SetupBanner";
import { CommandPaletteModal } from "./components/layout/CommandPaletteModal";
import { PrerequisitesModal } from "./components/layout/PrerequisitesModal";
import { GlobalDropzoneOverlay } from "./components/documents/GlobalDropzoneOverlay";
import { SkeletonSection } from "./components/common/SkeletonSection";
import { QueryProvider } from "./context/QueryProvider";
import { ThemeProvider, useTheme } from "./context/ThemeContext";
import { useAppSetup } from "./hooks/useAppSetup";
import { formatLastRefresh, shouldSurfaceRefreshFailure } from "./pollingStatus";

const ChatSection = lazy(() => import("./components/chat/ChatSection").then(m => ({ default: m.ChatSection })));
const CodingSection = lazy(() => import("./components/coding/CodingSection").then(m => ({ default: m.CodingSection })));
const DocumentsSection = lazy(() => import("./components/documents/DocumentsSection").then(m => ({ default: m.DocumentsSection })));
const ImagesSection = lazy(() => import("./components/images/ImagesSection").then(m => ({ default: m.ImagesSection })));
const TranslationSection = lazy(() => import("./components/translation/TranslationSection").then(m => ({ default: m.TranslationSection })));
const GraphSection = lazy(() => import("./components/graph/KnowledgeGraphSection").then(m => ({ default: m.KnowledgeGraphSection })));
const SettingsSection = lazy(() => import("./components/settings/SettingsSection").then(m => ({ default: m.SettingsSection })));

export type { BackendStatus } from "./hooks/useAppSetup";

const sectionLabels: Record<SectionId, string> = {
  chat: "Chat",
  documents: "Documenti",
  graph: "Grafo",
  images: "Immagini",
  translation: "Traduzione",
  coding: "Coding",
  settings: "Impostazioni"
};


export function AppContent() {
  const { theme } = useTheme();
  const [activeSection, setActiveSection] = useState<SectionId>(
    () => (new URLSearchParams(window.location.search).get("section") as SectionId) || "coding"
  );
  const [documentLibraryVersion, setDocumentLibraryVersion] = useState(0);
  const [isJobsDrawerOpen, setIsJobsDrawerOpen] = useState(false);
  const [isCommandPaletteOpen, setIsCommandPaletteOpen] = useState(false);
  const [isPrerequisitesModalOpen, setIsPrerequisitesModalOpen] = useState(false);

  const setup = useAppSetup();

  const handleSectionChange = useCallback((section: SectionId) => {
    setActiveSection(section);
    setIsJobsDrawerOpen(false);
  }, []);

  function notifyDocumentLibraryChanged() {
    setDocumentLibraryVersion((current) => current + 1);
  }

  useEffect(() => {
    function handleGlobalKeyDown(event: KeyboardEvent) {
      if (event.ctrlKey || event.metaKey) {
        const key = event.key.toLowerCase();
        const code = event.code;
        if (key === "k" || code === "KeyK") {
          event.preventDefault();
          setIsCommandPaletteOpen((prev) => !prev);
          return;
        }
        if (key === "1" || code === "Digit1" || code === "Numpad1") {
          event.preventDefault();
          setActiveSection("chat");
          setIsJobsDrawerOpen(false);
        } else if (key === "2" || code === "Digit2" || code === "Numpad2") {
          event.preventDefault();
          setActiveSection("coding");
          setIsJobsDrawerOpen(false);
        } else if (key === "3" || code === "Digit3" || code === "Numpad3") {
          event.preventDefault();
          setActiveSection("documents");
          setIsJobsDrawerOpen(false);
        } else if (key === "4" || code === "Digit4" || code === "Numpad4") {
          event.preventDefault();
          setActiveSection("translation");
          setIsJobsDrawerOpen(false);
        } else if (key === "5" || code === "Digit5" || code === "Numpad5") {
          event.preventDefault();
          setActiveSection("images");
          setIsJobsDrawerOpen(false);
        } else if (key === "6" || code === "Digit6" || code === "Numpad6") {
          event.preventDefault();
          setActiveSection("settings");
          setIsJobsDrawerOpen(false);
        }
      }
    }

    window.addEventListener("keydown", handleGlobalKeyDown);
    return () => window.removeEventListener("keydown", handleGlobalKeyDown);
  }, []);

  const previousSectionRef = useRef<SectionId>(activeSection);
  useEffect(() => {
    if (previousSectionRef.current === "settings" && activeSection !== "settings") {
      void setup.runInitialSetupChecks();
    }
    previousSectionRef.current = activeSection;
  }, [activeSection, setup]);

  const [droppedCodingFiles, setDroppedCodingFiles] = useState<FileList | null>(null);

  const handleGlobalFilesDropped = useCallback((files: FileList) => {
    if (activeSection === "coding") {
      setDroppedCodingFiles(files);
    } else {
      setActiveSection("documents");
    }
  }, [activeSection]);

  return (
    <div className="desktop-shell" data-theme={theme}>
      <GlobalDropzoneOverlay
        activeSection={activeSection}
        onFilesDropped={handleGlobalFilesDropped}
      />
      <a className="skip-link" href="#main-workspace">
        Salta al contenuto principale
      </a>
      <Sidebar
        activeSection={activeSection}
        sections={sectionLabels}
        onSectionChange={handleSectionChange}
        activeJobCount={parseInt(setup.backendStatus.jobsValue, 10) || 0}
        diagnostics={setup.diagnostics}
      />
      <main className="workspace" id="main-workspace" aria-labelledby="workspace-title" tabIndex={-1}>
        <AppHeader
          currentSection={sectionLabels[activeSection]}
          backendStatus={setup.backendStatus}
          diagnostics={setup.diagnostics}
          ocrProvisionStatus={setup.ocrStartupPrompt.provisionStatus}
          isInitialChecking={!setup.initialCheckDone}
          onOpenJobsDrawer={() => setIsJobsDrawerOpen(true)}
          onOpenCommandPalette={() => setIsCommandPaletteOpen(true)}
        />
        <section key={activeSection} className={`workspace-content workspace-content--${activeSection} workspace-section-animate`} aria-labelledby="workspace-title">
          {setup.statusChecked && setup.backendStatus.backendTone === "offline" && (
            <div className="feedback-banner feedback-banner--error feedback-banner--spaced" role="alert">
              {shouldSurfaceRefreshFailure(setup.backendStatus.refreshStatus)
                ? `${setup.backendStatus.refreshStatus.lastErrorMessage ?? "Il backend locale non è raggiungibile."} Ultimo aggiornamento riuscito: ${formatLastRefresh(setup.backendStatus.refreshStatus.lastSuccessfulRefreshAt)}.`
                : resolveBackendErrorMessage() ??
                  "Il backend locale non è raggiungibile. Le operazioni non sono disponibili. Riavviare l'applicazione."}
            </div>
          )}
          {(setup.initialCheckDone || setup.ollamaSettingsChecked) && activeSection !== "settings" && (
            <SetupBanner
              ollamaStatus={setup.ollamaStatus}
              ollamaInstallStatus={setup.ollamaInstallStatus}
              ollamaSettings={setup.ollamaSettings}
              ollamaModels={setup.ollamaModels}
              ocrAnalysis={setup.ocrStartupPrompt.analysis}
              ocrProvisionStatus={setup.ocrStartupPrompt.provisionStatus}
              ocrLastCheckedAt={setup.ocrStartupPrompt.lastCheckedAt}
              isChecking={setup.isRecheckingOllama}
              isConfiguringOcr={setup.ocrStartupPrompt.isConfiguring}
              onOpenSettings={() => setActiveSection("settings")}
              onInstallOllama={() => void setup.handleInstallOllama()}
              onConfigureOcr={(runtimeTarget) => void setup.ocrStartupPrompt.configure(runtimeTarget)}
              onCancelOcr={() => void setup.ocrStartupPrompt.cancel()}
              onRecheck={() => void setup.handleRecheckInitialSetup()}
              onOpenPrerequisitesModal={() => setIsPrerequisitesModalOpen(true)}
            />
          )}
          <div hidden={activeSection !== "chat"} className="chat-section-wrapper">
            <ChatSection
              models={setup.ollamaModels}
              defaultModel={setup.ollamaSettings?.defaultChatModel ?? null}
              ollamaStatus={setup.ollamaStatus}
              loadError={setup.ollamaLoadError}
              documentLibraryVersion={documentLibraryVersion}
              isActive={activeSection === "chat"}
            />
          </div>
          <Suspense fallback={<SkeletonSection />}>
            {activeSection === "documents" && <DocumentsSection onLibraryChanged={notifyDocumentLibraryChanged} />}
            {activeSection === "graph" && <GraphSection />}
            {activeSection === "images" && <ImagesSection />}

            {activeSection === "translation" && (
              <TranslationSection
                models={setup.ollamaModels}
                defaultModel={setup.ollamaSettings?.defaultTranslationModel ?? null}
                ollamaStatus={setup.ollamaStatus}
                loadError={setup.ollamaLoadError}
              />
            )}
            <div hidden={activeSection !== "coding"} className="coding-section-wrapper">
              <CodingSection
                models={setup.ollamaModels}
                defaultModel={setup.ollamaSettings?.defaultCodingModel ?? setup.ollamaSettings?.defaultChatModel ?? null}
                loadError={setup.ollamaLoadError}
                isActive={activeSection === "coding"}
                droppedFiles={droppedCodingFiles}
                onHandledDroppedFiles={() => setDroppedCodingFiles(null)}
              />
            </div>
            {activeSection === "settings" && (
              <SettingsSection
                settings={setup.ollamaSettings}
                status={setup.ollamaStatus}
                models={setup.ollamaModels}
                initialDiagnostics={setup.diagnostics}
                loadError={setup.ollamaLoadError}
                onDataChanged={async () => {
                  await setup.backendQuery.refetch();
                  await setup.ollamaQuery.refetch();
                  await setup.diagnosticsQuery.refetch().catch(() => {});
                }}
              />
            )}
          </Suspense>
        </section>
      </main>
      <JobsDrawer
        isOpen={isJobsDrawerOpen}
        onClose={() => setIsJobsDrawerOpen(false)}
        onJobsChanged={() => void setup.backendQuery.refetch()}
      />
      <CommandPaletteModal
        isOpen={isCommandPaletteOpen}
        onClose={() => setIsCommandPaletteOpen(false)}
        onSelectSection={(section) => {
          setActiveSection(section);
          setIsJobsDrawerOpen(false);
        }}
      />
      <PrerequisitesModal
        isOpen={isPrerequisitesModalOpen}
        onClose={() => setIsPrerequisitesModalOpen(false)}
        ocrAnalysis={setup.ocrStartupPrompt.analysis}
        ocrProvisionStatus={setup.ocrStartupPrompt.provisionStatus}
        isConfiguring={setup.ocrStartupPrompt.isConfiguring}
        onConfigureOcr={(target) => void setup.ocrStartupPrompt.configure(target)}
        onCancelOcr={() => void setup.ocrStartupPrompt.cancel()}
        onInstallOllama={() => void setup.handleInstallOllama()}
        ollamaInstalled={Boolean(setup.ollamaInstallStatus?.cliInstalled)}
        onOpenLibreOfficeDownload={() => void setup.handleOpenLibreOfficeDownload()}
      />
    </div>
  );
}


import { SignalRProvider } from "./context/SignalRContext";

export default function App() {
  return (
    <QueryProvider>
      <ThemeProvider>
        <SignalRProvider>
          <AppContent />
        </SignalRProvider>
      </ThemeProvider>
    </QueryProvider>
  );
}
