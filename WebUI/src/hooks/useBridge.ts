import { useCallback } from 'react';
import { send } from '../bridge/ipc';
import type { C2SMessage } from '../types/messages';

export function useBridge() {
  const post = useCallback((msg: C2SMessage) => send(msg), []);
  return { send: post };
}
