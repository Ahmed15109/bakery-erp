; Inno Setup Script for Bakery ERP
; Publisher: Ahmed Abdelmonem
; Application Version: 1.0.0

#define MyAppName "Bakery ERP"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Ahmed Abdelmonem"
#define MyAppExeName "Bakery.WPF.exe"

[Setup]
AppId={{A84F2B76-3D1E-4C90-9E11-2B47890123AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Bakery ERP
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=BakeryERP_Setup_v1.0
OutputDir=.
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
SetupIconFile=BakeryERP.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
AppMutex=BakeryERP.A84F2B76-3D1E-4C90-9E11-2B47890123AB
SetupMutex=BakeryERP.Setup.A84F2B76-3D1E-4C90-9E11-2B47890123AB
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
Uninstallable=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,*.log,*.bak,*.tmp,appsettings.json,appsettings.user.json"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: postinstall nowait skipifsilent

[Code]
const
  LocalDbRegistryKey = 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions';
  LocalDbDownloadUrl = 'https://www.microsoft.com/download/details.aspx?id=104781';

function IsLocalDbInstalled(): Boolean;
var
  InstalledVersions: TArrayOfString;
begin
  Result := RegGetSubkeyNames(HKLM64, LocalDbRegistryKey, InstalledVersions) and
    (GetArrayLength(InstalledVersions) > 0);

  { Registry detection is authoritative. File checks cover unusual but valid installs. }
  if not Result then
    Result :=
      FileExists(ExpandConstant('{pf64}\Microsoft SQL Server\170\Tools\Binn\SqlLocalDB.exe')) or
      FileExists(ExpandConstant('{pf64}\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe')) or
      FileExists(ExpandConstant('{pf64}\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe'));
end;

function InitializeSetup(): Boolean;
var
  OpenDownloadPage: Integer;
begin
  Result := IsLocalDbInstalled();
  if Result then
    Exit;

  OpenDownloadPage := MsgBox(
    'لا يمكن تثبيت نظام المخبز لأن قاعدة البيانات المطلوبة غير موجودة.' + #13#10 + #13#10 +
    'المطلوب: Microsoft SQL Server Express LocalDB (64-bit)، إصدار 2019 أو أحدث.' + #13#10 +
    '1. حمّل SQL Server 2022 Express من موقع Microsoft الرسمي.' + #13#10 +
    '2. اختر حزمة LocalDB وثبّتها.' + #13#10 +
    '3. أعد تشغيل ملف تثبيت نظام المخبز.' + #13#10 + #13#10 +
    'لن يكتمل التثبيت قبل توفير قاعدة البيانات. هل تريد فتح صفحة التنزيل الآن؟',
    mbCriticalError,
    MB_YESNO);
  if OpenDownloadPage = IDYES then
    ShellExec('open', LocalDbDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, OpenDownloadPage);

  Result := False;
end;

function InitializeUninstall(): Boolean;
var
  LegacyConfiguration: String;
  PreservedConfigurationDirectory: String;
  PreservedConfiguration: String;
begin
  Result := MsgBox(
    'سيتم حذف ملفات برنامج نظام المخبز فقط.' + #13#10 + #13#10 +
    'ستبقى قاعدة البيانات والنسخ الاحتياطية والمرفقات والإعدادات داخل مجلد بيانات المستخدم ولن يتم حذفها.' + #13#10 + #13#10 +
    'هل تريد متابعة إزالة البرنامج؟',
    mbConfirmation,
    MB_YESNO) = IDYES;

  if not Result then
    Exit;

  { Older installers registered appsettings.json for removal. Preserve any
    customer-edited legacy file outside the application directory first. }
  LegacyConfiguration := ExpandConstant('{app}\appsettings.json');
  if not FileExists(LegacyConfiguration) then
    Exit;

  PreservedConfigurationDirectory := ExpandConstant('{localappdata}\BakeryERP');
  PreservedConfiguration := AddBackslash(PreservedConfigurationDirectory) +
    'appsettings.legacy-uninstall.json';

  if not ForceDirectories(PreservedConfigurationDirectory) or
     not CopyFile(LegacyConfiguration, PreservedConfiguration, False) then
  begin
    MsgBox(
      'تعذر حفظ نسخة من إعدادات العميل القديمة خارج مجلد البرنامج. لن تتم إزالة البرنامج حتى لا تضيع الإعدادات.',
      mbCriticalError,
      MB_OK);
    Result := False;
  end;
end;
