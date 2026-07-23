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

export const minSizePresets = [
  { label: "512x512 (Veloce 1:1)", width: 512, height: 512 },
  { label: "512x768 (2:3)", width: 512, height: 768 },
  { label: "768x512 (3:2)", width: 768, height: 512 }
];

export const standardSizePresets = [
  { label: "768x768 (1:1)", width: 768, height: 768 },
  { label: "1024x1024 (HD 1:1)", width: 1024, height: 1024 },
  { label: "1216x832 (16:9)", width: 1216, height: 832 },
  { label: "832x1216 (9:16)", width: 832, height: 1216 }
];

export const socialSizePresets = [
  { label: "Instagram Post (1080x1080)", width: 1080, height: 1080 },
  { label: "Instagram Story / Reel (1080x1920)", width: 1080, height: 1920 },
  { label: "Instagram Portrait (1080x1350)", width: 1080, height: 1350 },
  { label: "YouTube / Twitter (1280x720)", width: 1280, height: 720 },
  { label: "TikTok / Short (1080x1920)", width: 1080, height: 1920 },
  { label: "LinkedIn Post (1200x627)", width: 1200, height: 627 }
];

export const sizePresets = [
  ...minSizePresets,
  ...standardSizePresets,
  ...socialSizePresets
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


export function resolveGenerationProfile(modelId: string, profile: Exclude<GenerationProfile, "custom">): { steps: number; batchSize: number } {
  const isFastModel = /turbo|lcm/i.test(modelId);
  if (profile === "quality") {
    return { steps: isFastModel ? 10 : 52, batchSize: 1 };
  }
  if (profile === "performance") {
    return { steps: isFastModel ? 5 : 18, batchSize: 1 };
  }
  return { steps: isFastModel ? 7 : 30, batchSize: 1 };
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
