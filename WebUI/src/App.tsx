import React from 'react';
import { send } from './bridge/ipc';
import { useGlobalBridgeSync } from './hooks/useGlobalBridgeSync';
import { useStore } from './hooks/useStore';
import { useUiStore } from './stores/uiStore';
import { useDownloadStore } from './stores/downloadStore';
import { setSetupError } from './lib/store';
import { Sidebar } from './components/Sidebar';
import { Toaster } from './components/Toaster';
import { ReadinessPage } from './pages/ReadinessPage';
import { DictatePage } from './pages/DictatePage';
import { CapturesPage } from './pages/CapturesPage';
import { ModelsPage } from './pages/ModelsPage';
import { PhoneMicPage } from './pages/PhoneMicPage';
import { SettingsLayout } from './pages/Settings/SettingsLayout';

export default function App() {
  useGlobalBridgeSync();

  const legacy = useStore();
  const page = useUiStore((s) => s.page);
  const setPage = useUiStore((s) => s.setPage);
  const statusText = useUiStore((s) => s.statusText);
  const statusVariant = useUiStore((s) => s.statusVariant);
  const isReady = useUiStore((s) => s.isReady);

  const showReadiness = !isReady;

  const handleRetry = () => {
    setSetupError(undefined);
    send({ type: 'setup_runtime' });
  };

  const handleContinue = () => {
    useUiStore.getState().setIsReady(true);
    setSetupError(undefined);
    setPage('models');
  };

  const renderPage = () => {
    switch (page) {
      case 'dictate': return <DictatePage />;
      case 'captures': return <CapturesPage />;
      case 'phone-mic': return <PhoneMicPage />;
      case 'models': return <ModelsPage />;
      case 'settings': return <SettingsLayout />;
      default: return <DictatePage />;
    }
  };

  return (
    <>
      {showReadiness && (
        <ReadinessPage
          steps={legacy.setupSteps}
          overall={legacy.setupOverall}
          error={legacy.setupError}
          statusText={statusText}
          onRetry={handleRetry}
          onContinue={handleContinue}
        />
      )}

      <div className="h-screen bg-background flex flex-col overflow-hidden">
        {isReady && (
          <Sidebar
            active={page}
            onChange={setPage}
            ready={isReady}
            statusText={statusText}
            statusVariant={statusVariant}
          />
        )}
        <main className={`flex-1 overflow-hidden flex flex-col ${isReady ? 'ml-20' : ''}`}>
          {isReady && (
            <div className="flex-1 container mx-auto px-8 max-w-[1800px] h-full overflow-hidden flex flex-col">
              {renderPage()}
            </div>
          )}
        </main>
      </div>
      <Toaster />
    </>
  );
}
