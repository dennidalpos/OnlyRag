import {
  readBoundedStorageItem,
  removeStorageItem,
  writeBoundedStorageItem
} from "../../storage/webViewStorage";

const maxCompareDraftCharacters = 120_000;

export function loadCompareDraft(compareDraftKey: string | null): string | null {
  if (!compareDraftKey) {
    return null;
  }

  return readBoundedStorageItem(window.localStorage, compareDraftKey, {
    maxCharacters: maxCompareDraftCharacters
  });
}

export function saveOrClearCompareDraft(
  compareDraftKey: string | null,
  hasUnsavedCompareDraft: boolean,
  editedTranslationText: string
): void {
  if (!compareDraftKey) {
    return;
  }

  if (!hasUnsavedCompareDraft) {
    removeStorageItem(window.localStorage, compareDraftKey);
    return;
  }

  writeBoundedStorageItem(window.localStorage, compareDraftKey, editedTranslationText, {
    maxCharacters: maxCompareDraftCharacters
  });
}

export function clearCompareDraft(compareDraftKey: string | null): void {
  if (compareDraftKey) {
    removeStorageItem(window.localStorage, compareDraftKey);
  }
}
