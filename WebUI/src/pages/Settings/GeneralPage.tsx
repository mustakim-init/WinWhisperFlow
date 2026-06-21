import React, { useState } from 'react';
import { Switch } from '../../components/ui/Switch';
import { send } from '../../bridge/ipc';
import { useUiStore } from '../../stores/uiStore';
import { SettingRow, SettingSection } from '../../components/ui/SettingRow';

export function GeneralPage() {
  const darkMode = useUiStore((s) => s.darkMode);
  const setDarkMode = useUiStore((s) => s.setDarkMode);
  const [startOnBoot, setStartOnBoot] = useState(false);

  const handleDarkMode = (checked: boolean) => {
    setDarkMode(checked);
    send({ type: 'set_setting', key: 'theme', value: checked ? 'dark' : 'light' });
  };

  const handleStartOnBoot = (checked: boolean) => {
    setStartOnBoot(checked);
    send({ type: 'set_setting', key: 'start_on_boot', value: checked });
  };

  return (
    <div className="space-y-8 max-w-2xl">
      <SettingSection title="General" description="Application preferences">
        <SettingRow
          title="Dark mode"
          description="Toggle dark/light appearance"
          action={<Switch checked={darkMode} onCheckedChange={handleDarkMode} />}
        />
        <SettingRow
          title="Start on boot"
          description="Launch WinWhisper Flow when you sign in"
          action={<Switch checked={startOnBoot} onCheckedChange={handleStartOnBoot} />}
        />
      </SettingSection>
    </div>
  );
}
