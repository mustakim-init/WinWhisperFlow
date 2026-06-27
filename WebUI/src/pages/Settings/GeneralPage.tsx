import React, { useState } from 'react';
import { BookOpen, Code2, ExternalLink } from 'lucide-react';
import { Switch } from '../../components/ui/Switch';
import { Select } from '../../components/ui/Select';
import { Card } from '../../components/ui/Card';
import { Badge } from '../../components/ui/Badge';
import { send } from '../../bridge/ipc';
import { useStore } from '../../hooks/useStore';
import { useUiStore } from '../../stores/uiStore';
import { SettingRow, SettingSection } from '../../components/ui/SettingRow';

const themeOptions = [
  { label: 'Dark', value: 'dark' },
  { label: 'Light', value: 'light' },
  { label: 'System', value: 'system' },
];

const languageOptions = [
  { label: 'English', value: 'en' },
];

export function GeneralPage() {
  const darkMode = useUiStore((s) => s.darkMode);
  const setDarkMode = useUiStore((s) => s.setDarkMode);
  const store = useStore();
  const [startOnBoot, setStartOnBoot] = useState(false);
  const [sfxEnabled, setSfxEnabled] = useState(true);
  const [theme, setTheme] = useState('dark');

  const handleDarkMode = (checked: boolean) => {
    setDarkMode(checked);
    setTheme(checked ? 'dark' : 'light');
    send({ type: 'set_setting', key: 'theme', value: checked ? 'dark' : 'light' });
  };

  const handleStartOnBoot = (checked: boolean) => {
    setStartOnBoot(checked);
    send({ type: 'set_setting', key: 'start_on_boot', value: checked });
  };

  const handleSfxToggle = (checked: boolean) => {
    setSfxEnabled(checked);
    send({ type: 'set_setting', key: 'sfx', value: checked });
  };

  const handleThemeChange = (v: string) => {
    setTheme(v);
    if (v === 'dark') {
      setDarkMode(true);
    } else if (v === 'light') {
      setDarkMode(false);
    } else {
      const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
      setDarkMode(prefersDark);
    }
    send({ type: 'set_setting', key: 'theme', value: v });
  };

  const handleLanguageChange = (v: string) => {
    send({ type: 'set_setting', key: 'language', value: v });
  };

  return (
    <div className="space-y-8 max-w-2xl">
      {/* Quick Links */}
      <div className="grid grid-cols-2 gap-4">
        <a
          href="#"
          onClick={(e) => { e.preventDefault(); send({ type: 'open_url', url: 'https://github.com/mustakim-init/WinWhisperFlow/wiki' }); }}
          className="flex items-center gap-3 rounded-xl border border-border bg-card p-4 hover:bg-muted/50 transition-colors group"
        >
          <div className="w-10 h-10 rounded-full bg-accent/15 flex items-center justify-center shrink-0">
            <BookOpen className="w-5 h-5 text-accent" />
          </div>
          <div className="min-w-0">
            <p className="text-sm font-medium">Documentation</p>
            <p className="text-xs text-muted-foreground">View guides and API</p>
          </div>
          <ExternalLink className="w-4 h-4 text-muted-foreground ml-auto opacity-0 group-hover:opacity-100 transition-opacity" />
        </a>
        <a
          href="#"
          onClick={(e) => { e.preventDefault(); send({ type: 'open_url', url: 'https://github.com/mustakim-init/WinWhisperFlow' }); }}
          className="flex items-center gap-3 rounded-xl border border-border bg-card p-4 hover:bg-muted/50 transition-colors group"
        >
          <div className="w-10 h-10 rounded-full bg-accent/15 flex items-center justify-center shrink-0">
            <Code2 className="w-5 h-5 text-accent" />
          </div>
          <div className="min-w-0">
            <p className="text-sm font-medium">GitHub</p>
            <p className="text-xs text-muted-foreground">Source code and issues</p>
          </div>
          <ExternalLink className="w-4 h-4 text-muted-foreground ml-auto opacity-0 group-hover:opacity-100 transition-opacity" />
        </a>
      </div>

      {/* Status Card — reflects actual service state */}
      <Card className="p-4 flex items-center gap-3">
        <span className="relative flex h-3 w-3">
          {store.statusVariant === 'error' ? (
            <span className="relative inline-flex rounded-full h-3 w-3 bg-red-400" />
          ) : store.statusVariant === 'warning' ? (
            <>
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-amber-400 opacity-75" />
              <span className="relative inline-flex rounded-full h-3 w-3 bg-amber-400" />
            </>
          ) : (
            <>
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75" />
              <span className="relative inline-flex rounded-full h-3 w-3 bg-emerald-400" />
            </>
          )}
        </span>
        <div>
          <p className="text-sm font-medium">{store.statusText || 'Starting\u2026'}</p>
          <p className="text-xs text-muted-foreground">
            {store.modelLoaded ? 'Model loaded' : store.modelLoading ? 'Loading model\u2026' : 'No model loaded'}
          </p>
        </div>
        <Badge variant="outline" className="ml-auto text-[10px]">v1.0</Badge>
      </Card>

      <SettingSection title="Appearance" description="Customize the look and feel">
        <SettingRow
          title="Theme"
          description="Choose dark, light, or system theme"
          action={
            <Select
              options={themeOptions}
              value={theme}
              onChange={handleThemeChange}
              className="w-32"
            />
          }
        />
        <SettingRow
          title="Dark mode"
          description="Toggle dark/light appearance"
          action={<Switch checked={darkMode} onCheckedChange={handleDarkMode} />}
        />
      </SettingSection>

      <SettingSection title="Preferences" description="Application behavior">
        <SettingRow
          title="Language"
          description="Interface language"
          action={
            <Select
              options={languageOptions}
              value="en"
              onChange={handleLanguageChange}
              className="w-32"
            />
          }
        />
        <SettingRow
          title="Start on boot"
          description="Launch WinWhisper Flow when you sign in"
          action={<Switch checked={startOnBoot} onCheckedChange={handleStartOnBoot} />}
        />
        <SettingRow
          title="Sound effects"
          description="Play sounds on record, transcribe, and errors"
          action={<Switch checked={sfxEnabled} onCheckedChange={handleSfxToggle} />}
        />
      </SettingSection>
    </div>
  );
}
