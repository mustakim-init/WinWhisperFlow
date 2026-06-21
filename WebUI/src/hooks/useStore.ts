import { useSyncExternalStore } from 'react';
import { subscribe, getState } from '../lib/store';

export function useStore() {
  return useSyncExternalStore(subscribe, getState);
}
