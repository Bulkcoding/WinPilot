#define AppName      "WinPilot"
#define AppPublisher "Bulkcoding"
#define AppURL       "https://github.com/Bulkcoding/WinPilot"
#define AppExeName   "WinPilot.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputBaseFilename=WinPilotSetup
OutputDir=installer_output
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
; 앱 자체가 관리자 권한 필요 (manifest)
PrivilegesRequiredOverridesAllowed=dialog
; 아이콘 설정
SetupIconFile=Resources\icon.ico
UninstallDisplayIcon={app}\WinPilot.exe
; 64-bit 전용
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 폴더 배포: WPF 네이티브 DLL(wpfgfx_cor3.dll 등)을 모두 포함
Source: "publish_installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";        Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
