import React, { useState, useEffect } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { Play, StopCircle, Copy, Terminal } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '../components/ui/Card';
import { Button } from '../components/ui/Button';
import { send } from '../bridge/ipc';
import { useStore } from '../hooks/useStore';

export function PhoneMicPage() {
  const store = useStore();
  const [qrDataUrl, setQrDataUrl] = useState('');
  const [logs, setLogs] = useState<string[]>([]);

  useEffect(() => {
    if (store.phoneMicUrl && store.phoneMicUrl !== 'Not started') {
      let cancelled = false;
      import('qrcode').then((mod) => {
        const QRCode = mod.default;
        QRCode.toDataURL(store.phoneMicUrl, { margin: 1, color: { dark: '#000000', light: '#ffffff' } }).then((dataUrl: string) => {
          if (!cancelled) setQrDataUrl(dataUrl);
        });
      }).catch(() => {});
      return () => { cancelled = true; };
    }
  }, [store.phoneMicUrl]);

  useEffect(() => {
    setLogs((prev) => {
      const next = [...prev];
      if (store.phoneMicRunning && !prev.some((l) => l.includes('Server started'))) {
        next.push(`Server started on ws://localhost:8766/`);
      }
      if (!store.phoneMicRunning && prev.length > 0 && !prev[prev.length - 1].includes('Server stopped')) {
        next.push(`Server stopped`);
      }
      return next.slice(-50);
    });
  }, [store.phoneMicRunning]);

  const handleToggle = () => {
    if (!store.phoneMicRunning) {
      setLogs((prev) => [...prev, 'Starting server...']);
    }
    send({ type: 'phone_mic_toggle' });
  };
  const handleCopy = () => send({ type: 'copy_text', text: store.phoneMicUrl });

  return (
    <div className="py-8 max-w-2xl mx-auto space-y-6 h-full overflow-y-auto w-full scrollbar-hide">
      <div>
        <h1 className="text-2xl font-bold">Phone Mic</h1>
        <p className="text-sm text-muted-foreground mt-0.5">Use your mobile device as a wireless microphone input</p>
      </div>

      <Card>
        <CardContent className="space-y-6 pt-6">
          <p className="text-sm text-muted-foreground leading-relaxed">
            Scan the QR code below or navigate to the connection URL on your phone's browser to stream audio over local Wi-Fi.
          </p>

          <div className="flex items-center gap-3 flex-wrap">
            <Button
              variant={store.phoneMicRunning ? 'destructive' : 'default'}
              onClick={handleToggle}
              className="gap-1.5"
            >
              {store.phoneMicRunning ? <StopCircle size={15} /> : <Play size={15} />}
              {store.phoneMicRunning ? 'Stop Server' : 'Start Server'}
            </Button>

            <div className="flex-1 flex items-center gap-2 bg-muted/60 px-3 py-1.5 rounded-full border border-border min-w-[200px]">
              <span className="flex-1 font-mono text-xs truncate select-all px-1 text-muted-foreground">{store.phoneMicUrl}</span>
              <Button
                variant="ghost"
                size="sm"
                className="h-7 rounded-full text-xs"
                onClick={handleCopy}
                disabled={!store.phoneMicUrl || store.phoneMicUrl === 'Not started'}
              >
                <Copy size={12} className="mr-1" /> Copy
              </Button>
            </div>
          </div>

          {store.phoneMicRunning && qrDataUrl && (
            <motion.div
              initial={{ opacity: 0, y: 5 }}
              animate={{ opacity: 1, y: 0 }}
              className="flex flex-col items-center gap-3 pt-2"
            >
              <div className="p-3.5 bg-white rounded-xl shadow-md border border-border">
                <img src={qrDataUrl} alt="QR Code" className="w-40 h-40" />
              </div>
              <p className="text-xs text-muted-foreground text-center max-w-xs leading-relaxed">
                Scan with your phone camera. If prompted, proceed to the address.
              </p>
            </motion.div>
          )}
        </CardContent>
      </Card>

      {logs.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              <Terminal size={13} /> Connection Log
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="bg-black/20 rounded-lg border border-border/80 p-4 max-h-36 overflow-y-auto font-mono text-[11px] leading-relaxed space-y-1 scrollbar-hide">
              <AnimatePresence initial={false}>
                {logs.map((log, i) => (
                  <motion.div
                    key={i}
                    initial={{ opacity: 0, x: -4 }}
                    animate={{ opacity: 1, x: 0 }}
                    className="text-muted-foreground/80"
                  >
                    &gt; {log}
                  </motion.div>
                ))}
              </AnimatePresence>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
