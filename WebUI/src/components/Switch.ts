export function createSwitch(
  label: string,
  checked: boolean,
  onChange?: (checked: boolean) => void
): HTMLLabelElement {
  const labelEl = document.createElement('label');
  labelEl.style.cssText = `
    display: inline-flex; align-items: center; gap: 12px;
    cursor: pointer; font-size: 14px; color: var(--md-sys-color-on-surface);
    height: 28px;
  `;

  const track = document.createElement('div');
  track.style.cssText = `
    width: 44px; height: 24px; border-radius: 12px;
    background: ${checked ? 'var(--md-sys-color-primary)' : 'var(--md-sys-color-surface-container-highest)'};
    border: 2px solid ${checked ? 'var(--md-sys-color-primary)' : 'var(--md-sys-color-outline)'};
    position: relative; transition: all var(--md-sys-motion-duration-medium) var(--md-sys-motion-easing-standard);
    flex-shrink: 0;
  `;

  const thumb = document.createElement('div');
  thumb.style.cssText = `
    width: 16px; height: 16px; border-radius: 8px;
    background: ${checked ? 'var(--md-sys-color-on-primary)' : 'var(--md-sys-color-on-surface)'};
    position: absolute; top: 2px;
    transition: left var(--md-sys-motion-duration-medium) var(--md-sys-motion-easing-standard),
                background var(--md-sys-motion-duration-medium) var(--md-sys-motion-easing-standard);
    ${checked ? 'left: 24px;' : 'left: 2px;'}
  `;
  track.appendChild(thumb);

  const text = document.createElement('span');
  text.textContent = label;
  text.style.cssText = `
    font-size: 14px; color: var(--md-sys-color-on-surface);
    user-select: none;
  `;

  labelEl.appendChild(track);
  labelEl.appendChild(text);

  labelEl.addEventListener('click', () => {
    const newChecked = !checked;
    checked = newChecked;
    track.style.background = newChecked ? 'var(--md-sys-color-primary)' : 'var(--md-sys-color-surface-container-highest)';
    track.style.borderColor = newChecked ? 'var(--md-sys-color-primary)' : 'var(--md-sys-color-outline)';
    thumb.style.background = newChecked ? 'var(--md-sys-color-on-primary)' : 'var(--md-sys-color-on-surface)';
    thumb.style.left = newChecked ? '24px' : '2px';
    (labelEl as any)._checked = newChecked;
    onChange?.(newChecked);
  });

  (labelEl as any)._checked = checked;
  (labelEl as any).setChecked = (val: boolean) => {
    checked = val;
    track.style.background = val ? 'var(--md-sys-color-primary)' : 'var(--md-sys-color-surface-container-highest)';
    track.style.borderColor = val ? 'var(--md-sys-color-primary)' : 'var(--md-sys-color-outline)';
    thumb.style.background = val ? 'var(--md-sys-color-on-primary)' : 'var(--md-sys-color-on-surface)';
    thumb.style.left = val ? '24px' : '2px';
    (labelEl as any)._checked = val;
  };

  return labelEl;
}
