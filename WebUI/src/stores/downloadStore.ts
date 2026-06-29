import { create } from 'zustand';

export interface DownloadState {
  active: boolean;
  compositeName: string | null;
  downloaded: number;
  total: number;
  status: 'idle' | 'downloading' | 'paused' | 'done' | 'error' | 'cancelled';
  error?: string;
  speed?: number;
}

interface DownloadStore {
  downloads: Record<string, DownloadState>;
  setProgress: (compositeName: string, downloaded: number, total: number, status: DownloadState['status'], error?: string, speed?: number) => void;
  clear: (compositeName: string) => void;
  isActive: (compositeName: string) => boolean;
  isPaused: (compositeName: string) => boolean;
}

export const useDownloadStore = create<DownloadStore>((set, get) => ({
  downloads: {},

  setProgress: (compositeName, downloaded, total, status, error, speed) =>
    set((state) => ({
      downloads: {
        ...state.downloads,
        [compositeName]: { active: status === 'downloading', compositeName, downloaded, total, status, error, speed },
      },
    })),

  clear: (compositeName) =>
    set((state) => {
      const next = { ...state.downloads };
      delete next[compositeName];
      return { downloads: next };
    }),

  isActive: (compositeName) => get().downloads[compositeName]?.active ?? false,
  isPaused: (compositeName) => get().downloads[compositeName]?.status === 'paused',
}));
