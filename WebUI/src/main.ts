import './index.css';
import { setup } from './bridge/ipc';
import { buildApp } from './App';

setup();

// Apply OS dark mode preference as initial fallback (overridden by C# init message)
if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
  document.documentElement.setAttribute('data-theme', 'dark');
}

const root = document.getElementById('app');
if (root) buildApp(root);
