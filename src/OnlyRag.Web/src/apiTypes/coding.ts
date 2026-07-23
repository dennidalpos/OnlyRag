export type CodingTaskRequest = {
  prompt: string;
  model?: string | null;
  persona?: "architect" | "speedrunner" | "clean_code" | "security_auditor" | string | null;
  language?: string | null;
  codeContext?: string | null;
  targetFilePath?: string | null;
  mode?: "plan" | "write" | null;
  workspaceSummary?: string | null;
};

export type CodingTaskResponse = {
  generatedCode: string;
  explanation: string;
  language: string;
  targetFilePath?: string | null;
  executionSuggestions?: string[] | null;
};

export type CodeRefactorRequest = {
  originalCode: string;
  goal: "performance" | "readability" | "unit_tests" | "type_safety" | string;
  model?: string | null;
  language?: string | null;
  instructions?: string | null;
};

export type CodeRefactorResponse = {
  originalCode: string;
  modifiedCode: string;
  explanation: string;
  language: string;
};

export type CodeDiagnoseRequest = {
  errorLog: string;
  model?: string | null;
  codeContext?: string | null;
  language?: string | null;
};

export type CodeDiagnoseResponse = {
  rootCauseAnalysis: string;
  suggestedFixCode: string;
  fixedCodeDiff: string;
  language: string;
};
