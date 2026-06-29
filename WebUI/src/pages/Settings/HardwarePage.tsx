import React from 'react';
import { Cpu, MemoryStick, Monitor } from 'lucide-react';
import { Badge } from '../../components/ui/Badge';
import { useStore } from '../../hooks/useStore';

function formatBytes(bytes: number): string {
  if (!bytes) return 'Unknown';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${(bytes / k ** i).toFixed(i < 2 ? 0 : 1)} ${sizes[i]}`;
}

const modelRequirements: Record<string, { cpu: string; ram: string; disk: string; gpuVram: string }> = {
  tiny:    { cpu: 'Any dual-core',          ram: '1 GB',   disk: '75 MB',  gpuVram: '1 GB' },
  base:    { cpu: 'Any dual-core',          ram: '2 GB',   disk: '141 MB', gpuVram: '1 GB' },
  small:   { cpu: 'Quad-core 2.5 GHz+',     ram: '4 GB',   disk: '464 MB', gpuVram: '2 GB' },
  medium:  { cpu: 'Quad-core 3.0 GHz+',     ram: '6 GB',   disk: '1.4 GB', gpuVram: '4 GB' },
  'large-v3': { cpu: '6+ cores, 3.0 GHz+',  ram: '8 GB',   disk: '2.9 GB', gpuVram: '6 GB' },
  turbo:   { cpu: '6+ cores, 3.0 GHz+',     ram: '8 GB',   disk: '4.1 GB', gpuVram: '6 GB' },
};

function GpuIcon({ className }: { className?: string }) {
  return (
    <svg className={`h-5 w-5 shrink-0 text-accent ${className ?? ''}`} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="4" y="6" width="16" height="12" rx="2" />
      <path d="M2 10h2M2 14h2M20 10h2M20 14h2" />
      <path d="M9 10h6M9 14h4" />
    </svg>
  );
}

const deviceLabel: Record<string, string> = { cuda: 'NVIDIA CUDA', dml: 'DirectML', cpu: 'CPU (no GPU)' };
const badgeVariant: Record<string, 'success' | 'warning' | 'secondary'> = { cuda: 'success', dml: 'warning', cpu: 'secondary' };

export function HardwarePage() {
  const store = useStore();
  const dd = store.detectedDevice;
  const hasGpu = dd !== 'cpu';

  return (
    <div className="space-y-8 max-w-2xl">

      {/* Quick info cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="rounded-xl border border-border bg-card p-4 flex items-center gap-4">
          <div className="w-10 h-10 rounded-full bg-accent/15 flex items-center justify-center shrink-0">
            <Cpu className="w-5 h-5 text-accent" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium">{store.cpuName || 'Unknown CPU'}</p>
            <p className="text-xs text-muted-foreground">
              {store.cpuCores || '?'} cores / {store.cpuThreads || '?'} threads
            </p>
          </div>
        </div>

        <div className="rounded-xl border border-border bg-card p-4 flex items-center gap-4">
          <div className="w-10 h-10 rounded-full bg-accent/15 flex items-center justify-center shrink-0">
            <MemoryStick className="w-5 h-5 text-accent" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium">{formatBytes(store.totalRam)}</p>
            <p className="text-xs text-muted-foreground">System RAM</p>
          </div>
        </div>
      </div>

      {/* GPU card */}
      <div className="rounded-xl border border-border bg-card p-4 flex items-center gap-4">
        <div className="w-10 h-10 rounded-full bg-accent/15 flex items-center justify-center shrink-0">
          {hasGpu ? <GpuIcon /> : <Monitor className="w-5 h-5 text-muted-foreground" />}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <p className="text-sm font-medium">{hasGpu ? store.detectedGpuName : 'No GPU detected'}</p>
            <Badge variant={badgeVariant[dd]} className="text-[10px]">{dd.toUpperCase()}</Badge>
          </div>
          <p className="text-xs text-muted-foreground">{deviceLabel[dd]}</p>
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

      {/* Model guidance */}
      <div>
        <div className="flex items-baseline gap-2 mb-1">
          <h2 className="text-sm font-semibold">Which model should I use?</h2>
          <span className="text-[10px] text-muted-foreground">Requirements per model</span>
        </div>
        <div className="border border-border bg-card rounded-xl divide-y divide-border/60 overflow-hidden">
          {/* Header */}
          <div className="grid grid-cols-5 gap-2 px-4 py-2.5 text-[10px] font-semibold text-muted-foreground uppercase tracking-wider bg-muted/30">
            <span>Model</span>
            <span>CPU</span>
            <span>RAM</span>
            <span>Disk</span>
            <span>GPU VRAM</span>
          </div>
          {/* Rows */}
          {Object.entries(modelRequirements).map(([name, req]) => (
            <div key={name} className="grid grid-cols-5 gap-2 px-4 py-2.5 text-xs hover:bg-muted/20 transition-colors">
              <span className="font-medium capitalize">{name}</span>
              <span className="text-muted-foreground">{req.cpu}</span>
              <span className="text-muted-foreground">{req.ram}</span>
              <span className="text-muted-foreground">{req.disk}</span>
              <span className="text-muted-foreground">{req.gpuVram}</span>
            </div>
          ))}
        </div>
        <p className="text-xs text-muted-foreground/60 mt-3 leading-relaxed">
          CPU models use faster-whisper (CTranslate2, recommended for most users).
          GPU models use sherpa-onnx — requires a compatible NVIDIA GPU with CUDA 12.x or a DirectML-compatible GPU.
          GPU downloads are significantly larger (include FP32 + INT8 encoder/decoder pairs).
        </p>
      </div>
    </div>
  );
}
