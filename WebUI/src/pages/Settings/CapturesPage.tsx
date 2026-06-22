import React, { useState } from 'react';
import { Keyboard } from 'lucide-react';
import { Switch } from '../../components/ui/Switch';
import { Select } from '../../components/ui/Select';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { SettingRow, SettingSection } from '../../components/ui/SettingRow';
import { ChordPicker } from '../../components/ui/ChordPicker';
import { send } from '../../bridge/ipc';
import { useStore } from '../../hooks/useStore';
import { defaultChordKeys, displayLabelForKey } from '../../lib/utils/keyCodes';

export function CapturesPage() {
  const store = useStore();
  const [autoPaste, setAutoPaste] = useState(false);
  const [pushToTalk, setPushToTalk] = useState(false);
  const [hotkeyOpen, setHotkeyOpen] = useState(false);
  const [hotkey, setHotkey] = useState<string[]>([]);

  const handleHotkeyChange = (keys: string[]) => {
    setHotkey(keys);
    send({ type: 'set_setting', key: 'hotkey_chord', value: keys });
  };

  const handleAutoPaste = (checked: boolean) => {
    setAutoPaste(checked);
    send({ type: 'set_setting', key: 'auto_paste', value: checked });
  };

  const handlePushToTalk = (checked: boolean) => {
    setPushToTalk(checked);
    send({ type: 'set_setting', key: 'push_to_talk', value: checked });
  };

  const hotkeyToShow = hotkey.length > 0 ? hotkey : defaultChordKeys(pushToTalk ? 'push' : 'toggle');
  const hotkeyLabel = hotkeyToShow.map(displayLabelForKey).join(' + ');

  const downloadedModels = store.availableModels
    .filter((m) => m.downloaded)
    .map((m) => ({ label: m.displayName, value: m.name }));

  return (
    <div className="space-y-8 max-w-2xl">
      <SettingSection title="Dictation" description="Configure how dictation is triggered and controlled">
        <SettingRow
          title="Keyboard shortcut"
          description="Global hotkey to start/stop dictation"
          action={
            <Button variant="outline" size="sm" className="gap-2" onClick={() => setHotkeyOpen(true)}>
              <Keyboard className="w-3.5 h-3.5" />
              <span className="text-xs font-mono">{hotkeyLabel}</span>
            </Button>
          }
        />
        <SettingRow
          title="Push-to-talk"
          description="Hold the shortcut while speaking, release to transcribe"
          action={<Switch checked={pushToTalk} onCheckedChange={handlePushToTalk} />}
        />
        <SettingRow
          title="Auto-paste"
          description="Automatically paste transcription into focused app"
          action={<Switch checked={autoPaste} onCheckedChange={handleAutoPaste} />}
        />
      </SettingSection>

      <SettingSection title="Transcription" description="Speech recognition settings for dictation">
        <SettingRow
          title="Model"
          description="Whisper model for dictation"
          action={
            <Select
              options={downloadedModels.length > 0 ? downloadedModels : [{ label: 'No models downloaded', value: '' }]}
              value={store.model}
              onChange={(v) => send({ type: 'load_model', model: v })}
              className="w-44"
            />
          }
        />
      </SettingSection>

      <ChordPicker
        open={hotkeyOpen}
        onOpenChange={setHotkeyOpen}
        onChord={handleHotkeyChange}
        initialKeys={hotkey}
        mode={pushToTalk ? 'push' : 'toggle'}
      />
    </div>
  );
}
