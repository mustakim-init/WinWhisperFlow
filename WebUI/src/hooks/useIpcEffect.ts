import { useEffect, useRef } from 'react';
import { onMessage } from '../bridge/ipc';
import type { S2CMessage } from '../types/messages';

export function useIpcEffect<T extends S2CMessage['type']>(
  type: T,
  handler: (msg: Extract<S2CMessage, { type: T }>) => void
) {
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect(() => {
    const unsub = onMessage((msg) => {
      if (msg.type === type) {
        (handlerRef.current as (msg: S2CMessage) => void)(msg);
      }
    });
    return unsub;
  }, [type]);
}
