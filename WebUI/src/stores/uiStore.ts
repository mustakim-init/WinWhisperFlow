import { create } from 'zustand';
import type { PageId } from '../components/Sidebar';

interface UiState {
  page: PageId;
  setPage: (page: PageId) => void;

  darkMode: boolean;
  setDarkMode: (dark: boolean) => void;

  isReady: boolean;
  setIsReady: (ready: boolean) => void;

  statusText: string;
  statusVariant: 'success' | 'warning' | 'error' | '';
  setStatus: (text: string, variant?: 'success' | 'warning' | 'error' | '') => void;

  setupSteps: { id: string; label: string; status: string; error?: string }[];
  setSetupSteps: (steps: { id: string; label: string; status: string; error?: string }[]) => void;
}

export const useUiStore = create<UiState>((set) => ({
  page: 'dictate',
  setPage: (page) => set({ page }),

  darkMode: true,
  setDarkMode: (dark) => {
    document.documentElement.classList.toggle('dark', dark);
    set({ darkMode: dark });
  },

  isReady: false,
  setIsReady: (ready) => set({ isReady: ready }),

  statusText: '',
  statusVariant: '',
  setStatus: (text, variant = '') => set({ statusText: text, statusVariant: variant }),

  setupSteps: [],
  setSetupSteps: (steps) => set({ setupSteps: steps }),
}));
