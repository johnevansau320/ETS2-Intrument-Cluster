; Inno Setup script for the ETS2 Instrument Cluster. Build with build-installer.ps1 (needs Inno Setup 6).
; Installs for the current user only (no administrator rights).
#define AppName "ETS2 Instrument Cluster"
#define AppVer "1.3.1"

[Setup]
AppId={{C41D7E26-9B3A-4F58-A1D0-6E2B9F7C3A15}
AppName={#AppName}
AppVersion={#AppVer}
AppPublisher=ETS2 GPS
DefaultDirName={localappdata}\Programs\ETS2 Instrument Cluster
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\Ets2Cluster.exe
OutputDir=Output
OutputBaseFilename=Ets2ClusterSetup-{#AppVer}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter=Ets2Cluster.exe
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "autostart"; Description: "Start the instrument cluster when I log in to Windows"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\ETS2-Instrument-Cluster-Guide.pdf"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\ETS2 Instrument Cluster"; Filename: "{app}\Ets2Cluster.exe"; Comment: "ETS2 Instrument Cluster"
Name: "{autoprograms}\ETS2 Instrument Cluster user guide"; Filename: "{app}\ETS2-Instrument-Cluster-Guide.pdf"
Name: "{autodesktop}\ETS2 Instrument Cluster"; Filename: "{app}\Ets2Cluster.exe"; Tasks: desktopicon
Name: "{userstartup}\ETS2 Instrument Cluster"; Filename: "{app}\Ets2Cluster.exe"; Tasks: autostart

[Run]
Filename: "{app}\Ets2Cluster.exe"; Description: "Start the instrument cluster now"; Flags: nowait postinstall skipifsilent

[Code]
// The window position, size and options live in %APPDATA%\ETS2-Cluster; keep them unless the user says otherwise.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox('Also delete your saved window position and settings?' + #13#10 + ExpandConstant('{userappdata}\ETS2-Cluster'),
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\ETS2-Cluster'), True, True, True);
end;
