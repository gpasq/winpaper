; Inno Setup script for Paperboy (Windows ARM64). Built by build-installer.ps1, which passes
; AppVersion and PayloadDir on the command line.

#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\build\installer-payload"
#endif

[Setup]
AppId={{6B1E3F4A-2C7D-4E8B-9A51-5D0F2C8E7B93}
AppName=Paperboy
AppVersion={#AppVersion}
AppVerName=Paperboy {#AppVersion}
AppPublisher=Paperboy
VersionInfoVersion={#AppVersion}
; Per-user install into %LOCALAPPDATA%\Programs\Paperboy: no admin prompt.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
DefaultDirName={autopf}\Paperboy
DefaultGroupName=Paperboy
DisableProgramGroupPage=yes
DisableDirPage=auto
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
MinVersion=10.0.17763
OutputDir=..\build
OutputBaseFilename=Paperboy-{#AppVersion}-arm64-setup
SetupIconFile=..\Resources\AppIcon.ico
UninstallDisplayIcon={app}\Paperboy.exe
UninstallDisplayName=Paperboy
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Launch Paperboy when I sign in"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\Paperboy.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Paperboy"; Filename: "{app}\Paperboy.exe"

[Registry]
; Same Run entry the app's "Launch at login" toggle writes, so the two stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Paperboy"; ValueData: """{app}\Paperboy.exe"""; Tasks: startup
; On uninstall, remove the login item (however it was set) and the app's settings.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Paperboy"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Paperboy"; Flags: uninsdeletekey dontcreatekey

[Run]
Filename: "{app}\Paperboy.exe"; Description: "Start Paperboy"; Flags: nowait postinstall skipifsilent

[Code]
// Paperboy lives in the tray with no window to close, so stop it before replacing or removing it.
procedure StopPaperboy();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Paperboy.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopPaperboy();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopPaperboy();
  Result := True;
end;
