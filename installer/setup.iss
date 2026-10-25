; Instalador do Transcritor de Reunião (Inno Setup 6.3+).
; Pré-requisito: rodar scripts\publish.ps1 antes, para gerar publish\TranscritorDeReuniao.
; Compilar: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
; Instalar o Inno Setup: winget install JRSoftware.InnoSetup

#define AppName "Transcritor de Reunião"
#define AppId "TranscritorDeReuniao"
#define AppVersion "0.2.0"
#define AppExe "TranscritorDeReuniao.exe"
#define SourceDir "..\publish\TranscritorDeReuniao"

[Setup]
AppId={{9C1F5E62-3B7A-4E0B-9A4D-7D2F3C5E8A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=AlissonTexeiraDev
AppPublisherURL=https://github.com/AlissonTexeiraDev/TranscritorDeReuniao
DefaultDirName={localappdata}\Programs\{#AppId}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename={#AppId}-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "startup"; Description: "Iniciar com o Windows (minimizado na bandeja)"; GroupDescription: "Opções:"
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Opções:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppId}"; ValueData: """{app}\{#AppExe}"" --minimized"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName} agora"; Flags: nowait postinstall skipifsilent
