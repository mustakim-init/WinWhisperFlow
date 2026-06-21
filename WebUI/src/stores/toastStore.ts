import { create } from 'zustand';

export interface Toast {
  id: string;
  title: string;
  message?: string;
  variant: 'info' | 'success' | 'warning' | 'error';
  duration?: number;
}

interface ToastStore {
  toasts: Toast[];
  addToast: (toast: Omit<Toast, 'id'>) => void;
  removeToast: (id: string) => void;
}

let nextId = 0;

export const useToastStore = create<ToastStore>((set) => ({
  toasts: [],
  addToast: (toast) => {
    const id = `toast-${nextId++}`;
    set((state) => ({ toasts: [...state.toasts, { ...toast, id }] }));
    const ms = toast.duration ?? (toast.variant === 'error' ? 8000 : 4000);
    setTimeout(() => {
      set((state) => ({ toasts: state.toasts.filter((t) => t.id !== id) }));
    }, ms);
  },
  removeToast: (id) => set((state) => ({ toasts: state.toasts.filter((t) => t.id !== id) })),
}));
