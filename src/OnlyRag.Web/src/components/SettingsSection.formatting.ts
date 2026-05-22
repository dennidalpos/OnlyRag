export function formatOcrInteger(value: number): string {
  return Math.round(value).toLocaleString("it-IT");
}

export function formatOcrDecimal(value: number): string {
  return value.toFixed(2);
}

export function formatModelSize(size: number): string {
  if (size >= 1_000_000_000) {
    return `${(size / 1_000_000_000).toFixed(1)} GB`;
  }

  if (size >= 1_000_000) {
    return `${(size / 1_000_000).toFixed(1)} MB`;
  }

  return `${size} B`;
}

export function formatTelemetryBytes(bytes: number | null): string {
  if (bytes === null || !Number.isFinite(bytes) || bytes < 0) {
    return "n/d";
  }

  if (bytes >= 1024 ** 3) {
    return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
  }

  if (bytes >= 1024 ** 2) {
    return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  }

  return `${Math.round(bytes / 1024).toLocaleString("it-IT")} KB`;
}

export function formatTelemetryPercent(value: number | null): string {
  return value === null || !Number.isFinite(value) ? "n/d" : `${value.toFixed(1)}%`;
}
