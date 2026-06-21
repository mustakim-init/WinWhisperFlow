import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Check, Loader2, X, Circle, ArrowRight } from 'lucide-react';
import type { SetupStep } from '../types/messages';

interface ReadinessPageProps {
  steps: SetupStep[];
  overall: number;
  error?: string;
  statusText?: string;
  onRetry?: () => void;
  onContinue?: () => void;
}

const statusIcon = (status: SetupStep['status']) => {
  switch (status) {
    case 'done': return <Check size={16} className="text-emerald-400" />;
    case 'running': return <Loader2 size={16} className="text-accent animate-spin" />;
    case 'error': return <X size={16} className="text-destructive" />;
    case 'pending': return <Circle size={16} className="text-muted-foreground/40" />;
  }
};

const allStepsDone = (steps: SetupStep[]) =>
  steps.length > 0 && steps.every((s) => s.status === 'done');

export function ReadinessPage({ steps, overall, error, statusText, onRetry, onContinue }: ReadinessPageProps) {
  const loadingModel = allStepsDone(steps) && !error;
  const canContinue = !!error && allStepsDone(steps);

  return (
    <div className="fixed inset-0 z-50 bg-background flex items-center justify-center">
      <div className="w-full max-w-md mx-auto px-6">
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4 }}
          className="text-center mb-8"
        >
          <div className="w-20 h-20 rounded-full bg-accent/20 flex items-center justify-center mx-auto mb-4 relative">
            <div className="absolute inset-0 rounded-full bg-accent/20 blur-3xl" />
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="w-10 h-10 text-accent relative">
              <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
              <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
              <line x1="12" x2="12" y1="19" y2="22" />
            </svg>
          </div>
          <h1 className="text-xl font-semibold mb-1 animate-fade-in-scale">Setting up WinWhisper Flow</h1>
          <p className="text-sm text-muted-foreground animate-fade-in-delayed">Preparing your speech-to-text engine</p>
        </motion.div>

        <div className="space-y-2 mb-6">
          <AnimatePresence mode="popLayout">
            {steps.map((step, i) => (
              <motion.div
                key={step.id}
                initial={{ opacity: 0, x: -10 }}
                animate={{ opacity: 1, x: 0 }}
                transition={{ delay: i * 0.05 }}
                className={`flex items-center gap-3 px-4 py-3 rounded-xl border ${
                  step.status === 'error' ? 'border-destructive/30 bg-destructive/10' :
                  step.status === 'done' ? 'border-emerald-500/20 bg-emerald-500/5' :
                  step.status === 'running' ? 'border-accent/20 bg-accent/5' :
                  'border-border bg-card/50'
                }`}
              >
                <div className="shrink-0">{statusIcon(step.status)}</div>
                <div className="flex-1 min-w-0">
                  <div className="text-sm font-medium">{step.label}</div>
                  {step.error && <div className="text-xs text-destructive mt-0.5">{step.error}</div>}
                </div>
              </motion.div>
            ))}
          </AnimatePresence>
        </div>

        {overall < 100 && overall > 0 && (
          <div className="mb-6">
            <div className="h-1.5 w-full rounded-full bg-muted overflow-hidden">
              <motion.div
                initial={{ width: 0 }}
                animate={{ width: `${overall}%` }}
                className="h-full rounded-full bg-accent"
                transition={{ duration: 0.5 }}
              />
            </div>
            <p className="text-xs text-muted-foreground text-center mt-2">{Math.round(overall)}%</p>
          </div>
        )}

        {loadingModel && statusText && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            className="text-center mb-4"
          >
            <div className="flex items-center justify-center gap-2 text-sm text-accent">
              <Loader2 size={16} className="animate-spin" />
              <span>{statusText}</span>
            </div>
          </motion.div>
        )}

        {loadingModel && !statusText && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            className="text-center mb-4"
          >
            <div className="flex items-center justify-center gap-2 text-sm text-muted-foreground">
              <Loader2 size={16} className="animate-spin" />
              <span>Loading model\u2026</span>
            </div>
          </motion.div>
        )}

        {error && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            className="text-center"
          >
            <p className="text-sm text-destructive mb-3">{error}</p>
            <div className="flex items-center justify-center gap-3">
              {onRetry && (
                <button
                  onClick={onRetry}
                  className="px-5 py-2 rounded-full bg-accent text-accent-foreground text-sm font-medium hover:opacity-90 transition-opacity"
                >
                  Retry Setup
                </button>
              )}
              {canContinue && onContinue && (
                <button
                  onClick={onContinue}
                  className="px-5 py-2 rounded-full border border-border text-sm font-medium hover:bg-muted/50 transition-colors flex items-center gap-1.5"
                >
                  Continue without model <ArrowRight size={14} />
                </button>
              )}
            </div>
          </motion.div>
        )}

        {!error && steps.length === 0 && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            className="text-center"
          >
            <Loader2 size={24} className="animate-spin mx-auto text-accent" />
          </motion.div>
        )}
      </div>
    </div>
  );
}
