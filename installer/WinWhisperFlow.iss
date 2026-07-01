#define MyAppName      "WinWhisper Flow"
; Must match <VersionPrefix> in WinWhisperFlow.csproj (patched by CI via regex before compilation)
#define MyAppVersion   "0.1.0"
#define MyAppPublisher "WinWhisper Flow"
#define MyAppExeName   "WinWhisperFlow.exe"
#define MyAppRegKey    "Software\WinWhisperFlow"

[Setup]
AppId={{CF67C6DB-FA9D-48D2-A3C2-FC94CB5E78A5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

; Allow the user to choose the install directory (dir page is shown by default)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Allow changing install dir even on upgrade
DirExistsWarning=no

; Write uninstall info to HKCU so no elevation is needed for per-user installs
; For machine-wide (Program Files) installs, the installer will self-elevate via RequestedExecutionLevel
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=..\artifacts\installer
OutputBaseFilename=WinWhisperFlowSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Minimum Windows 10 1903 (required for the app itself)
MinVersion=10.0.18362

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";  Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupentry"; Description: "Start WinWhisper Flow with Windows";  GroupDescription: "Startup:"

[Files]
Source: "..\artifacts\publish\WinWhisperFlow\*"; \
  DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs; \
  Excludes: "**\__pycache__\*;*.pyc"

[Registry]
; Write the install dir so UpdateService can find it without guessing from Assembly.Location.
; Written to HKCU for per-user installs, HKLM for machine-wide installs.
Root: HKCU; Subkey: "{#MyAppRegKey}"; ValueType: string; ValueName: "InstallDir"; \
  ValueData: "{app}"; Flags: uninsdeletekey; Check: not IsAdminInstallMode
Root: HKLM; Subkey: "{#MyAppRegKey}"; ValueType: string; ValueName: "InstallDir"; \
  ValueData: "{app}"; Flags: uninsdeletekey; Check: IsAdminInstallMode

; Start with Windows (via HKCU Run key — no elevation needed, auto-cleaned on uninstall)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "WinWhisperFlow"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startupentry

[Icons]
Name: "{group}\{#MyAppName}";    Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Launch after install
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[Code]
// ── WebView2 check ───────────────────────────────────────────────────────
const
  WebView2RegKey = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function IsWebView2Installed: Boolean;
var
  Version: string;
begin
  Result := RegQueryStringValue(HKLM, WebView2RegKey, 'pv', Version)
         or RegQueryStringValue(HKCU, WebView2RegKey, 'pv', Version);
end;

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not IsWebView2Installed then
  begin
    if SuppressibleMsgBox(
         'WinWhisper Flow requires the Microsoft Edge WebView2 Runtime.' + #13#10 + #13#10 +
         'Click Yes to download and install it now (recommended), or No to skip.',
         mbConfirmation, MB_YESNO, IDYES) = IDYES then
    begin
      Exec('powershell.exe',
        '-NoProfile -Command "try {' +
          ' Invoke-WebRequest -Uri ''https://go.microsoft.com/fwlink/p/?LinkId=2124703''' +
          ' -OutFile ''$env:TEMP\WebView2Setup.exe'' -UseBasicParsing;' +
          ' Start-Process ''$env:TEMP\WebView2Setup.exe'' -ArgumentList ''/silent /install'' -Wait' +
          ' } catch { exit 1 }"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if ResultCode <> 0 then
        MsgBox(
          'WebView2 Runtime installation failed. You can install it manually from:' + #13#10 +
          'https://developer.microsoft.com/microsoft-edge/webview2/',
          mbError, MB_OK);
    end;
  end;
end;

// ── Upgrade: gracefully stop the running app before overwriting files ─────
function InitializeUninstall: Boolean;
begin
  Result := True;
end;

procedure KillRunningApp;
var
  ResultCode: Integer;
begin
  // Ask the running instance to close via taskkill /IM; ignore errors
  Exec('taskkill.exe', '/IM WinWhisperFlow.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  KillRunningApp;
end;
