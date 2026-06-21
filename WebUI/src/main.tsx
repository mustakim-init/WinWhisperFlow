import React from 'react';
import ReactDOM from 'react-dom/client';
import './index.css';
import { setup } from './bridge/ipc';
import App from './App';

setup();

ReactDOM.createRoot(document.getElementById('app')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
