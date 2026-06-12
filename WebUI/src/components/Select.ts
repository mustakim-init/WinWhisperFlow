export interface SelectOption {
  label: string;
  value: string;
}

function highlightItem(items: HTMLDivElement[], currentValue: string): void {
  for (const item of items) {
    const value = (item as any)._optValue as string;
    item.style.background = value === currentValue
      ? 'var(--md-sys-color-primary-container)'
      : 'transparent';
  }
}

export function createSelect(
  options: SelectOption[],
  selectedValue: string,
  onChange?: (value: string) => void,
  label?: string
): HTMLDivElement {
  const container = document.createElement('div');
  container.style.cssText = 'position: relative; width: 100%;';

  if (label) {
    const lbl = document.createElement('div');
    lbl.className = 'label-medium';
    lbl.textContent = label;
    lbl.style.marginBottom = '4px';
    container.appendChild(lbl);
  }

  const trigger = document.createElement('button');
  const selected = options.find(o => o.value === selectedValue);
  trigger.textContent = selected?.label ?? options[0]?.label ?? '';
  trigger.style.cssText = `
    width: 100%; height: 40px; padding: 0 12px;
    border-radius: 4px; border: 1px solid var(--md-sys-color-outline);
    background: var(--md-sys-color-surface-container-highest);
    color: var(--md-sys-color-on-surface);
    font-size: 14px; text-align: left;
    cursor: pointer; display: flex; align-items: center; justify-content: space-between;
    transition: border-color var(--md-sys-motion-duration-short) var(--md-sys-motion-easing-standard);
  `;

  const arrow = document.createElement('span');
  arrow.innerHTML = '&#x25BE;';
  arrow.style.cssText = `font-size: 10px; color: var(--md-sys-color-on-surface-variant); transition: transform var(--md-sys-motion-duration-medium) var(--md-sys-motion-easing-standard);`;
  trigger.appendChild(arrow);

  const dropdown = document.createElement('div');
  dropdown.style.cssText = `
    position: absolute; top: 100%; left: 0; right: 0; z-index: 100;
    background: var(--md-sys-color-surface);
    border: 1px solid var(--md-sys-color-outline-variant);
    border-radius: 12px; margin-top: 4px;
    box-shadow: var(--md-sys-elevation-2); overflow: hidden;
    display: none; max-height: 200px; overflow-y: auto;
  `;

  let currentValue = selectedValue;
  const items: HTMLDivElement[] = [];

  for (const opt of options) {
    const item = document.createElement('div');
    (item as any)._optValue = opt.value;
    item.textContent = opt.label;
    item.style.cssText = `
      padding: 10px 12px; cursor: pointer; font-size: 14px;
      color: var(--md-sys-color-on-surface);
      transition: background var(--md-sys-motion-duration-short) var(--md-sys-motion-easing-standard);
      ${opt.value === currentValue ? 'background: var(--md-sys-color-primary-container);' : ''}
    `;
    item.addEventListener('mouseenter', () => {
      if (opt.value !== currentValue)
        item.style.background = 'var(--md-sys-color-surface-container-highest)';
    });
    item.addEventListener('mouseleave', () => {
      item.style.background = opt.value === currentValue
        ? 'var(--md-sys-color-primary-container)'
        : 'transparent';
    });
    item.addEventListener('click', () => {
      currentValue = opt.value;
      highlightItem(items, currentValue);
      trigger.textContent = opt.label;
      trigger.appendChild(arrow);
      dropdown.style.display = 'none';
      arrow.style.transform = 'rotate(0deg)';
      onChange?.(opt.value);
    });
    dropdown.appendChild(item);
    items.push(item);
  }

  trigger.addEventListener('click', () => {
    const isOpen = dropdown.style.display === 'block';
    dropdown.style.display = isOpen ? 'none' : 'block';
    arrow.style.transform = isOpen ? 'rotate(0deg)' : 'rotate(180deg)';
  });

  const onDocClick = (e: MouseEvent): void => {
    if (!(e.target instanceof Node) || !container.contains(e.target)) {
      dropdown.style.display = 'none';
      arrow.style.transform = 'rotate(0deg)';
    }
  };
  document.addEventListener('click', onDocClick);

  container.appendChild(trigger);
  container.appendChild(dropdown);

  (container as any)._cleanup = () => document.removeEventListener('click', onDocClick);
  (container as any).setValue = (val: string) => {
    const opt = options.find(o => o.value === val);
    if (!opt) return;
    currentValue = val;
    trigger.textContent = opt.label;
    trigger.appendChild(arrow);
    highlightItem(items, val);
  };

  return container;
}
