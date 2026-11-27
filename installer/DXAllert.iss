; Script Inno Setup 6 per DXAllert.
; Prima eseguire la pubblicazione (vedi build.ps1): i file da installare devono essere in ..\publish
; Risultato: ..\dist\DXAllert<versione>.exe (es. DXAllert1.1.0.exe)

#define AppName "DXAllert"
; build.ps1 passa la versione letta da DXAllert.App.csproj (/DAppVersion=...).
#ifndef AppVersion
  #define AppVersion "1.1.6"
#endif
#define AppExe "dxallert.exe"

[Setup]
AppId={{6B0E5A2C-4E1F-4D0B-9C55-D8A11E7A2F01}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=DXAllert
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Installazione per l'utente corrente: non servono i diritti di amministratore.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename={#AppName}{#AppVersion}
SetupIconFile=..\src\DXAllert.App\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Lo stesso mutex creato dal programma: l'installer si accorge se DXAllert Ã¨ in esecuzione.
AppMutex=DXAllert.SingleInstance
CloseApplications=yes

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; GroupDescription: "Collegamenti:"
Name: "autostart"; Description: "Avvia DXAllert con Windows (ridotto nell'area di notifica)"; GroupDescription: "Avvio:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Stessa voce che il programma gestisce da Impostazioni > "Avvia DXAllert con Windows".
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DXAllert"; ValueData: """{app}\{#AppExe}"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Avvia DXAllert"; Flags: nowait postinstall skipifsilent
; Aggiornamento automatico (DXAllert lancia l'installer con /SILENT /AUTOUPDATE=1): a fine installazione riapre il programma.
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: IsAutoUpdate

[UninstallDelete]
; I dati dell'utente (%AppData%\DXAllert: impostazioni, cache, log) NON vengono cancellati.
Type: filesandordirs; Name: "{app}"

[Code]
const
  WebView2Guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function IsAutoUpdate: Boolean;
begin
  Result := ExpandConstant('{param:AUTOUPDATE|0}') = '1';
end;

function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not WebView2Installed) and (not WizardSilent) then
    MsgBox('Per la scheda "Mappa" serve Microsoft Edge WebView2 Runtime, che su questo computer non risulta installato.' + #13#10 + #13#10 +
           'Si scarica gratis da: https://developer.microsoft.com/microsoft-edge/webview2/' + #13#10 +
           '(scegliere "Evergreen Bootstrapper"). Il resto del programma funziona comunque.', mbInformation, MB_OK);
end;
