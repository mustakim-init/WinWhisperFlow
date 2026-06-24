import React, { useState } from 'react';
import { LayoutGroup, motion } from 'framer-motion';
import { GeneralPage } from './GeneralPage';
import { AudioPage } from './AudioPage';
import { TranscriptionSettingsPage } from './TranscriptionPage';
import { CapturesPage } from './CapturesPage';
import { StoragePage } from './StoragePage';
import { GpuPage } from './GpuPage';
import { LogsPage } from './LogsPage';
import { AboutPage } from './AboutPage';

const settingsTabs = [
  { id: 'general', label: 'General' },
  { id: 'audio', label: 'Audio' },
  { id: 'transcription', label: 'Transcription' },
  { id: 'captures', label: 'Captures' },
  { id: 'storage', label: 'Storage' },
  { id: 'gpu', label: 'GPU' },
  { id: 'logs', label: 'Logs' },
  { id: 'about', label: 'About' },
];

const pages: Record<string, React.FC> = {
  general: GeneralPage,
  audio: AudioPage,
  transcription: TranscriptionSettingsPage,
  captures: CapturesPage,
  storage: StoragePage,
  gpu: GpuPage,
  logs: LogsPage,
  about: AboutPage,
};

export function SettingsLayout() {
  const [activeTab, setActiveTab] = useState('general');
  const Page = pages[activeTab];

  return (
    <div className="flex flex-col h-full min-h-0 relative w-full pt-8">
      <div className="shrink-0 mb-4 px-4">
        <h1 className="text-2xl font-bold">Settings</h1>
        <p className="text-sm text-muted-foreground mt-0.5">Configure system parameters and speech options</p>
      </div>

      <nav className="flex gap-1 border-b border-border/60 shrink-0 overflow-x-auto scrollbar-hide px-4">
        <LayoutGroup id="settings-tabs">
          {settingsTabs.map((tab) => {
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id)}
                className={`relative shrink-0 px-4 py-2.5 text-sm font-medium transition-colors whitespace-nowrap pb-3 ${
                  isActive ? 'text-foreground font-semibold' : 'text-muted-foreground hover:text-foreground'
                }`}
              >
                {tab.label}
                {isActive && (
                  <motion.div
                    layoutId="settings-tab-active"
                    className="absolute bottom-0 left-0 right-0 h-0.5 bg-accent"
                    transition={{ type: 'spring', stiffness: 350, damping: 30 }}
                  />
                )}
              </button>
            );
          })}
        </LayoutGroup>
      </nav>

      <div className="flex-1 overflow-y-auto pt-6 pb-6 px-4 scrollbar-hide">
        {Page && <Page />}
      </div>
    </div>
  );
}
