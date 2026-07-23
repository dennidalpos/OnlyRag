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
    title: "🎨 Audit UI/UX",
    icon: "🎨",
    prompt: "Esegui un Audit UI/UX approfondito: esamina i componenti visuali, la gerarchia visiva, i design token, la reattività, le animazioni e l'usabilità complessiva dell'interfaccia.",
    isBuiltIn: true
  },
  {
    id: "preset_audit_pipeline",
    title: "⚙️ Audit Pipeline",
    icon: "⚙️",
    prompt: "Esegui un Audit Pipeline del codice: esamina l'elaborazione dei dati, i flussi asincroni, le gestione delle eccezioni, il retry handling e le prestazioni delle code di lavoro in background.",
    isBuiltIn: true
  },
  {
    id: "preset_audit_docs",
    title: "📚 Audit Documentazione",
    icon: "📚",
    prompt: "Esegui un Audit Documentazione: analizza la presenza e chiarezza di README, commenti esplicativi di codice, guide di setup e documentazione delle API/interfacce.",
    isBuiltIn: true
  },
  {
    id: "preset_audit_build",
    title: "🛠️ Audit Script Build / Package",
    icon: "🛠️",
    prompt: "Esegui un Audit Script Build & Package: esamina gli script di automazione PowerShell/npm, la configurazione di compilazione e gli installer/pacchetti eseguibili prodotti.",
    isBuiltIn: true
  },
  {
    id: "preset_refactor",
    title: "🚀 Rifattorizza & Pulisci",
    icon: "🚀",
    prompt: "Rifattorizza il codice rendendolo pulito, leggibile, privo di duplicazioni e conforme ai principi SOLID.",
    isBuiltIn: true
  },
  {
    id: "preset_unit_tests",
    title: "🧪 Genera Unit Tests",
    icon: "🧪",
    prompt: "Genera una suite completa ed esaustiva di test unitari con copertura dei casi limite ed eventuali mock.",
    isBuiltIn: true
  },
  {
    id: "preset_fix_bug",
    title: "🐛 Risolvi Bug",
    icon: "🐛",
    prompt: "Analizza l'errore o il comportamento inatteso, individua la causa radice e fornisci il codice corretto.",
    isBuiltIn: true
  },
  {
    id: "preset_architecture",
    title: "🏗️ Analizza Architettura",
    icon: "🏗️",
    prompt: "Esamina la struttura e l'architettura di questo codice/progetto e proponi miglioramenti per modularità e manutenibilità.",
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

