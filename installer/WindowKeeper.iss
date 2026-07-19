#ifndef MyAppVersion
  #define MyAppVersion "2.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish-sc"
#endif
#ifndef OutputDir
  #define OutputDir "..\installer-output"
#endif

#define MyAppName "WindowKeeper"
#define MyAppPublisher "naturian"
#define MyAppUrl "https://github.com/naturian/WindowKeeper"
#define MyAppExeName "WindowKeeper.exe"

[Setup]
AppId={{3A84AC99-6A2F-4F45-8527-91F04592074C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={autopf}\WindowKeeper
DefaultGroupName=WindowKeeper
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=WindowKeeper-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=WindowKeeper.exe
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=WindowKeeper Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "{#SourceDir}\WindowKeeper.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\WindowKeeper"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\Uninstall WindowKeeper"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-task"; \
  StatusMsg: "Registering WindowKeeper for logon..."; \
  Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-task"; \
  RunOnceId: "UnregisterWindowKeeperTask"; Flags: runhidden waituntilterminated

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  ExistingExe: String;
begin
  Result := '';
  ExistingExe := ExpandConstant('{app}\{#MyAppExeName}');
  if FileExists(ExistingExe) then
  begin
    if (not Exec(ExistingExe, '--unregister-task', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
    begin
      Result := 'The existing WindowKeeper instance could not be stopped. ' +
        'Close it manually and retry the installation.';
    end;
  end;
end;
