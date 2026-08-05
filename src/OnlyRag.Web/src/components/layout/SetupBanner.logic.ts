import type {
  OcrProvisionStatus,
  OcrStartupAnalysis,
  OllamaInstallStatus,
  OllamaModel,
  OllamaSettings,
  OllamaStatusResponse
} from "../../api";

export type SetupIssue = {
  key: string;
  title: string;
  detail: string;
  tone?: "warning" | "running" | "success";
  badge?: string;
  action?: "install-ollama" | "configure-ocr" | "cancel-ocr";
  actionLabel?: string;
  runtimeTarget?: "auto" | "cpu" | "nvidia";
  installCommand?: string | null;
  networkAccessHint?: string | null;
  isRunning?: boolean;
};

export function detectSetupIssues(
  ollamaStatus: OllamaStatusResponse | null,
  ollamaInstallStatus: OllamaInstallStatus | null,
  ollamaSettings: OllamaSettings | null,
  ollamaModels: OllamaModel[],
  ocrAnalysis: OcrStartupAnalysis | null,
  ocrProvisionStatus: OcrProvisionStatus | null
): SetupIssue[] {
  const issues: SetupIssue[] = [];

  issues.push(...detectOllamaIssues(ollamaStatus, ollamaInstallStatus, ollamaSettings, ollamaModels));

  const hasVerifiedOcrRuntime = Boolean(ocrProvisionStatus?.isConfigured && !ocrProvisionStatus.isRunning);
  if (ocrProvisionStatus?.isRunning) {
    issues.push({
      key: "ocr",
      title: ocrProvisionStatus.isAutomaticRepair ? "Riparazione automatica OCR in corso" : "Configurazione OCR in corso",
      detail: ocrProvisionStatus.message,
      tone: "running",
      badge: ocrProvisionStatus.resolvedRuntime,
      action: "cancel-ocr",
      actionLabel: "Annulla OCR",
      isRunning: true
    });
  } else if (ocrProvisionStatus && isFailedOcrProvisionStatus(ocrProvisionStatus)) {
    issues.push({
      key: "ocr",
      title: ocrProvisionStatus.isAutomaticRepair
        ? "Riparazione automatica OCR non completata"
        : ocrProvisionStatus.resolvedRuntime === "cancelled"
        ? "Configurazione OCR annullata"
        : "Configurazione OCR non completata",
      detail: ocrProvisionStatus.message,
      badge: ocrProvisionStatus.resolvedRuntime,
      action: "configure-ocr",
      actionLabel: "Riprova OCR"
    });
  } else if (ocrProvisionStatus && isRepairableOcrRuntimeStatus(ocrProvisionStatus)) {
    issues.push({
      key: "ocr",
      title: "Runtime OCR da riparare",
      detail: formatRepairableOcrRuntimeDetail(ocrProvisionStatus.message),
      badge: getKnownRuntimeBadge(ocrProvisionStatus.resolvedRuntime),
      action: "configure-ocr",
      actionLabel: "Ripara OCR"
    });
  } else if (ocrAnalysis?.shouldPrompt && !hasVerifiedOcrRuntime) {
    issues.push({
      key: "ocr",
      title: ocrAnalysis.title,
      detail: ocrProvisionStatus?.message ?? ocrAnalysis.message,
      badge: ocrAnalysis.recommendedRuntimeTarget === "nvidia" ? "NVIDIA GPU" : "CPU",
      action: "configure-ocr",
      actionLabel: ocrAnalysis.recommendedRuntimeTarget === "nvidia" ? "Installa OCR GPU" : "Installa OCR CPU",
      runtimeTarget: ocrAnalysis.recommendedRuntimeTarget
    });
  }

  return issues;
}

export function detectSetupStatusItems(ocrProvisionStatus: OcrProvisionStatus | null): SetupIssue[] {
  if (!ocrProvisionStatus?.isConfigured) {
    return [];
  }

  return [
    {
      key: "ocr",
      title: "OCR configurato",
      detail: ocrProvisionStatus.message,
      tone: "success",
      badge: ocrProvisionStatus.resolvedRuntime
    }
  ];
}

export function formatSetupTime(value: Date | null): string {
  if (!value) {
    return "non ancora eseguita";
  }

  return value.toLocaleTimeString("it-IT", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });
}

export function formatSetupDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString("it-IT", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });
}

function isFailedOcrProvisionStatus(status: OcrProvisionStatus): boolean {
  return Boolean(status.lastError)
    || status.resolvedRuntime === "cancelled"
    || status.message.startsWith("Configurazione OCR non completata");
}

function isRepairableOcrRuntimeStatus(status: OcrProvisionStatus): boolean {
  return status.message.startsWith("Runtime OCR locale incompleto o danneggiato.");
}

function getKnownRuntimeBadge(value: string | null | undefined): string | undefined {
  if (!value || value === "unknown") {
    return undefined;
  }

  return value;
}

function formatRepairableOcrRuntimeDetail(message: string): string {
  if (!message.startsWith("Runtime OCR locale incompleto o danneggiato.")) {
    return message;
  }

  return "Runtime OCR locale incompleto o danneggiato. Premi Ripara OCR per reinstallare PaddleOCR e il runtime PaddlePaddle corretto.";
}

function detectOllamaIssues(
  ollamaStatus: OllamaStatusResponse | null,
  ollamaInstallStatus: OllamaInstallStatus | null,
  ollamaSettings: OllamaSettings | null,
  ollamaModels: OllamaModel[]
): SetupIssue[] {
  const missingModelIssues = detectMissingRequiredModelIssues(ollamaSettings);

  if (ollamaInstallStatus && !ollamaInstallStatus.cliInstalled) {
    return [{
      key: "ollama",
      title: "Ollama non installato",
      detail: "Installa Ollama manualmente per usare chat, embedding e traduzione con modelli locali.",
      action: "install-ollama",
      actionLabel: "Apri download Ollama",
      installCommand: ollamaInstallStatus.installCommand,
      networkAccessHint: ollamaInstallStatus.networkAccessHint
    }, ...missingModelIssues];
  }

  if (!ollamaStatus || !ollamaStatus.isReachable) {
    return [{
      key: "ollama",
      title: "Ollama non raggiungibile",
      detail: "Avvia Ollama e verifica l'indirizzo nelle Impostazioni.",
      networkAccessHint: ollamaInstallStatus?.networkAccessHint ?? null
    }, ...missingModelIssues];
  }

  const modelIssues = [
    detectRequiredModelIssue({
      key: "chat-model",
      modelName: ollamaSettings?.defaultChatModel,
      missingTitle: "Modello chat non configurato",
      missingDetail: "Seleziona un modello da usare per la chat nelle Impostazioni."
    }),
    detectRequiredModelIssue({
      key: "embedding-model",
      modelName: ollamaSettings?.defaultEmbeddingModel,
      missingTitle: "Modello embedding non configurato",
      missingDetail: "Seleziona un modello da usare per l'indicizzazione dei documenti nelle Impostazioni."
    }),
    detectRequiredModelIssue({
      key: "translation-model",
      modelName: ollamaSettings?.defaultTranslationModel,
      missingTitle: "Modello traduzione non configurato",
      missingDetail: "Seleziona un modello da usare per la traduzione dei documenti nelle Impostazioni."
    })
  ].filter((issue): issue is SetupIssue => issue !== null);

  if (ollamaModels.length === 0) {
    return [{
      key: "models",
      title: "Nessun modello installato",
      detail:
        "Installa almeno un modello chat e un modello embedding in Ollama, poi selezionali nelle Impostazioni."
    }, ...modelIssues];
  }

  return modelIssues;
}

function detectMissingRequiredModelIssues(ollamaSettings: OllamaSettings | null): SetupIssue[] {
  return [
    detectMissingRequiredModelIssue({
      key: "chat-model",
      modelName: ollamaSettings?.defaultChatModel,
      missingTitle: "Modello chat non configurato",
      missingDetail: "Seleziona un modello da usare per la chat nelle Impostazioni."
    }),
    detectMissingRequiredModelIssue({
      key: "embedding-model",
      modelName: ollamaSettings?.defaultEmbeddingModel,
      missingTitle: "Modello embedding non configurato",
      missingDetail: "Seleziona un modello da usare per l'indicizzazione dei documenti nelle Impostazioni."
    }),
    detectMissingRequiredModelIssue({
      key: "translation-model",
      modelName: ollamaSettings?.defaultTranslationModel,
      missingTitle: "Modello traduzione non configurato",
      missingDetail: "Seleziona un modello da usare per la traduzione dei documenti nelle Impostazioni."
    })
  ].filter((issue): issue is SetupIssue => issue !== null);
}

function detectMissingRequiredModelIssue({
  key,
  modelName,
  missingTitle,
  missingDetail
}: {
  key: string;
  modelName: string | null | undefined;
  missingTitle: string;
  missingDetail: string;
}): SetupIssue | null {
  if (!isPlaceholderModelValue(modelName)) {
    return null;
  }

  return {
    key,
    title: missingTitle,
    detail: missingDetail
  };
}

function detectRequiredModelIssue({
  key,
  modelName,
  missingTitle,
  missingDetail
}: {
  key: string;
  modelName: string | null | undefined;
  missingTitle: string;
  missingDetail: string;
}): SetupIssue | null {
  if (isPlaceholderModelValue(modelName)) {
    return {
      key,
      title: missingTitle,
      detail: missingDetail
    };
  }

  return null;
}

function isPlaceholderModelValue(value: string | null | undefined): boolean {
  const normalized = value?.trim() ?? "";
  return normalized.length === 0
    || normalized === "Nessun modello selezionato";
}
