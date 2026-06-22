export function canonicalKeyFromEvent(event: KeyboardEvent): string | null {
  const code = event.code;
  if (!code) return null;
  switch (code) {
    case 'AltLeft':
      return 'Alt';
    case 'AltRight':
      return 'AltGr';
    case 'BracketLeft':
      return 'LeftBracket';
    case 'BracketRight':
      return 'RightBracket';
    case 'Semicolon':
      return 'SemiColon';
    case 'Backslash':
      return 'BackSlash';
    case 'Backquote':
      return 'BackQuote';
    case 'Period':
      return 'Dot';
    case 'Enter':
      return 'Return';
    case 'ArrowUp':
      return 'UpArrow';
    case 'ArrowDown':
      return 'DownArrow';
    case 'ArrowLeft':
      return 'LeftArrow';
    case 'ArrowRight':
      return 'RightArrow';
    default:
      if (
        /^(Meta|Control|Shift)(Left|Right)$/.test(code) ||
        /^Key[A-Z]$/.test(code) ||
        /^Digit[0-9]$/.test(code) ||
        /^F([1-9]|1[0-2])$/.test(code) ||
        ['Space', 'Tab', 'Backspace', 'Delete', 'Escape', 'Insert',
          'Home', 'End', 'PageUp', 'PageDown', 'CapsLock', 'Function',
          'Minus', 'Equal', 'Quote', 'Comma', 'Slash'].includes(code)
      ) {
        return code;
      }
      return null;
  }
}

export function defaultChordKeys(mode: 'push' | 'toggle'): string[] {
  // Match backend default: Ctrl+Alt+S (VK codes 0x11, 0x12, S)
  return mode === 'toggle'
    ? ['ControlRight', 'Alt', 'KeyS']
    : ['ControlRight', 'Alt'];
}

export function displayLabelForKey(name: string): string {
  switch (name) {
    case 'MetaLeft':
    case 'MetaRight':
      return 'Win';
    case 'Alt':
      return 'Alt';
    case 'AltGr':
      return 'AltGr';
    case 'ControlLeft':
    case 'ControlRight':
      return 'Ctrl';
    case 'ShiftLeft':
    case 'ShiftRight':
      return 'Shift';
    case 'CapsLock':
      return 'Caps';
    case 'Function':
      return 'Fn';
    case 'Space':
      return 'Space';
    case 'Tab':
      return 'Tab';
    case 'Return':
      return 'Enter';
    case 'Backspace':
      return 'Bksp';
    case 'Delete':
      return 'Del';
    case 'Escape':
      return 'Esc';
    case 'UpArrow':
      return '↑';
    case 'DownArrow':
      return '↓';
    case 'LeftArrow':
      return '←';
    case 'RightArrow':
      return '→';
  }
  if (/^Key([A-Z])$/.test(name)) return name.slice(3);
  if (/^Num([0-9])$/.test(name)) return name.slice(3);
  if (/^F([1-9]|1[0-2])$/.test(name)) return name;
  return name;
}

export function modifierSideHint(name: string): 'L' | 'R' | null {
  if (name === 'MetaRight' || name === 'AltGr' || name === 'ControlRight' || name === 'ShiftRight') {
    return 'R';
  }
  if (name === 'MetaLeft' || name === 'Alt' || name === 'ControlLeft' || name === 'ShiftLeft') {
    return 'L';
  }
  return null;
}

const SORT_ORDER: Record<string, number> = {
  ControlLeft: 0, ControlRight: 0,
  Alt: 1, AltGr: 1,
  ShiftLeft: 2, ShiftRight: 2,
  MetaLeft: 3, MetaRight: 3,
  Function: 4,
  CapsLock: 5,
};

export function sortChordKeys(keys: string[]): string[] {
  return [...keys].sort((a, b) => {
    const sa = SORT_ORDER[a] ?? 99;
    const sb = SORT_ORDER[b] ?? 99;
    if (sa !== sb) return sa - sb;
    return a.localeCompare(b);
  });
}
