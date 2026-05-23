export type BoundedStorageOptions = {
  maxCharacters: number;
};

export function readBoundedStorageItem(
  storage: Storage,
  key: string,
  options: BoundedStorageOptions
): string | null {
  try {
    const value = storage.getItem(key);
    if (value === null) {
      return null;
    }

    if (value.length > options.maxCharacters) {
      storage.removeItem(key);
      return null;
    }

    return value;
  } catch {
    return null;
  }
}

export function writeBoundedStorageItem(
  storage: Storage,
  key: string,
  value: string,
  options: BoundedStorageOptions
): boolean {
  try {
    if (value.length > options.maxCharacters) {
      storage.removeItem(key);
      return false;
    }

    storage.setItem(key, value);
    return true;
  } catch {
    return false;
  }
}

export function removeStorageItem(storage: Storage, key: string): void {
  try {
    storage.removeItem(key);
  } catch {
  }
}
