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
