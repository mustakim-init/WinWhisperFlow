import React from 'react';
import { motion } from 'framer-motion';
import { Mic, Smartphone, Box, Settings, Captions } from 'lucide-react';

export type PageId = 'dictate' | 'captures' | 'phone-mic' | 'models' | 'settings';

interface NavItem {
  id: PageId;
  label: string;
  icon: React.ReactNode;
}

const navItems: NavItem[] = [
  { id: 'dictate', label: 'Dictate', icon: <Mic size={20} /> },
  { id: 'captures', label: 'Captures', icon: <Captions size={20} /> },
  { id: 'phone-mic', label: 'Phone Mic', icon: <Smartphone size={20} /> },
  { id: 'models', label: 'Models', icon: <Box size={20} /> },
  { id: 'settings', label: 'Settings', icon: <Settings size={20} /> },
];

interface SidebarProps {
  active: PageId;
  onChange: (id: PageId) => void;
  ready: boolean;
}

export function Sidebar({ active, onChange, ready }: SidebarProps) {
  return (
    <nav className="fixed left-0 top-0 h-full w-20 bg-sidebar border-r border-border flex flex-col items-center py-6 gap-6 select-none z-40">
      {/* Logo */}
      <div className="w-10 h-10 rounded-full bg-accent/20 flex items-center justify-center mb-2 sidebar-logo">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="w-5 h-5 text-accent">
          <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
          <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
          <line x1="12" x2="12" y1="19" y2="22" />
        </svg>
      </div>

      {/* Navigation */}
      <div className="flex flex-col gap-2">
        {navItems.map((item, index) => {
          const isActive = active === item.id;
          const accentOpacity = Math.max(0.08, 0.5 - index * 0.07);

          return (
            <button
              key={item.id}
              onClick={() => onChange(item.id)}
              className={`relative w-12 h-12 rounded-full flex items-center justify-center transition-all duration-200 overflow-hidden ${
                isActive
                  ? 'bg-white/[0.07] text-foreground shadow-lg backdrop-blur-sm border border-white/[0.08] ring-1 ring-primary/30'
                  : 'text-muted-foreground hover:text-foreground hover:bg-muted/50'
              }`}
              title={item.label}
              aria-label={item.label}
            >
              {isActive && (
                <motion.div
                  layoutId="sidebar-active"
                  className="absolute inset-0 rounded-full pointer-events-none"
                  style={{
                    maskImage: 'linear-gradient(to bottom, black, transparent 60%)',
                    WebkitMaskImage: 'linear-gradient(to bottom, black, transparent 60%)',
                    border: `1px solid hsl(var(--accent) / ${accentOpacity})`,
                  }}
                  transition={{ type: 'spring', stiffness: 300, damping: 30 }}
                />
              )}
              {item.icon}
            </button>
          );
        })}
      </div>

      {/* Status / Version */}
      <div className="mt-auto flex flex-col items-center gap-1">
        {ready ? (
          <span className="flex items-center gap-1 text-[10px] text-muted-foreground/50">
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-400" />
            v1.0
          </span>
        ) : (
          <span className="flex items-center gap-1 text-[10px] text-muted-foreground/50">
            <span className="w-1.5 h-1.5 rounded-full bg-amber-400 animate-pulse" />
            setup
          </span>
        )}
      </div>
    </nav>
  );
}
