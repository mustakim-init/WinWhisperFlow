import React from 'react';
import { Select } from '../../components/ui/Select';
import { SettingRow, SettingSection } from '../../components/ui/SettingRow';
import { useStore } from '../../hooks/useStore';

export function AudioPage() {
  const store = useStore();
  const deviceOptions = store.audioDevices.length > 0
    ? store.audioDevices.map((name, i) => ({ label: name, value: String(i) }))
    : [{ label: 'Default Device', value: '0' }];

  return (
    <div className="space-y-8 max-w-2xl">
      <SettingSection title="Audio" description="Microphone and input settings">
        <SettingRow
          title="Input Device"
          description="Microphone to use for recording"
          action={
            <Select
              options={deviceOptions}
              value={String(store.device === 'cpu' ? 0 : 0)}
              onChange={() => {}}
              className="w-48"
            />
          }
        />
      </SettingSection>
    </div>
  );
}
