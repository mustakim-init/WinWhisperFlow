type CardVariant = 'elevated' | 'filled' | 'outlined';

export function createCard(
  content: HTMLElement | string,
  variant: CardVariant = 'elevated',
  options?: { padding?: string; style?: Partial<CSSStyleDeclaration> }
): HTMLDivElement {
  const card = document.createElement('div');

  const styles: Record<CardVariant, Partial<CSSStyleDeclaration>> = {
    elevated: {
      background: 'var(--md-sys-color-surface)',
      boxShadow: 'var(--md-sys-elevation-1)',
      border: 'none',
    },
    filled: {
      background: 'var(--md-sys-color-surface-variant)',
      boxShadow: 'none',
      border: 'none',
    },
    outlined: {
      background: 'transparent',
      boxShadow: 'none',
      border: '1px solid var(--md-sys-color-outline)',
    },
  };

  Object.assign(card.style, {
    borderRadius: '16px',
    padding: options?.padding ?? '24px',
    transition: 'box-shadow var(--md-sys-motion-duration-medium) var(--md-sys-motion-easing-standard), transform var(--md-sys-motion-duration-medium) var(--md-sys-motion-spring-gentle)',
    ...styles[variant],
    ...options?.style,
  });

  if (typeof content === 'string') {
    card.innerHTML = content;
  } else {
    card.appendChild(content);
  }

  const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (!prefersReducedMotion) {
    card.addEventListener('mouseenter', () => {
      if (variant === 'elevated') {
        card.style.boxShadow = 'var(--md-sys-elevation-2)';
      }
    });
    card.addEventListener('mouseleave', () => {
      if (variant === 'elevated') {
        card.style.boxShadow = 'var(--md-sys-elevation-1)';
      }
    });
  }

  return card;
}

export function createCardHeader(
  accentColor: string,
  title: string,
  actions?: HTMLElement
): HTMLDivElement {
  const header = document.createElement('div');
  header.style.cssText = `
    display: flex; align-items: center; gap: 12px;
    margin-bottom: 18px;
  `;

  const accent = document.createElement('div');
  accent.style.cssText = `
    width: 4px; height: 18px; border-radius: 2px;
    background: ${accentColor};
    flex-shrink: 0;
  `;

  const titleEl = document.createElement('span');
  titleEl.className = 'title-small';
  titleEl.style.flex = '1';
  titleEl.textContent = title;

  header.appendChild(accent);
  header.appendChild(titleEl);
  if (actions) header.appendChild(actions);

  return header;
}
