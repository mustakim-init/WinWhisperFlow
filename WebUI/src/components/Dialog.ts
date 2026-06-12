export function showDialog(
  title: string,
  message: string,
  options?: {
    actionText?: string;
    actionHandler?: () => void;
    variant?: 'info' | 'warning' | 'error';
  }
): HTMLDivElement {
  const existing = document.getElementById('m3-dialog-overlay');
  if (existing) existing.remove();

  const overlay = document.createElement('div');
  overlay.id = 'm3-dialog-overlay';
  overlay.setAttribute('role', 'presentation');
  overlay.style.cssText = `
    position: fixed; inset: 0; z-index: 1000;
    background: rgba(0,0,0,0.32);
    display: flex; align-items: center; justify-content: center;
    animation: dialog-fade-in var(--md-sys-motion-duration-medium) var(--md-sys-motion-easing-standard) forwards;
  `;

  if (!document.getElementById('m3-dialog-keyframes')) {
    const s = document.createElement('style');
    s.id = 'm3-dialog-keyframes';
    s.textContent = `
      @keyframes dialog-fade-in { from { opacity: 0; } to { opacity: 1; } }
      @keyframes dialog-scale-in { from { transform: scale(0.9); opacity: 0; } to { transform: scale(1); opacity: 1; } }
    `;
    document.head.appendChild(s);
  }

  const card = document.createElement('div');
  card.setAttribute('role', 'dialog');
  card.setAttribute('aria-modal', 'true');
  card.setAttribute('aria-labelledby', 'm3-dialog-title');
  card.setAttribute('aria-describedby', 'm3-dialog-message');
  card.style.cssText = `
    background: var(--md-sys-color-surface);
    border-radius: 28px; padding: 24px;
    max-width: 400px; width: 90%;
    box-shadow: var(--md-sys-elevation-3);
    animation: dialog-scale-in var(--md-sys-motion-duration-medium) cubic-bezier(0.34, 1.56, 0.64, 1) forwards;
  `;

  const titleEl = document.createElement('div');
  titleEl.id = 'm3-dialog-title';
  titleEl.className = 'title-medium';
  titleEl.textContent = title;
  titleEl.style.marginBottom = '16px';

  const msgEl = document.createElement('div');
  msgEl.id = 'm3-dialog-message';
  msgEl.className = 'body-medium';
  msgEl.textContent = message;
  msgEl.style.marginBottom = '24px';
  msgEl.style.color = 'var(--md-sys-color-on-surface-variant)';

  const actions = document.createElement('div');
  actions.style.cssText = 'display: flex; justify-content: flex-end; gap: 8px;';

  const dismiss = () => {
    document.removeEventListener('keydown', onKeyDown);
    overlay.remove();
  };

  const dismissBtn = document.createElement('button');
  dismissBtn.textContent = 'Dismiss';
  dismissBtn.setAttribute('aria-label', 'Dismiss dialog');
  Object.assign(dismissBtn.style, {
    height: '40px', padding: '0 24px', borderRadius: '20px',
    background: 'transparent', border: 'none',
    color: 'var(--md-sys-color-primary)', cursor: 'pointer',
    fontSize: '14px', fontWeight: '600',
  });
  dismissBtn.addEventListener('click', dismiss);

  actions.appendChild(dismissBtn);

  if (options?.actionText && options?.actionHandler) {
    const actionBtn = document.createElement('button');
    actionBtn.textContent = options.actionText;
    Object.assign(actionBtn.style, {
      height: '40px', padding: '0 24px', borderRadius: '20px',
      background: options.variant === 'error'
        ? 'var(--md-sys-color-error)'
        : options.variant === 'warning'
        ? 'var(--md-sys-color-warning)'
        : 'var(--md-sys-color-primary)',
      color: 'var(--md-sys-color-on-primary)', cursor: 'pointer',
      border: 'none', fontSize: '14px', fontWeight: '600',
    });
    actionBtn.addEventListener('click', () => {
      options.actionHandler?.();
      dismiss();
    });
    actions.appendChild(actionBtn);
  }

  card.appendChild(titleEl);
  card.appendChild(msgEl);
  card.appendChild(actions);
  overlay.appendChild(card);
  document.body.appendChild(overlay);

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Escape') dismiss();
  };
  document.addEventListener('keydown', onKeyDown);
  dismissBtn.focus();

  return overlay;
}
