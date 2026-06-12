export function animateScale(
  el: HTMLElement,
  from: number,
  to: number,
  duration = 300
): void {
  el.animate(
    [
      { transform: `scale(${from})` },
      { transform: `scale(${to})` },
    ],
    {
      duration,
      easing: 'cubic-bezier(0.34, 1.56, 0.64, 1)', // bouncy spring
      fill: 'forwards',
    }
  );
}

export function animateSlideIn(
  el: HTMLElement,
  direction: 'up' | 'down' | 'left' | 'right' = 'up',
  duration = 400
): void {
  const offset = direction === 'up' || direction === 'down' ? 'translateY' : 'translateX';
  const fromVal = direction === 'up' || direction === 'left' ? '30px' : '-30px';
  el.animate(
    [
      { transform: `${offset}(${fromVal})`, opacity: 0 },
      { transform: `${offset}(0px)`, opacity: 1 },
    ],
    {
      duration,
      easing: 'cubic-bezier(0.175, 0.885, 0.32, 1.275)',
      fill: 'forwards',
    }
  );
}

export function animateMorph(
  el: HTMLElement,
  fromRadius: string,
  toRadius: string,
  duration = 200
): void {
  el.animate(
    [
      { borderRadius: fromRadius },
      { borderRadius: toRadius },
    ],
    {
      duration,
      easing: 'cubic-bezier(0.2, 0, 0, 1.0)',
      fill: 'forwards',
    }
  );
}

export function springIn(el: HTMLElement): void {
  el.animate(
    [
      { opacity: 0, transform: 'scale(0.8) translateY(10px)' },
      { opacity: 1, transform: 'scale(1) translateY(0)' },
    ],
    {
      duration: 400,
      easing: 'cubic-bezier(0.34, 1.56, 0.64, 1)',
      fill: 'forwards',
    }
  );
}
