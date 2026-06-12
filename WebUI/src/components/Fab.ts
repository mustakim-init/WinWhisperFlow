export function createFab(options?: { expanded?: boolean; onClick?: () => void; icon?: string }): HTMLButtonElement {
  const fab = document.createElement('button');
  fab.innerHTML = options?.icon ?? '&#x1F3A4;';
  fab.style.cssText = `
    width: 56px; height: 56px; border-radius: 16px;
    background: var(--md-sys-color-primary);
    color: var(--md-sys-color-on-primary);
    font-size: 24px;
    border: none; cursor: pointer;
    box-shadow: var(--md-sys-elevation-3);
    transition: background var(--md-sys-motion-duration-short) var(--md-sys-motion-easing-standard),
                transform var(--md-sys-motion-duration-medium) var(--md-sys-motion-spring-bouncy),
                box-shadow var(--md-sys-motion-duration-short) var(--md-sys-motion-easing-standard);
    display: flex; align-items: center; justify-content: center;
  `;

  fab.addEventListener('click', () => options?.onClick?.());
  fab.addEventListener('mousedown', () => { fab.style.transform = 'scale(0.92)'; });
  fab.addEventListener('mouseup', () => { fab.style.transform = 'scale(1)'; });
  fab.addEventListener('mouseleave', () => { fab.style.transform = 'scale(1)'; });
  fab.addEventListener('mouseenter', () => { fab.style.boxShadow = 'var(--md-sys-elevation-4)'; });
  fab.addEventListener('mouseleave', () => { fab.style.boxShadow = 'var(--md-sys-elevation-3)'; });

  return fab;
}

export function createMiniFab(label: string, onClick?: () => void): HTMLButtonElement {
  const btn = document.createElement('button');
  btn.textContent = label;
  btn.style.cssText = `
    height: 40px; padding: 0 16px; border-radius: 12px;
    background: var(--md-sys-color-secondary-container);
    color: var(--md-sys-color-on-secondary-container);
    font-size: 12px; font-weight: 600;
    border: none; cursor: pointer;
    box-shadow: var(--md-sys-elevation-1);
    transition: transform var(--md-sys-motion-duration-short) var(--md-sys-motion-spring-bouncy);
    white-space: nowrap;
  `;
  btn.addEventListener('click', () => onClick?.());
  btn.addEventListener('mousedown', () => { btn.style.transform = 'scale(0.95)'; });
  btn.addEventListener('mouseup', () => { btn.style.transform = 'scale(1)'; });
  btn.addEventListener('mouseleave', () => { btn.style.transform = 'scale(1)'; });
  return btn;
}
