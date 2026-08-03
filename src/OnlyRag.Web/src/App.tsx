import { useEffect, useRef, useState } from "react";
import { resolveBackendErrorMessage } from "./api";
import { AppHeader } from "./components/layout/AppHeader";
import { ChatSection } from "./components/chat/ChatSection";
import { CodingSection } from "./components/coding/CodingSection";
import { DocumentsSection } from "./components/documents/DocumentsSection";
import { ImagesSection } from "./components/images/ImagesSection";
import { JobsDrawer } from "./components/layout/JobsDrawer";
import { SectionId, Sidebar } from "./components/layout/Sidebar";
import { SettingsSection } from "./components/settings/SettingsSection";
import { SetupBanner } from "./components/layout/SetupBanner";
import { TranslationSection } from "./components/translation/TranslationSection";
import { CommandPaletteModal } from "./components/layout/CommandPaletteModal";
import { QueryProvider } from "./context/QueryProvider";
import { ThemeProvider, useTheme } from "./context/ThemeContext";
import { useAppSetup } from "./hooks/useAppSetup";
import { formatLastRefresh, shouldSurfaceRefreshFailure } from "./pollingStatus";

export type { BackendStatus } from "./hooks/useAppSetup";

const sectionLabels: Record<SectionId, string> = {
  chat: "Chat",
  documents: "Documenti",
  images: "Immagini",
  translation: "Traduzione",
  coding: "Coding",
  settings: "Impostazioni"
};

export function AppContent() {
  const { theme } = useTheme();
  const [activeSection, setActiveSection] = useState<SectionId>("coding");
  const [documentLibraryVersion, setDocumentLibraryVersion] = useState(0);
  const [isJobsDrawerOpen, setIsJobsDrawerOpen] = useState(false);
  const [isCommandPaletteOpen, setIsCommandPaletteOpen] = useState(false);

  const setup = useAppSetup();

  function notifyDocumentLibraryChanged() {
    setDocumentLibraryVersion((current) => current + 1);
  }

  useEffect(() => {
    function handleGlobalKeyDown(event: KeyboardEvent) {
      if (event.ctrlKey || event.metaKey) {
        if (event.key.toLowerCase() === "k") {
          event.preventDefault();
          setIsCommandPaletteOpen((prev) => !prev);
          return;
        }
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
      void setup.runInitialSetupChecks();
    }
    previousSectionRef.current = activeSection;
  }, [activeSection, setup]);

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
        activeJobCount={parseInt(setup.backendStatus.jobsValue, 10) || 0}
        diagnostics={setup.diagnostics}
      />
      <main className="workspace" id="main-workspace" aria-labelledby="workspace-title" tabIndex={-1}>
        <AppHeader
          currentSection={sectionLabels[activeSection]}
          backendStatus={setup.backendStatus}
          diagnostics={setup.diagnostics}
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
          {activeSection === "documents" && <DocumentsSection onLibraryChanged={notifyDocumentLibraryChanged} />}
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
