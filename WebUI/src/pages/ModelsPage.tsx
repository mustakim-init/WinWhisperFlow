import React, { useState, useEffect, useRef } from 'react';
import { Download, Trash2, Check, Loader2 } from 'lucide-react';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { Progress } from '../components/ui/Progress';
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger } from '../components/ui/AlertDialog';
import { send } from '../bridge/ipc';
import { useIpcEffect } from '../hooks/useIpcEffect';
import { useDownloadStore } from '../stores/downloadStore';

function formatSize(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${(bytes / k ** i).toFixed(i === 0 ? 0 : 1)} ${sizes[i]}`;
}

function formatSpeed(bytesPerSec: number): string {
  if (bytesPerSec <= 0) return '';
  const k = 1024;
  const sizes = ['B/s', 'KB/s', 'MB/s', 'GB/s'];
  const i = Math.floor(Math.log(bytesPerSec) / Math.log(k));
  return `${(bytesPerSec / k ** i).toFixed(i <= 1 ? 0 : 1)} ${sizes[i]}`;
}

interface ModelEntry {
  name: string;
  displayName: string;
  size: number;
  downloaded: boolean;
  loaded: boolean;
  downloading?: boolean;
  progress?: number;
  provider: string;
}

const providerLabel: Record<string, string> = {
  cpu: 'CPU (faster-whisper)',
  cuda: 'GPU \u2014 CUDA (faster-whisper)',
  dml: 'GPU \u2014 DirectML (sherpa-onnx)',
  demucs: 'Tools',
};

export function ModelsPage() {
  const [byProvider, setByProvider] = useState<Record<string, ModelEntry[]>>({});
  const downloadState = useDownloadStore((s) => s.downloads);
  const pollRef = useRef<ReturnType<typeof setInterval>>(undefined);

  // Poll models_status while any download is active
  useEffect(() => {
    const hasActive = Object.values(downloadState).some((d) => d.status === 'downloading');
    if (hasActive && !pollRef.current) {
      pollRef.current = setInterval(() => send({ type: 'get_models_status' }), 2000);
    } else if (!hasActive && pollRef.current) {
      clearInterval(pollRef.current);
      pollRef.current = undefined;
    }
    return () => {
      if (pollRef.current) {
        clearInterval(pollRef.current);
        pollRef.current = undefined;
      }
    };
  }, [downloadState]);

  useIpcEffect('models_status', (msg) => {
    setByProvider((prev) => {
      const grouped: Record<string, ModelEntry[]> = {};
      for (const m of msg.models) {
        const prevEntry = Object.values(prev).flat().find((e) => e.name === m.name);
        const entry: ModelEntry & { provider: string } = {
          name: m.name,
          displayName: m.displayName,
          size: m.size,
          downloaded: m.downloaded,
          loaded: m.loaded,
          provider: m.provider,
          downloading: prevEntry?.downloading ?? false,
          progress: prevEntry?.progress,
        };
        if (!grouped[m.provider]) grouped[m.provider] = [];
        grouped[m.provider].push(entry);
      }
      return grouped;
    });
  });

  useIpcEffect('model_download_progress', (msg) => {
    const composite = msg.compositeName ?? msg.model;
    const pct = msg.total > 0 ? (msg.downloaded / msg.total) * 100 : 0;
    setByProvider((prev) => {
      const next: Record<string, ModelEntry[]> = {};
      for (const [prov, list] of Object.entries(prev)) {
        next[prov] = list.map((m) =>
          m.name === composite ? { ...m, downloading: msg.status === 'downloading', progress: pct } : m
        );
      }
      return next;
    });
  });

  useIpcEffect('model_loaded', (msg) => {
    setByProvider((prev) => {
      const next: Record<string, ModelEntry[]> = {};
      for (const [prov, list] of Object.entries(prev)) {
        next[prov] = list.map((m) => ({
          ...m,
          loaded: m.name === msg.model,
          downloaded: m.name === msg.model ? true : m.downloaded,
        }));
      }
      return next;
    });
  });

  useEffect(() => {
    send({ type: 'get_models_status' });
  }, []);

  const handleDownload = (model: string) => {
    send({ type: 'download_model', model });
  };

  const handleDelete = (model: string) => {
    send({ type: 'delete_model', model });
    setByProvider((prev) => {
      const next: Record<string, ModelEntry[]> = {};
      for (const [prov, list] of Object.entries(prev)) {
        next[prov] = list.map((m) => (m.name === model ? { ...m, downloaded: false, loaded: false } : m));
      }
      return next;
    });
  };

  const handleLoad = (model: string) => {
    send({ type: 'load_model', model });
  };

  const renderModelRow = (m: ModelEntry) => {
    const dl = downloadState[m.name];
    const downloading = dl?.status === 'downloading';
    const paused = dl?.status === 'paused';
    const progress = dl ? (dl.total > 0 ? (dl.downloaded / dl.total) * 100 : 0) : m.progress;

    return (
      <div
        key={m.name}
        className="flex items-center justify-between gap-4 px-4 py-3.5 hover:bg-muted/40 transition-colors"
      >
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-sm font-medium">{m.displayName}</span>
            {m.loaded && (
              <Badge className="text-[10px] bg-accent/10 border-accent/20 text-accent font-medium hover:bg-accent/15">
                Loaded
              </Badge>
            )}
            {m.downloaded && !m.loaded && (
              <Badge variant="outline" className="text-[10px] border-emerald-500/30 text-emerald-400 bg-emerald-500/5 font-medium">
                Downloaded
              </Badge>
            )}
            {paused && (
              <Badge variant="outline" className="text-[10px] border-amber-500/30 text-amber-400 bg-amber-500/5 font-medium">
                Paused
              </Badge>
            )}
          </div>
          <div className="text-xs text-muted-foreground mt-1">
            {downloading ? (
              <span>
                {formatSize(dl?.downloaded ?? 0)} / {formatSize(dl?.total ?? 0)}
                {dl?.speed ? <span className="ml-2 text-accent/70">({formatSpeed(dl.speed)})</span> : null}
              </span>
            ) : (
              formatSize(m.size)
            )}
          </div>
          {(downloading || paused) && dl && (
            <div className="mt-1.5 space-y-1">
              <Progress value={progress ?? 0} className="h-1 w-32" />
              <div className="text-[10px] text-muted-foreground">
                {Math.round(progress ?? 0)}%
              </div>
            </div>
          )}
        </div>

        <div className="shrink-0">
          {downloading ? (
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                className="text-[11px] h-8"
                onClick={() => send({ type: 'pause_download', model: m.name })}
              >
                Pause
              </Button>
              <Button
                variant="outline"
                size="sm"
                className="text-[11px] h-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                onClick={() => send({ type: 'cancel_download', model: m.name })}
              >
                Cancel
              </Button>
            </div>
          ) : paused ? (
            <div className="flex gap-2">
              <Button
                variant="default"
                size="sm"
                className="h-8 text-xs"
                onClick={() => send({ type: 'resume_download', model: m.name })}
              >
                Resume
              </Button>
              <Button
                variant="outline"
                size="sm"
                className="text-[11px] h-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                onClick={() => send({ type: 'cancel_download', model: m.name })}
              >
                Cancel
              </Button>
            </div>
          ) : m.downloaded ? (
            <div className="flex gap-2">
              <AlertDialog>
                <AlertDialogTrigger asChild>
                  <Button
                    variant="outline"
                    size="sm"
                    className="text-destructive hover:bg-destructive/10 hover:text-destructive h-8 w-8 p-0"
                    title="Delete Model"
                  >
                    <Trash2 size={13} />
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>Delete {m.displayName}?</AlertDialogTitle>
                    <AlertDialogDescription>
                      This will remove the model files from disk ({formatSize(m.size)}).
                      You can download it again later.
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Cancel</AlertDialogCancel>
                    <AlertDialogAction onClick={() => handleDelete(m.name)} className="bg-destructive text-destructive-foreground hover:bg-destructive/90">
                      Delete
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
              {m.provider !== 'demucs' && (
                <Button
                  variant="secondary"
                  size="sm"
                  className="h-8 min-w-16 text-xs"
                  onClick={() => handleLoad(m.name)}
                  disabled={m.loaded}
                >
                  {m.loaded ? <Check size={13} className="text-emerald-400" /> : 'Load'}
                </Button>
              )}
            </div>
          ) : (
            <Button
              variant="default"
              size="sm"
              className="h-8 gap-1 text-xs"
              onClick={() => handleDownload(m.name)}
            >
              <Download size={13} /> Download
            </Button>
          )}
        </div>
      </div>
    );
  };

  const providerOrder = ['cpu', 'cuda', 'dml', 'demucs'];

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="shrink-0 pt-8 pb-4 max-w-2xl mx-auto w-full">
        <h1 className="text-2xl font-bold">Models</h1>
        <p className="text-sm text-muted-foreground mt-0.5">Manage models for transcription and hardware acceleration</p>
      </div>

      <div className="flex-1 min-h-0 overflow-y-auto scrollbar-hide">
        <div className="max-w-2xl mx-auto w-full space-y-6 pb-8">
          {providerOrder.map((prov) => {
            const list = byProvider[prov];
            if (!list || list.length === 0) return null;
            return (
              <div key={prov} className="space-y-2">
                <h2 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider px-1">
                  {providerLabel[prov] || prov}
                </h2>
                <div className="border border-border bg-card rounded-xl divide-y divide-border/60 overflow-hidden shadow-sm">
                  {list.map(renderModelRow)}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

