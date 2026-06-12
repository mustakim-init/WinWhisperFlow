export interface ListItemData {
  action: string;
  text: string;
  timestamp: string;
}

export function createList(
  items: ListItemData[],
  containerStyle?: Partial<CSSStyleDeclaration>
): HTMLDivElement {
  const list = document.createElement('div');
  Object.assign(list.style, {
    display: 'flex', flexDirection: 'column', gap: '0',
    borderRadius: '8px',
    overflow: 'hidden',
    border: '1px solid var(--md-sys-color-outline-variant)',
    ...containerStyle,
  });

  for (const item of items) {
    const row = document.createElement('div');
    row.style.cssText = `
      display: flex; align-items: center; gap: 8px;
      padding: 10px 12px;
      background: transparent;
      transition: background var(--md-sys-motion-duration-short) var(--md-sys-motion-easing-standard);
      cursor: default;
    `;

    const actionBadge = document.createElement('span');
    actionBadge.textContent = item.action;
    actionBadge.style.cssText = `
      padding: 2px 6px; border-radius: 4px;
      background: var(--md-sys-color-primary-container);
      color: var(--md-sys-color-on-primary-container);
      font-size: 11px; font-weight: 600;
      flex-shrink: 0;
    `;

    const textSpan = document.createElement('span');
    textSpan.textContent = item.text;
    textSpan.style.cssText = `
      flex: 1; font-size: 12px; color: var(--md-sys-color-on-surface);
      overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
    `;

    const timeSpan = document.createElement('span');
    timeSpan.textContent = item.timestamp;
    timeSpan.style.cssText = `
      font-size: 11px; font-weight: 600;
      color: var(--md-sys-color-on-surface-variant);
      flex-shrink: 0;
    `;

    row.appendChild(actionBadge);
    row.appendChild(textSpan);
    row.appendChild(timeSpan);
    row.addEventListener('mouseenter', () => {
      row.style.background = 'var(--md-sys-color-surface-container-highest)';
    });
    row.addEventListener('mouseleave', () => {
      row.style.background = 'transparent';
    });

    list.appendChild(row);

    // Divider between items except last
    if (item !== items[items.length - 1]) {
      const divider = document.createElement('div');
      divider.style.cssText = 'height: 1px; background: var(--md-sys-color-outline-variant); margin: 0 12px;';
      list.appendChild(divider);
    }
  }

  return list;
}
