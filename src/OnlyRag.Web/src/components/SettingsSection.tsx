import { SettingsSectionPanels } from "./SettingsSection.panels";
import { SettingsSectionProvider } from "./SettingsSectionContext";
import {
  type SettingsSectionProps,
  useSettingsSectionController
} from "./useSettingsSectionController";

export function SettingsSection(props: SettingsSectionProps) {
  const controller = useSettingsSectionController(props);

  return (
    <SettingsSectionProvider value={controller}>
      <SettingsSectionPanels />
    </SettingsSectionProvider>
  );
}

