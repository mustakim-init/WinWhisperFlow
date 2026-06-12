export function createBadge(
  text: string,
  dotColor?: string
): HTMLDivElement {
  const badge = document.createElement('div');
  badge.style.cssText = `
    display: inline-flex; align-items: center; gap: 6px;
    padding: 4px 10px; border-radius: 6px;
    background: var(--md-sys-color-primary-container);
    border: 1px solid var(--md-sys-color-primary);
    font-size: 11px; font-weight: 600;
    color: var(--md-sys-color-on-primary-container);
  `;

  if (dotColor) {
    const dot = document.createElement('div');
    dot.style.cssText = `
      width: 6px; height: 6px; border-radius: 50%;
      background: ${dotColor};
      flex-shrink: 0;
    `;
    badge.appendChild(dot);
  }

  const textEl = document.createElement('span');
  textEl.textContent = text;
  badge.appendChild(textEl);

  return badge;
}
