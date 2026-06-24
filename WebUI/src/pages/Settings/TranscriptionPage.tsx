import React from 'react';
import { Select } from '../../components/ui/Select';
import { send } from '../../bridge/ipc';
import { useStore } from '../../hooks/useStore';
import { SettingRow, SettingSection } from '../../components/ui/SettingRow';

export function TranscriptionSettingsPage() {
  const store = useStore();

  const handleModelChange = (v: string) => {
    send({ type: 'load_model', model: v });
  };

  const handleLanguageChange = (v: string) => {
    const lang = v === 'auto' ? '' : v;
    send({ type: 'set_language', language: lang });
    send({ type: 'set_setting', key: 'language', value: lang });
  };

  const downloadedModels = store.availableModels
    .filter((m) => m.downloaded)
    .map((m) => ({ label: m.displayName, value: m.name }));

  return (
    <div className="space-y-8 max-w-2xl">
      <SettingSection title="Transcription" description="Transcription behavior preferences">
        <SettingRow
          title="Default Model"
          description="Whisper model to use for transcription"
          action={
            <Select
              options={downloadedModels.length > 0 ? downloadedModels : [{ label: 'No models downloaded', value: '__none__' }]}
              value={store.model}
              onChange={handleModelChange}
              className="w-44"
            />
          }
        />
        <SettingRow
          title="Language Lock"
          description="Lock transcription to a specific language"
          action={
            <Select
              options={[
                { label: 'Auto detect', value: 'auto' },
                { label: 'English', value: 'en' },
                { label: 'Bengali', value: 'bn' },
              ]}
              value={store.language || 'auto'}
              onChange={handleLanguageChange}
              className="w-32"
            />
          }
        />
      </SettingSection>
    </div>
  );
}
