export type CodingTab = "workbench" | "refactor" | "diagnose" | "preview";

export type VibePersona = "architect" | "speedrunner" | "clean_code" | "security_auditor" | "free_prompt";

export type RefactorGoal = "performance" | "readability" | "unit_tests" | "type_safety";

export type CodeSnippetItem = {
  id: string;
  title: string;
  language: string;
  code: string;
  explanation?: string;
  createdAt: string;
};

export type VibePersonaOption = {
  id: VibePersona;
  label: string;
  description: string;
  iconName: string;
};
