import type { CodeSnippetItem } from "./CodingSection.types";

const STORAGE_KEY = "onlyrag_coding_snippets_v1";
const PRESETS_STORAGE_KEY = "onlyrag_vibe_presets_v1";

export type VibePreset = {
  id: string;
  title: string;
  icon: string;
  prompt: string;
  isBuiltIn?: boolean;
};

export const BUILT_IN_PRESETS: VibePreset[] = [
  {
    id: "preset_audit_ui_ux",
    title: "Audit UI/UX",
    icon: "🎨",
    prompt: "Esegui un audit del codice UI/frontend: esamina la struttura visiva, la componibilità, l'usabilità, la reattività e l'accessibilità dell'interfaccia.",
    isBuiltIn: true
  },
  {
    id: "preset_audit_pipeline",
    title: "Audit Flussi & Logica",
    icon: "⚙️",
    prompt: "Esegui un audit dei flussi dati e della logica di elaborazione: analizza la gestione delle eccezioni, la concorrenza/asincronia, le prestazioni e la robustezza del codice.",
    isBuiltIn: true
  },
  {
    id: "preset_audit_docs",
    title: "Audit Documentazione",
    icon: "📚",
    prompt: "Esegui un audit della documentazione del codice: verifica la presenza e la chiarezza di commenti, specifiche delle API, tipi/interfacce e guide di sviluppo.",
    isBuiltIn: true
  },
  {
    id: "preset_audit_build",
    title: "Audit Build & Pacchetti",
    icon: "🛠️",
    prompt: "Esegui un audit della configurazione di build, compilazione e pacchettizzazione del progetto: verifica correttezza dei file di build, dipendenze ed efficienza.",
    isBuiltIn: true
  },
  {
    id: "preset_refactor",
    title: "Rifattorizza & Pulisci",
    icon: "🚀",
    prompt: "Rifattorizza il codice rendendolo pulito, leggibile, privo di duplicazioni e conforme ai principi SOLID e alle best practice del linguaggio.",
    isBuiltIn: true
  },
  {
    id: "preset_unit_tests",
    title: "Genera Unit Tests",
    icon: "🧪",
    prompt: "Genera una suite completa di test unitari per questo codice, coprendo i flussi principali, i casi limite ed isolando le dipendenze esterne.",
    isBuiltIn: true
  },
  {
    id: "preset_fix_bug",
    title: "Risolvi Bug",
    icon: "🐛",
    prompt: "Analizza questo codice o l'errore riscontrato, individua la causa radice del problema e fornisci la soluzione corretta spiegando la modifica effettuata.",
    isBuiltIn: true
  },
  {
    id: "preset_architecture",
    title: "Analizza Architettura",
    icon: "🏗️",
    prompt: "Esamina la struttura e l'architettura complessiva di questo codice o progetto, evidenziando criticità e proponendo raccomandazioni di modularità e manutenibilità.",
    isBuiltIn: true
  }
];


export function loadSavedPresets(): VibePreset[] {
  try {
    const raw = localStorage.getItem(PRESETS_STORAGE_KEY);
    const custom: VibePreset[] = raw ? JSON.parse(raw) : [];
    return [...BUILT_IN_PRESETS, ...custom];
  } catch {
    return BUILT_IN_PRESETS;
  }
}

export function saveCustomPreset(preset: Omit<VibePreset, "id" | "isBuiltIn">): VibePreset[] {
  const current = loadSavedPresets().filter((p) => !p.isBuiltIn);
  const newItem: VibePreset = {
    ...preset,
    id: `preset_custom_${Date.now()}_${Math.random().toString(36).substring(2, 7)}`,
    isBuiltIn: false
  };
  const updated = [...current, newItem];
  try {
    localStorage.setItem(PRESETS_STORAGE_KEY, JSON.stringify(updated));
  } catch {
    // Ignore
  }
  return [...BUILT_IN_PRESETS, ...updated];
}

export function deleteCustomPreset(id: string): VibePreset[] {
  const custom = loadSavedPresets().filter((p) => !p.isBuiltIn && p.id !== id);
  try {
    localStorage.setItem(PRESETS_STORAGE_KEY, JSON.stringify(custom));
  } catch {
    // Ignore
  }
  return [...BUILT_IN_PRESETS, ...custom];
}

export function loadSavedSnippets(): CodeSnippetItem[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    return JSON.parse(raw) as CodeSnippetItem[];
  } catch {
    return [];
  }
}

export function saveSnippet(snippet: Omit<CodeSnippetItem, "id" | "createdAt">): CodeSnippetItem {
  const current = loadSavedSnippets();
  const newItem: CodeSnippetItem = {
    ...snippet,
    id: `snippet_${Date.now()}_${Math.random().toString(36).substring(2, 7)}`,
    createdAt: new Date().toISOString()
  };
  const updated = [newItem, ...current].slice(0, 50);
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
  } catch {
    // Ignore storage quota errors
  }
  return newItem;
}

export function deleteSnippet(id: string): CodeSnippetItem[] {
  const current = loadSavedSnippets();
  const updated = current.filter((s) => s.id !== id);
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
  } catch {
    // Ignore
  }
  return updated;
}

