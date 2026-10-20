#ifndef AppVersion
 #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
 #define PublishDir "..\dist\Moa-win-x64"
#endif
#ifndef OutputDir
 #define OutputDir "..\dist"
#endif
[Setup]
AppId={{D9412A14-E5D0-4D81-A374-D75D6BCE1ABA}
AppName=모아
AppVersion={#AppVersion}
AppVerName=모아 {#AppVersion}
AppPublisher=SCOTT0318
AppPublisherURL=https://github.com/SCOTT0318/moa-calendar
AppSupportURL=https://github.com/SCOTT0318/moa-calendar/issues
DefaultDirName={localappdata}\Programs\Moa
DefaultGroupName=모아
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=Moa-Setup
SetupIconFile=..\Assets\moa.ico
UninstallDisplayIcon={app}\Gather.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
CloseApplications=no
RestartApplications=no
CreateUninstallRegKey=not IsTest
Uninstallable=yes
AppMutex=Local\Moa.Setup.Running
[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
[Tasks]
Name: "desktopicon"; Description: "바탕화면 바로가기 만들기"; Flags: checkedonce
Name: "startup"; Description: "Windows 로그인 시 트레이에서 자동 실행"; Flags: unchecked
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,Data\*,TestData\*"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\모아\모아"; Filename: "{app}\Gather.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Gather.exe"; AppUserModelID: "SCOTT0318.Moa.Desktop"; Check: not IsTest
Name: "{userdesktop}\모아"; Filename: "{app}\Gather.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Gather.exe"; AppUserModelID: "SCOTT0318.Moa.Desktop"; Tasks: desktopicon; Check: not IsTest
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Moa"; ValueData: """{app}\Gather.exe"" --startup"; Flags: uninsdeletevalue; Tasks: startup; Check: not IsTest
[Run]
Filename: "{app}\Gather.exe"; Description: "모아 실행"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent; Check: not IsTest
[Code]
function IsTest: Boolean;
begin Result := ExpandConstant('{param:SMOKETEST|0}') = '1'; end;
{ User data is stored in LocalAppData\Moa, outside the install directory.
  Installation, upgrades and uninstall never remove it. }
