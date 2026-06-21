import React, { useState } from 'react';
import { FolderOpen, RotateCcw } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { Card } from '../../components/ui/Card';
import { SettingRow, SettingSection } from '../../components/ui/SettingRow';
import { send } from '../../bridge/ipc';

export function StoragePage() {
  const [modelDir, setModelDir] = useState('');

  const handleChangeLocation = () => {
    send({ type: 'pick_directory', purpose: 'models' });
  };

  const handleOpenFolder = () => {
    send({ type: 'open_directory', path: 'models' });
  };

  const handleResetDefault = () => {
    send({ type: 'set_setting', key: 'model_dir', value: '' });
    setModelDir('');
  };

  return (
    <div className="space-y-8 max-w-2xl">
      <SettingSection title="Model Storage" description="Where downloaded models are stored on disk">
        <Card className="p-4 space-y-3">
          <div className="flex items-center justify-between gap-4">
            <div className="min-w-0 flex-1">
              <p className="text-xs font-medium text-muted-foreground mb-1">Current location</p>
              <p className="text-sm font-mono text-muted-foreground/80 truncate">
                {modelDir || 'Default (AppData/Local/WinWhisperFlow/runtime/models)'}
              </p>
            </div>
            <Button variant="outline" size="sm" className="gap-1.5 shrink-0" onClick={handleOpenFolder}>
              <FolderOpen className="w-3.5 h-3.5" />
              Open
            </Button>
          </div>
          <div className="flex gap-2">
            <Button variant="secondary" size="sm" className="gap-1.5" onClick={handleChangeLocation}>
              Change Location
            </Button>
            <Button variant="ghost" size="sm" className="gap-1.5 text-muted-foreground" onClick={handleResetDefault}>
              <RotateCcw className="w-3.5 h-3.5" />
              Reset to Default
            </Button>
          </div>
        </Card>
      </SettingSection>
    </div>
  );
}
