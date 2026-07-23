import type {
  ImageGenerationSettings,
  ImageModelCatalogEntry
} from "../../api";

export const defaultModelId = "lcm-sdxl-olive-onnx";

export const defaultSettings: ImageGenerationSettings = {
  selectedModelId: defaultModelId,
  requestTimeoutSeconds: 300,
  preferGpu: true
};

export type ResolutionPreset = {
  label: string;
  width: number;
  height: number;
};

export function getCompatiblePresets(selectedModel: ImageModelCatalogEntry | null): ResolutionPreset[] {
  const supported = selectedModel?.supportedResolutions ?? ["1024x1024", "832x1216", "1216x832"];
  const presets: ResolutionPreset[] = [];

  for (const item of supported) {
    const parts = item.trim().toLowerCase().split("x");
    if (parts.length === 2) {
      const w = parseInt(parts[0], 10);
      const h = parseInt(parts[1], 10);
      if (!isNaN(w) && !isNaN(h) && w > 0 && h > 0) {
        let arLabel = "";
        const ratio = w / h;
        if (Math.abs(ratio - 1.0) < 0.02) arLabel = " (1:1)";
        else if (Math.abs(ratio - (16 / 9)) < 0.05) arLabel = " (16:9)";
        else if (Math.abs(ratio - (9 / 16)) < 0.05) arLabel = " (9:16)";
        else if (Math.abs(ratio - (3 / 2)) < 0.05) arLabel = " (3:2)";
        else if (Math.abs(ratio - (2 / 3)) < 0.05) arLabel = " (2:3)";
        else if (Math.abs(ratio - (4 / 3)) < 0.05) arLabel = " (4:3)";
        else if (Math.abs(ratio - (3 / 4)) < 0.05) arLabel = " (3:4)";

        presets.push({
          label: `${w}x${h}${arLabel}`,
          width: w,
          height: h
        });
      }
    }
  }

  return presets.length > 0 ? presets : [{ label: "1024x1024 (1:1)", width: 1024, height: 1024 }];
}

export const modelTemplates = [
  {
    id: "sdxl-turbo-onnx",
    displayName: "SDXL Turbo ONNX",
    downloadUrl: "https://huggingface.co/optimum/sdxl-turbo-onnx",
    licenseLabel: "OpenRAIL-M",
    expectedSizeBytes: "8000000000",
    modelType: "SDXL Turbo ONNX",
    modelProfile: "sdxl-turbo",
    supportedResolutions: "512x512, 1024x1024",
    defaultSteps: "2",
    defaultGuidance: "0.0",
    scheduler: "EulerAncestral",
    compatibilityNotes: "DirectML GPU consigliato per generazioni ultra veloci (1-4 step)."
  },
  {
    id: "sdxl-base-1.0-onnx",
    displayName: "SDXL Base 1.0 ONNX",
    downloadUrl: "https://huggingface.co/optimum/sdxl-base-1.0-onnx",
    licenseLabel: "OpenRAIL-M",
    expectedSizeBytes: "12000000000",
    modelType: "SDXL Base ONNX",
    modelProfile: "sdxl-base",
    supportedResolutions: "1024x1024, 832x1216, 1216x832",
    defaultSteps: "30",
    defaultGuidance: "5.0",
    scheduler: "Euler",
    compatibilityNotes: "Richiede DirectML GPU consigliata con almeno 8GB VRAM."
  },
  {
    id: "lcm-sdxl-olive-onnx",
    displayName: "LCM SDXL Olive ONNX",
    downloadUrl: "https://huggingface.co/softwareweaver/Latent-Consistency-xl-Olive-Onnx",
    licenseLabel: "OpenRAIL++",
    expectedSizeBytes: "8000000000",
    modelType: "SDXL Turbo/LCM ONNX",
    modelProfile: "lcm-sdxl-olive",
    supportedResolutions: "1024x1024, 832x1216, 1216x832",
    defaultSteps: "6",
    defaultGuidance: "1.0",
    scheduler: "LCM",
    compatibilityNotes: "DirectML GPU preferred on Windows; CPU fallback supported."
  }
];

export const promptLanguages = [
  { value: "en", label: "Inglese (English)" },
  { value: "it", label: "Italiano" },
  { value: "es", label: "Español" },
  { value: "fr", label: "Français" },
  { value: "de", label: "Deutsch" }
] as const;

export type PromptLanguage = (typeof promptLanguages)[number]["value"];

export const generationProfiles = [
  { label: "Qualità", value: "quality" },
  { label: "Bilanciato", value: "balanced" },
  { label: "Performance", value: "performance" },
  { label: "Custom", value: "custom" }
] as const;

export type GenerationProfile = (typeof generationProfiles)[number]["value"];

export const imageTooltips = {
  generationProfile: "Regola solo parametri tecnici. Le modifiche manuali passano a custom.",
  format: "Imposta larghezza e altezza con formati stabili per SDXL.",
  negativePrompt: "Opzionale. Viene inviato esattamente come scritto.",
  seed: "Opzionale. Ripete una generazione simile quando usi lo stesso prompt e modello.",
  steps: "Numero di step di inferenza usati dal runtime locale.",
  guidanceScale: "Forza la guidance scale del runtime. Lascia vuoto per usare il default del modello.",
  batchSize: "Numero di immagini prodotte nella stessa richiesta.",
  model: "Scegli un modello locale o manuale configurato nel catalogo.",
  downloadConsent: "Il download parte solo dopo conferma esplicita e salva i file nel profilo locale.",
  catalogEditor: "Modifica solo snapshot ONNX SDXL compatibili e verifica licenza, file richiesti e hash.",
  downloadUrl: "URL del repository o del file modello da scaricare nel profilo locale.",
  recommendedProfile: "Nota breve mostrata nello stato modello per guidare la scelta.",
  modelType: "Tipo runtime atteso dal motore ONNX locale.",
  modelProfile: "Profilo tecnico del modello, ad esempio SDXL base, turbo o LCM.",
  supportedResolutions: "Risoluzioni supportate separate da virgole nel formato larghezzaxaltezza.",
  defaultSteps: "Step consigliati quando il profilo generazione non viene personalizzato.",
  defaultGuidance: "Guidance scale attesa dal modello o scheduler.",
  scheduler: "Scheduler o aspettativa di timestep richiesta dal modello.",
  compatibilityNotes: "Note operative per DirectML, CPU, GPU e VRAM.",
  expectedSize: "Dimensione attesa in byte; 0 indica dimensione non dichiarata.",
  requiredFiles: "Elenco separato da virgole dei file che rendono lo snapshot utilizzabile.",
  sha256: "Hash opzionale del file modello singolo. Lascia vuoto per snapshot verificati dai file richiesti.",
  preferGpu: "Usa DirectML quando disponibile; se fallisce, il backend puo ripiegare su CPU.",
  timeout: "Tempo massimo concesso a una generazione prima di interrompere la richiesta."
};

export type Feedback = {
  tone: "success" | "error" | "warning";
  message: string;
};

export type ModelDraft = {
  id: string;
  displayName: string;
  recommendedProfile: string;
  modelType: string;
  modelProfile: string;
  supportedResolutions: string;
  defaultSteps: string;
  defaultGuidance: string;
  scheduler: string;
  compatibilityNotes: string;
  downloadUrl: string;
  licenseLabel: string;
  expectedSizeBytes: string;
  requiredFiles: string;
  sha256: string;
};

export type CropSelection = {
  x: number;
  y: number;
  width: number;
  height: number;
};

export type EditTool = "move" | "crop" | "text" | "arrow";

export type TextLayer = {
  id: number;
  text: string;
  x: number;
  y: number;
  fontSize: number;
  color: string;
};

export type ArrowLayer = {
  id: number;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  color: string;
  strokeWidth: number;
};

export type ImageEditState = {
  crop: CropSelection | null;
  textLayers: TextLayer[];
  arrowLayers: ArrowLayer[];
};

export function createEmptyEditState(): ImageEditState {
  return { crop: null, textLayers: [], arrowLayers: [] };
}

export function isEditStateEmpty(state: ImageEditState): boolean {
  return !state.crop && state.textLayers.length === 0 && state.arrowLayers.length === 0;
}

export type ImageEditHistory = {
  past: ImageEditState[];
  present: ImageEditState;
  future: ImageEditState[];
};

export function createInitialHistory(): ImageEditHistory {
  return {
    past: [],
    present: createEmptyEditState(),
    future: []
  };
}


export function resolveGenerationProfile(
  modelId: string | null,
  profile: Exclude<GenerationProfile, "custom">
): { steps: number; guidanceScale: string; batchSize: number } {
  const isFastModel = modelId ? /turbo|lcm/i.test(modelId) : true;
  if (profile === "quality") {
    return { steps: isFastModel ? 10 : 35, guidanceScale: isFastModel ? "1.5" : "6.5", batchSize: 1 };
  }
  if (profile === "performance") {
    return { steps: isFastModel ? 4 : 15, guidanceScale: isFastModel ? "1.0" : "4.0", batchSize: 1 };
  }
  return { steps: isFastModel ? 6 : 22, guidanceScale: isFastModel ? "1.0" : "5.0", batchSize: 1 };
}

export function createModelDraft(model: ImageModelCatalogEntry): ModelDraft {
  return {
    id: model.id,
    displayName: model.displayName,
    recommendedProfile: model.recommendedProfile,
    modelType: model.modelType,
    modelProfile: model.modelProfile,
    supportedResolutions: model.supportedResolutions.join(", "),
    defaultSteps: model.defaultSteps.toString(),
    defaultGuidance: model.defaultGuidance ? model.defaultGuidance.toString() : "",
    scheduler: model.scheduler,
    compatibilityNotes: model.compatibilityNotes,
    downloadUrl: model.downloadUrl,
    licenseLabel: model.licenseLabel,
    expectedSizeBytes: model.expectedSizeBytes.toString(),
    requiredFiles: model.requiredFiles.join(", "),
    sha256: model.sha256 ?? ""
  };
}

export function createEmptyModelDraft(modelId: string): ModelDraft {
  return {
    id: modelId,
    displayName: "",
    recommendedProfile: "",
    modelType: "onnx-sdxl",
    modelProfile: "sdxl-lcm-lightning",
    supportedResolutions: "1024x1024, 832x1216, 1216x832",
    defaultSteps: "6",
    defaultGuidance: "1.5",
    scheduler: "LCM",
    compatibilityNotes: "",
    downloadUrl: "",
    licenseLabel: "OpenRAIL-M",
    expectedSizeBytes: "0",
    requiredFiles: "model.onnx",
    sha256: ""
  };
}
