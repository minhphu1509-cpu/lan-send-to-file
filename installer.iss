#define AppName "Gửi File LAN"
#define AppVersion "1.1.0"
#define AppPublisher "PhuDong"
#define AppExeName "GuiFileLAN.exe"

[Setup]
AppId={{A57493C2-4389-42A9-9B47-8F835AC06D01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\GuiFileLAN
DefaultGroupName={#AppName}
OutputDir=artifacts
OutputBaseFilename=GuiFileLAN-Setup-win-x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=Assets\app.ico

[Files]
Source: "artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Tạo lối tắt ngoài màn hình nền"; GroupDescription: "Lối tắt bổ sung:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Mở {#AppName}"; Flags: postinstall nowait skipifsilent
