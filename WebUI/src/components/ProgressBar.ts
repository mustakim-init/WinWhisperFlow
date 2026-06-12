export function createProgressBar(value: number, wavy = true): HTMLDivElement {
  const container = document.createElement('div');
  container.style.cssText = `
    width: 100%; height: 4px; border-radius: 2px;
    background: var(--md-sys-color-surface-container-highest);
    position: relative; overflow: hidden;
  `;

  const fill = document.createElement('div');
  fill.style.cssText = `
    height: 100%; border-radius: 2px;
    background: var(--md-sys-color-primary);
    width: ${Math.min(100, Math.max(0, value * 100))}%;
    transition: width var(--md-sys-motion-duration-long) var(--md-sys-motion-easing-emphasized);
  `;

  if (wavy) {
    // wavy overlay via gradient animation
    const wave = document.createElement('div');
    wave.style.cssText = `
      position: absolute; top: 0; left: 0; right: 0; bottom: 0;
      background: repeating-linear-gradient(
        -45deg,
        transparent,
        transparent 4px,
        rgba(255,255,255,0.3) 4px,
        rgba(255,255,255,0.3) 8px
      );
      background-size: 12px 100%;
      animation: wave-shift 0.8s linear infinite;
      pointer-events: none;
    `;

    // Add keyframes if not already added
    if (!document.getElementById('m3-wave-keyframes')) {
      const style = document.createElement('style');
      style.id = 'm3-wave-keyframes';
      style.textContent = `
        @keyframes wave-shift {
          0% { background-position: 0 0; }
          100% { background-position: 12px 0; }
        }
      `;
      document.head.appendChild(style);
    }

    container.appendChild(fill);
    container.appendChild(wave);
  } else {
    container.appendChild(fill);
  }

  return container;
}
