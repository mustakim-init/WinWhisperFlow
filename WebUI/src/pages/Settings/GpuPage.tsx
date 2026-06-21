import React from 'react';
import { Cpu } from 'lucide-react';
import { Badge } from '../../components/ui/Badge';
import { useStore } from '../../hooks/useStore';
import { SettingRow, SettingSection } from '../../components/ui/SettingRow';

function GpuAccelIcon({ className, device }: { className?: string; device: string }) {
  if (device === 'cpu') return <Cpu className={`h-5 w-5 shrink-0 text-muted-foreground ${className ?? ''}`} />;
  return (
    <svg className={`h-5 w-5 shrink-0 text-accent ${className ?? ''}`} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="4" y="6" width="16" height="12" rx="2" />
      <path d="M2 10h2M2 14h2M20 10h2M20 14h2" />
      <path d="M9 10h6M9 14h4" />
    </svg>
  );
}

function GpuInfoCard({ device, gpuName }: { device: string; gpuName: string }) {
  const hasGpu = device !== 'cpu';
  const gpuLabel = device === 'cuda' ? 'CUDA' : device === 'dml' ? 'DirectML' : null;
  const displayGpu = gpuName && hasGpu ? gpuName : null;

  return (
    <div className="rounded-lg border border-border/60 p-4">
      <div className="flex items-center gap-3">
        <GpuAccelIcon device={device} />
        <div className="flex-1 min-w-0 space-y-0.5">
          <div className="text-sm font-medium">
            {displayGpu ?? (hasGpu ? `${gpuLabel} GPU` : 'CPU Only')}
          </div>
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
            {hasGpu ? (
              <>
                <span>{gpuLabel}</span>
                <span className="text-border">|</span>
                <span>{device.toUpperCase()}</span>
              </>
            ) : (
              <span>No GPU acceleration detected. Running on CPU.</span>
            )}
          </div>
        </div>
        {hasGpu && (
          <div className="flex items-center gap-2 rounded-full border border-accent/30 px-2.5 py-0.5">
            <span className="relative flex h-1.5 w-1.5">
              <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-accent/60" />
              <span className="relative inline-flex h-1.5 w-1.5 rounded-full bg-accent shadow-[0_0_4px_1px_hsl(var(--accent)/0.4)]" />
            </span>
            <span className="text-[10px] font-medium text-muted-foreground">Active</span>
          </div>
        )}
      </div>
    </div>
  );
}

export function GpuPage() {
  const store = useStore();
  const device = store.device;

  const badgeVariant = device === 'cuda' ? 'success' : device === 'dml' ? 'warning' : 'secondary';

  return (
    <div className="space-y-8 max-w-2xl">
      <GpuInfoCard device={device} gpuName={store.gpuName} />

      <SettingSection title="GPU Acceleration" description="Current hardware acceleration backend">
        <SettingRow
          title="Backend"
          description={device === 'cuda' ? 'NVIDIA CUDA' : device === 'dml' ? 'DirectML (Windows)' : 'CPU (no GPU)'}
          action={
            <Badge variant={badgeVariant}>
              {device === 'cuda' ? 'CUDA' : device === 'dml' ? 'DirectML' : 'CPU'}
            </Badge>
          }
        >
          <div className="bg-muted rounded-lg px-3 py-2 text-xs text-muted-foreground space-y-1 mt-2">
            <p><strong>Device:</strong> {device.toUpperCase()}</p>
            <p className="pt-1">
              {device === 'cuda' && 'NVIDIA CUDA acceleration is active. Models will run on your GPU for faster transcription.'}
              {device === 'dml' && 'DirectML acceleration is active on this Windows 10+ system.'}
              {device === 'cpu' && 'Running on CPU. A compatible NVIDIA GPU with CUDA can significantly improve transcription speed.'}
            </p>
          </div>
        </SettingRow>
      </SettingSection>

      <p className="text-xs text-muted-foreground/60 leading-relaxed">
        GPU acceleration requires a compatible NVIDIA GPU with CUDA 12.x or a DirectML-compatible GPU.
        Model download sizes and compatibility vary by backend.
      </p>
    </div>
  );
}
