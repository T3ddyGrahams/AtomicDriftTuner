#ifndef MyAppVersion
  #error MyAppVersion must be provided by build-beta-package.ps1
#endif

#ifndef MyVersionInfoVersion
  #error MyVersionInfoVersion must be provided by build-beta-package.ps1
#endif

#ifndef RepoRoot
  #error RepoRoot must be provided by build-beta-package.ps1
#endif

#define MyAppName "Atomic Drift Tuner"
#define MyAppExeName "AtomicDriftTuner.exe"
#define StagingDir RepoRoot + "\artifacts\staging"
#define ReleaseDir RepoRoot + "\artifacts\release"

[Setup]
AppId={{5EAC35E3-6D44-4C4E-B476-80F3A063B001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}

DefaultDirName={localappdata}\Programs\AtomicDriftTuner
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

OutputDir={#ReleaseDir}
OutputBaseFilename=AtomicDriftTuner-{#MyAppVersion}-setup

Compression=lzma2
SolidCompression=yes
WizardStyle=modern

PrivilegesRequired=lowest

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile={#RepoRoot}\src\AtomicDriftTuner\Assets\ADT.ico

VersionInfoVersion={#MyVersionInfoVersion}
VersionInfoTextVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Atomic Drift Tuner installer
VersionInfoProductVersion={#MyVersionInfoVersion}
VersionInfoProductTextVersion={#MyAppVersion}

CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#StagingDir}\AtomicDriftTuner.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\aspnetcorev2_inprocess.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\D3DCompiler_47_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\PenImc_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\PresentationNative_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\vcruntime140_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\wpfgfx_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\README-BETA-TESTERS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\BridgePayload\AtomicDriftTuner.SimHubBridge.dll"; DestDir: "{app}\BridgePayload"; Flags: ignoreversion
Source: "{#StagingDir}\docs\GUIDED_WORKFLOW.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#StagingDir}\docs\TOUCHSCREEN.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#StagingDir}\docs\GEARING.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#StagingDir}\docs\TELEMETRY_INTELLIGENCE.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#StagingDir}\docs\PIT_SETUP.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#StagingDir}\docs\PITHOUSE.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#StagingDir}\docs\CAR_PHYSICS.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#StagingDir}\ADTCompanion-ContentManager.zip"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\CompanionPayload\README.md"; DestDir: "{app}\CompanionPayload"; Flags: ignoreversion
Source: "{#StagingDir}\CompanionPayload\apps\lua\ADTCompanion\ADTCompanion.lua"; DestDir: "{app}\CompanionPayload\apps\lua\ADTCompanion"; Flags: ignoreversion
Source: "{#StagingDir}\CompanionPayload\apps\lua\ADTCompanion\companion_client.lua"; DestDir: "{app}\CompanionPayload\apps\lua\ADTCompanion"; Flags: ignoreversion
Source: "{#StagingDir}\CompanionPayload\apps\lua\ADTCompanion\setup_capture.lua"; DestDir: "{app}\CompanionPayload\apps\lua\ADTCompanion"; Flags: ignoreversion
Source: "{#StagingDir}\CompanionPayload\apps\lua\ADTCompanion\pit_setup.lua"; DestDir: "{app}\CompanionPayload\apps\lua\ADTCompanion"; Flags: ignoreversion
Source: "{#StagingDir}\CompanionPayload\apps\lua\ADTCompanion\manifest.ini"; DestDir: "{app}\CompanionPayload\apps\lua\ADTCompanion"; Flags: ignoreversion
Source: "{#StagingDir}\CompanionPayload\apps\lua\ADTCompanion\icon.png"; DestDir: "{app}\CompanionPayload\apps\lua\ADTCompanion"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Atomic Drift Tuner"; Flags: nowait postinstall skipifsilent
