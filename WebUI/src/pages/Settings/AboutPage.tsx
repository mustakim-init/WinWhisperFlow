import React from 'react';
import { SettingRow, SettingSection } from '../../components/ui/SettingRow';
import { APP_VERSION } from '../../lib/version';
import { ExternalLink, RefreshCw, Download, RotateCw, CheckCircle, AlertCircle } from 'lucide-react';
import { send } from '../../bridge/ipc';
import { Button } from '../../components/ui/Button';
import { Progress } from '../../components/ui/Progress';
import { useStore } from '../../hooks/useStore';

export function AboutPage() {
  const store = useStore();
  const [checked, setChecked] = React.useState(false);

  const handleCheck = () => {
    setChecked(true);
    send({ type: 'check_for_updates' });
  };

  const handleDownload = () => {
    send({ type: 'download_update' });
  };

  const handleApply = () => {
    send({ type: 'apply_update' });
  };

  const isUpToDate = checked && !store.updateAvailable;
  const hasUpdate = store.updateAvailable && store.updateVersion;

  return (
    <div className="space-y-8 max-w-2xl">
      <SettingSection title="" description="">
        <SettingRow
          title="WinWhisper Flow"
          description={`Version ${APP_VERSION}`}
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
            <p>Free for personal use. Commercial use requires a paid license.</p>
          </div>
        </SettingRow>
      </SettingSection>

      <SettingSection title="Updates" description="">
        <SettingRow
          title="Current version"
          description={APP_VERSION}
          action={
            !store.updateDownloading && !store.updateReady ? (
              <Button
                variant="outline"
                size="sm"
                onClick={handleCheck}
                disabled={store.updateDownloading}
                className="gap-1.5 text-xs"
              >
                <RefreshCw size={12} />
                {checked && !hasUpdate ? 'Check again' : 'Check for updates'}
              </Button>
            ) : null
          }
        >
          <div className="space-y-3">
            {isUpToDate && (
              <div className="flex items-center gap-2 text-xs text-emerald-400">
                <CheckCircle size={14} />
                <span>You're up to date</span>
              </div>
            )}

            {hasUpdate && !store.updateDownloading && !store.updateReady && (
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-xs text-accent">
                  <Download size={14} />
                  <span>Update available: v{store.updateVersion}</span>
                </div>
                <Button variant="default" size="sm" onClick={handleDownload} className="gap-1.5 text-xs">
                  <Download size={12} />
                  Download update
                </Button>
              </div>
            )}

            {store.updateDownloading && (
              <div className="space-y-2">
                <div className="flex items-center gap-2 text-xs text-muted-foreground">
                  <RefreshCw size={14} className="animate-spin" />
                  <span>Downloading update...</span>
                </div>
                <Progress value={store.updateProgress * 100} className="h-1.5" />
                <span className="text-[11px] text-muted-foreground">
                  {Math.round(store.updateProgress * 100)}%
                </span>
              </div>
            )}

            {store.updateReady && (
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-xs text-emerald-400">
                  <CheckCircle size={14} />
                  <span>Update ready to install</span>
                </div>
                <Button variant="default" size="sm" onClick={handleApply} className="gap-1.5 text-xs">
                  <RotateCw size={12} />
                  Restart to update
                </Button>
              </div>
            )}

            {store.updateError && (
              <div className="flex items-center gap-2 text-xs text-red-400">
                <AlertCircle size={14} />
                <span>Update failed: {store.updateError}</span>
              </div>
            )}
          </div>
        </SettingRow>
      </SettingSection>

      <SettingSection title="Resources" description="">
        <SettingRow
          title="Source code"
          description="View on GitHub"
          action={
            <button
              onClick={() => send({ type: 'open_url', url: 'https://github.com/mustakim-init/WinWhisperFlow' })}
              className="text-accent hover:text-accent/80 transition-colors"
            >
              <ExternalLink size={16} />
            </button>
          }
        />
        <SettingRow
          title="Report an issue"
          description="Open a GitHub issue"
          action={
            <button
              onClick={() => send({ type: 'open_url', url: 'https://github.com/mustakim-init/WinWhisperFlow/issues' })}
              className="text-accent hover:text-accent/80 transition-colors"
            >
              <ExternalLink size={16} />
            </button>
          }
        />
        <SettingRow
          title="License"
          description="Non-Commercial (free) / Commercial (paid)"
        />
      </SettingSection>
    </div>
  );
}
