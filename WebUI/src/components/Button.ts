type ButtonVariant = 'filled' | 'tonal' | 'outlined' | 'text';

export function createButton(
  label: string,
  variant: ButtonVariant = 'filled',
  onClick?: () => void,
  options?: { disabled?: boolean; style?: Partial<CSSStyleDeclaration> }
): HTMLButtonElement {
  const btn = document.createElement('button');
  const isDisabled = options?.disabled ?? false;
  btn.disabled = isDisabled;
  btn.textContent = label;

  const base: Partial<CSSStyleDeclaration> = {
    height: '40px',
    padding: variant === 'text' ? '0 12px' : '0 24px',
    borderRadius: '20px',
    fontSize: '14px',
    fontWeight: '600',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '8px',
    transition: 'background var(--md-sys-motion-duration-short) var(--md-sys-motion-easing-standard), box-shadow var(--md-sys-motion-duration-short) var(--md-sys-motion-easing-standard), transform var(--md-sys-motion-duration-short) var(--md-sys-motion-spring-bouncy), border-color var(--md-sys-motion-duration-short) var(--md-sys-motion-easing-standard)',
    cursor: isDisabled ? 'default' : 'pointer',
    whiteSpace: 'nowrap',
  };

  const variants: Record<ButtonVariant, Partial<CSSStyleDeclaration>> = {
    filled: {
      background: 'var(--md-sys-color-primary)',
      color: 'var(--md-sys-color-on-primary)',
      boxShadow: 'var(--md-sys-elevation-1)',
      border: 'none',
    },
    tonal: {
      background: 'var(--md-sys-color-primary-container)',
      color: 'var(--md-sys-color-on-primary-container)',
      border: 'none',
    },
    outlined: {
      background: 'transparent',
      color: 'var(--md-sys-color-primary)',
      border: '1px solid var(--md-sys-color-outline)',
    },
    text: {
      background: 'transparent',
      color: 'var(--md-sys-color-primary)',
      border: 'none',
    },
  };

  Object.assign(btn.style, base, variants[variant], options?.style);

  if (isDisabled) {
    btn.style.opacity = '0.38';
    btn.style.boxShadow = 'none';
    btn.style.pointerEvents = 'none';
    return btn;
  }

  btn.addEventListener('click', () => onClick?.());

  btn.addEventListener('mousedown', () => {
    btn.style.transform = 'scale(0.96)';
  });
  btn.addEventListener('mouseup', () => {
    btn.style.transform = 'scale(1)';
  });
  btn.addEventListener('mouseleave', () => {
    btn.style.transform = 'scale(1)';
  });

  btn.addEventListener('mouseenter', () => {
    if (variant === 'filled') {
      btn.style.boxShadow = 'var(--md-sys-elevation-2)';
    }
    if (variant === 'outlined') {
      btn.style.borderColor = 'var(--md-sys-color-primary)';
    }
  });
  btn.addEventListener('mouseleave', () => {
    if (variant === 'filled') {
      btn.style.boxShadow = 'var(--md-sys-elevation-1)';
    }
    if (variant === 'outlined') {
      btn.style.borderColor = 'var(--md-sys-color-outline)';
    }
  });

  return btn;
}
