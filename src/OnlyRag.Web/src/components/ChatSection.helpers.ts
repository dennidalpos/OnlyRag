export function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

export function getMaxDocumentsPanelWidth(
  layout: HTMLDivElement | null,
  minDocumentsPanelWidth: number,
  maxDocumentsPanelWidth: number,
  minChatPanelWidth: number
): number {
  if (!layout) {
    return maxDocumentsPanelWidth;
  }

  return Math.max(
    minDocumentsPanelWidth,
    Math.min(maxDocumentsPanelWidth, layout.getBoundingClientRect().width - minChatPanelWidth)
  );
}

export function formatPageRange(pageStart: number | null, pageEnd: number | null): string {
  if (!pageStart && !pageEnd) {
    return "Pagina non disponibile";
  }

  if (!pageEnd || pageStart === pageEnd) {
    return `Pagina ${pageStart}`;
  }

  return `Pagine ${pageStart}-${pageEnd}`;
}
