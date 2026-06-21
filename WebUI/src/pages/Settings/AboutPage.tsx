import React from 'react';
import { SettingRow, SettingSection } from '../../components/ui/SettingRow';

export function AboutPage() {
  return (
    <div className="space-y-8 max-w-2xl">
      <SettingSection title="" description="">
        <SettingRow
          title="WinWhisper Flow"
          description="Version 1.0.0"
          action={
            <div className="w-12 h-12 rounded-xl bg-accent/20 flex items-center justify-center">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="w-6 h-6 text-accent">
                <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
                <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                <line x1="12" x2="12" y1="19" y2="22" />
              </svg>
            </div>
          }
        >
          <div className="bg-muted rounded-lg p-3 text-xs text-muted-foreground space-y-2">
            <p>Local speech-to-text for Windows 11.</p>
            <p>Built with .NET 8, NAudio, WebView2, and faster-whisper.</p>
            <p>License: MIT</p>
          </div>
        </SettingRow>
      </SettingSection>
    </div>
  );
}
