export function createStatusPill(text: string, variant: 'success' | 'warning' | 'error' = 'success'): HTMLDivElement {
  const vars = {
    success: { bg: 'var(--md-sys-color-primary-container)', dot: 'var(--md-sys-color-primary)', text: 'var(--md-sys-color-on-primary-container)' },
    warning: { bg: 'var(--md-sys-color-secondary-container)', dot: 'var(--md-sys-color-secondary)', text: 'var(--md-sys-color-on-secondary-container)' },
    error: { bg: 'var(--md-sys-color-error-container)', dot: 'var(--md-sys-color-error)', text: 'var(--md-sys-color-on-error-container)' },
  };

  const c = vars[variant];

  const pill = document.createElement('div');
  pill.style.cssText = `
    display: inline-flex; align-items: center; gap: 7px;
    padding: 6px 12px; border-radius: 20px;
    background: ${c.bg};
  `;

  const dot = document.createElement('div');
  dot.style.cssText = `width: 7px; height: 7px; border-radius: 50%; background: ${c.dot}; flex-shrink: 0;`;
  if (variant === 'warning') {
    dot.style.animation = 'pulse-dot 1.4s ease-in-out infinite';
  }

  const textEl = document.createElement('span');
  textEl.style.cssText = `font-size: 12px; font-weight: 600; color: ${c.text};`;
  textEl.textContent = text;

  pill.appendChild(dot);
  pill.appendChild(textEl);

  return pill;
}
