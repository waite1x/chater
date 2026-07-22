#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\bin\Release\net10.0\win-x64\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

#ifndef OutputBaseName
  #define OutputBaseName "chater-setup"
#endif

#ifndef SetupIcon
  #define SetupIcon "..\Assets\chater.ico"
#endif

#define MyAppName "Chater"
#define MyAppPublisher "Chater"
#define MyAppExeName "Chater.exe"

[Setup]
AppId={{B7B7C5E4-3D4A-4C6B-9D2C-202607260001}}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Chater
DefaultGroupName=Chater
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
SetupIconFile={#SetupIcon}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Chater
CloseApplications=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Icons]
Name: "{autoprograms}\Chater"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Chater"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Chater"; Flags: nowait postinstall skipifsilent
