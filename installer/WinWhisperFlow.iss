#define MyAppName "WinWhisper Flow"
// Must match <VersionPrefix> in WinWhisperFlow.csproj
#define MyAppVersion "0.1.0"
#define MyAppPublisher "WinWhisper Flow"
#define MyAppExeName "WinWhisperFlow.exe"

[Setup]
AppId={{CF67C6DB-FA9D-48D2-A3C2-FC94CB5E78A5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=WinWhisperFlowSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Icon.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\artifacts\publish\WinWhisperFlow\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "**\__pycache__\*;*.pyc"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Code]
const
  WebView2RegKey = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function IsWebView2Installed: Boolean;
var
  Version: string;
begin
  Result := RegQueryStringValue(HKLM, WebView2RegKey, 'pv', Version);
end;

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not IsWebView2Installed then
  begin
    if SuppressibleMsgBox('WinWhisper Flow requires the WebView2 Runtime.'#13#10#13#10'Click Yes to download and install it now (recommended), or No to skip.', mbConfirmation, MB_YESNO, IDYES) = IDYES then
    begin
      Exec('powershell.exe',
        '-Command "try { Invoke-WebRequest -Uri ''https://go.microsoft.com/fwlink/p/?LinkId=2124703'' -OutFile ''$env:TEMP\WebView2Setup.exe'' -UseBasicParsing; Start-Process ''$env:TEMP\WebView2Setup.exe'' -ArgumentList ''/silent /install'' -Wait } catch { exit 1 }"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if ResultCode <> 0 then
        MsgBox('Failed to install WebView2 Runtime. You can download it manually from:'#13#10'https://developer.microsoft.com/microsoft-edge/webview2/', mbError, MB_OK);
    end;
  end;
end;

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
