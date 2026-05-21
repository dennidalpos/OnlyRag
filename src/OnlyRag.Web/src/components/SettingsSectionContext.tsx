import { createContext, type ReactNode, useContext } from "react";
import type { SettingsSectionController } from "./useSettingsSectionController";

const SettingsSectionContext = createContext<SettingsSectionController | null>(null);

export function SettingsSectionProvider({
  value,
  children
}: {
  value: SettingsSectionController;
  children: ReactNode;
}) {
  return <SettingsSectionContext.Provider value={value}>{children}</SettingsSectionContext.Provider>;
}

export function useSettingsSectionContext() {
  const value = useContext(SettingsSectionContext);
  if (value === null) {
    throw new Error("SettingsSectionContext is not available.");
  }

  return value;
}

