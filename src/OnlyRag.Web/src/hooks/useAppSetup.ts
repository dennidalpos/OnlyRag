import { useEffect, useRef, useState } from "react";
import {
  apiRequest,
  resolveBackendErrorMessage,
  type DependencyActionResponse,
  type OcrAutoGpuEnableResponse,
  type OllamaStatusResponse
} from "../api";
import { initializeAppLifecycleBridge } from "../appLifecycle";
import { useOcrStartupPrompt } from "../components/layout/useOcrStartupPrompt";
import { useBackendStatusQuery } from "./useBackendStatusQuery";
import { useDiagnosticsQuery } from "./useDiagnosticsQuery";
import { useOllamaStatusQuery } from "./useOllamaStatusQuery";
import {
  initialRefreshStatus,
  markRefreshFailure,
  markRefreshSuccess,
  type RefreshStatus
} from "../pollingStatus";

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

export function useAppSetup() {
  const [isRecheckingOllama, setIsRecheckingOllama] = useState(false);
  const [initialCheckDone, setInitialCheckDone] = useState(false);
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

  async function handleOpenLibreOfficeDownload() {
    await apiRequest<DependencyActionResponse>("/api/dependencies/libreoffice/open-download", {
      method: "POST",
      body: JSON.stringify({ confirmed: true })
    }).catch(() => {});
  }

  const lastSetupCheckTimeRef = useRef<number>(0);

  async function runInitialSetupChecks({ showBusy = false, force = false }: { showBusy?: boolean; force?: boolean } = {}) {
    const now = Date.now();
    if (initialSetupCheckInProgressRef.current || (!force && now - lastSetupCheckTimeRef.current < 15000)) {
      return;
    }

    initialSetupCheckInProgressRef.current = true;
    lastSetupCheckTimeRef.current = now;
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
    await runInitialSetupChecks({ showBusy: true, force: true });
  }

  useEffect(() => {
    initializeAppLifecycleBridge();
  }, []);

  useEffect(() => {
    let isCancelled = false;

    async function load() {
      initialSetupCheckInProgressRef.current = true;
      try {
        // Stage 1: confirm backend is alive before launching heavy probes
        await backendQuery.refetch();
        if (isCancelled) return;

        // Stage 2: run remaining probes in parallel once backend is confirmed
        const diagnosticsResultPromise = diagnosticsQuery.refetch().then((res) => res.data ?? null).catch(() => null);
        await Promise.all([
          ollamaQuery.refetch(),
          diagnosticsResultPromise
        ]);
        const diagnosticsResult = await diagnosticsResultPromise;

        if (isCancelled) return;

        if (diagnosticsResult?.ocrGpuCapability.isUsable) {
          await autoEnableOcrGpu().catch(() => {});
        }
        if (!isCancelled) {
          setInitialCheckDone(true);
        }
        void ocrStartupPrompt.refresh();
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




  return {
    backendStatus,
    statusChecked,
    ollamaSettings,
    ollamaStatus,
    ollamaInstallStatus,
    ollamaModels,
    ollamaLoadError,
    ollamaSettingsChecked,
    diagnostics,
    initialCheckDone,
    isRecheckingOllama,
    ocrStartupPrompt,
    backendQuery,
    ollamaQuery,
    diagnosticsQuery,
    handleInstallOllama,
    handleOpenLibreOfficeDownload,
    handleRecheckInitialSetup,
    runInitialSetupChecks
  };
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
