import { act } from "@testing-library/react";
import type { OcrProvisionStatus, OcrSettings, OcrStartupAnalysis } from "./api";
import { createModel } from "./test/fixtures";

export function createRequiredModels() {
  return [
    createModel(),
    createModel({ name: "nomic-embed-text", model: "nomic-embed-text", family: "bert" })
  ];
}

export function createAppStatus() {
  return {
    backend: "Ready",
    database: "Ready",
    jobQueue: "0",
    ollama: "Ready",
    startedAtUtc: "2026-05-23T20:00:00Z",
    lowResourceMode: false
  };
}

export function createOcrStartupAnalysis(overrides: Partial<OcrStartupAnalysis> = {}): OcrStartupAnalysis {
  return {
    shouldPrompt: false,
    isWindowsSupported: true,
    hasMinimumDiskSpace: true,
    availableDiskBytes: 240 * 1024 * 1024 * 1024,
    requiredDiskBytes: 3 * 1024 * 1024 * 1024,
    hasCompatiblePython: true,
    isOcrConfigured: true,
    isNvidiaRuntimeAvailable: false,
    isGpuUsable: false,
    recommendedRuntimeTarget: "auto",
    title: "",
    message: "",
    findings: [],
    ...overrides
  };
}

export function createOcrProvisionStatus(overrides: Partial<OcrProvisionStatus> = {}): OcrProvisionStatus {
  return {
    isConfigured: true,
    isRunning: false,
    message: "OCR configurato.",
    lastError: null,
    runtimeTarget: "auto",
    resolvedRuntime: "cpu",
    runtimeDetail: null,
    startedAtUtc: null,
    updatedAtUtc: "2026-05-24T14:00:00Z",
    stepKey: null,
    stepLabel: null,
    stepIndex: 0,
    stepCount: 8,
    progressPercent: 100,
    severity: "info",
    canRetry: false,
    selectedRuntime: null,
    ...overrides
  };
}

export function createOcrSettings(overrides: Partial<OcrSettings> = {}): OcrSettings {
  return {
    profile: "balanced",
    pdfDpi: 220,
    modelPreset: "PP-OCRv5",
    modelVersion: "PP-OCRv5",
    detectionSideLimit: 1152,
    detectionThreshold: 0.3,
    detectionBoxThreshold: 0.6,
    detectionUnclipRatio: 1.5,
    recognitionScoreThreshold: 0.5,
    useTextlineOrientation: true,
    useDocumentOrientationClassification: false,
    useDocumentUnwarping: false,
    recognitionBatchSize: 6,
    cpuThreads: 2,
    device: "cpu",
    ...overrides
  };
}

export async function flushPromises() {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });
}
