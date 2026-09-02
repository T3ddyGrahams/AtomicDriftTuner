#ifndef MyAppVersion
  #define MyAppVersion "0.7.3-beta.2"
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
VersionInfoVersion=0.7.3.2
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Atomic Drift Tuner beta installer

[Files]
Source: "{#StagingDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Atomic Drift Tuner"; Flags: nowait postinstall skipifsilent
