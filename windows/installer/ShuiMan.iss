#if Ver < EncodeVer(6,7,0)
  #error Inno Setup 6.7 or newer is required.
#endif
#ifndef AppVersion
  #define AppVersion "0.7.1"
#endif
#ifndef PayloadDir
  #error Pass /DPayloadDir pointing to the self-contained publish folder.
#endif
#ifndef OutputDir
  #define OutputDir "..\..\build\windows"
#endif
#ifdef TestMode
  #define AppIdentity "ShuiMan.Installer.NativeSmoke.8BB2BDE2"
  #define ProductName "水漫 · 安装器测试"
  #define ShortcutName "ShuiMan Native Installer Test"
  #define BookProgId "ShuiMan.NativeInstallerTest.Book"
  #define OutputName "ShuiMan-Installer-Test-" + AppVersion + "-x64"
  #define DefaultDirectory TestInstallDir
  #define LaunchArgs '--data-dir ""' + TestInstallDir + '\test-library""'
#else
  #define AppIdentity "ShuiMan.Windows.291790F9-329C-44A8-A6C5-83D596049CF4"
  #define ProductName "水漫"
  #define ShortcutName "ShuiMan"
  #define BookProgId "ShuiMan.Book"
  #define OutputName "ShuiMan-Setup-" + AppVersion + "-x64"
  #define DefaultDirectory "{localappdata}\Programs\ShuiMan"
  #define LaunchArgs ""
#endif

[Setup]
AppId={#AppIdentity}
AppName={#ProductName}
AppVersion={#AppVersion}
AppVerName={#ProductName} {#AppVersion}
AppPublisher=ShuiMan Project
AppPublisherURL=https://github.com/dashuipaoyihuier/ShuiMan-
AppSupportURL=https://github.com/dashuipaoyihuier/ShuiMan-/issues
AppUpdatesURL=https://github.com/dashuipaoyihuier/ShuiMan-/releases
VersionInfoDescription=水漫 Windows 安装程序
VersionInfoProductName=ShuiMan
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={#DefaultDirectory}
DefaultGroupName={#ShortcutName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile=..\ShuiMan.Windows\AppIcon.ico
UninstallDisplayIcon={app}\ShuiMan.exe
UninstallDisplayName={#ProductName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern light windows11 hidebevels
WizardSizePercent=115
DisableWelcomePage=no
DisableDirPage=no
DisableReadyPage=yes
ShowLanguageDialog=no
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
SetupMutex={#AppIdentity}.Setup
UninstallLogMode=append
UsePreviousAppDir=yes
UsePreviousTasks=yes

[Languages]
Name: "zhcn"; MessagesFile: "Languages\ChineseSimplified.isl"

[Messages]
WelcomeLabel1=让阅读，安静下来。
WelcomeLabel2=水漫 · Windows {#AppVersion}%n%n一个属于漫画的私人书库。%nZIP / CBZ、图片、PDF 与 EPUB，轻松收藏，接着上次阅读。%n%n此安装包已包含 .NET 运行环境。安装到当前用户，升级时保留您的书库与阅读进度。
FinishedHeadingLabel=水漫，已为你准备好。
FinishedLabel=现在可以从开始菜单打开水漫。%n%n把漫画拖入书库，就从这一页开始。%n升级和卸载均保留原始漫画与阅读记录。
SelectDirDesc=选择水漫的安装位置。
SelectTasksDesc=按你的习惯，完成最后一点设置。
SelectTasksLabel2=以下选项均可按需选择。水漫自带运行环境，可以直接离线阅读。
ButtonNext=继续(&N)
ButtonBack=返回(&B)
ButtonInstall=安装水漫(&I)

[Tasks]
Name: "desktopicon"; Description: "在桌面创建水漫快捷方式"; GroupDescription: "快捷方式"; Flags: unchecked
Name: "fileassociations"; Description: "在 ZIP / CBZ / EPUB / PDF / MOBI 的“打开方式”中添加水漫"; GroupDescription: "文件打开"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "Install.cmd,install.ps1,uninstall.ps1,使用说明.txt,*.pdb,Microsoft.Web.WebView2.*,WebView2Loader.dll"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE-Inno-Setup.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

[InstallDelete]
; Remove only the browser SDK files distributed by 0.6; shared runtimes are untouched.
Type: files; Name: "{app}\Microsoft.Web.WebView2.Core.dll"
Type: files; Name: "{app}\Microsoft.Web.WebView2.Core.xml"
Type: files; Name: "{app}\Microsoft.Web.WebView2.Wpf.dll"
Type: files; Name: "{app}\Microsoft.Web.WebView2.Wpf.xml"
Type: files; Name: "{app}\Microsoft.Web.WebView2.WinForms.dll"
Type: files; Name: "{app}\Microsoft.Web.WebView2.WinForms.xml"
Type: files; Name: "{app}\WebView2Loader.dll"
Type: files; Name: "{app}\runtimes\win-x64\native\WebView2Loader.dll"
; Only the obsolete 0.6 script helpers; never delete whole directories or library data.
Type: files; Name: "{app}\Install.cmd"; Check: HasLegacyInstallMarker
Type: files; Name: "{app}\install.ps1"; Check: HasLegacyInstallMarker
Type: files; Name: "{app}\uninstall.ps1"; Check: HasLegacyInstallMarker

[Icons]
Name: "{userprograms}\{#ShortcutName}"; Filename: "{app}\ShuiMan.exe"; Parameters: "{#LaunchArgs}"; WorkingDir: "{app}"; Comment: "水漫 · 你的私人漫画书库"
Name: "{userdesktop}\{#ShortcutName}"; Filename: "{app}\ShuiMan.exe"; Parameters: "{#LaunchArgs}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\{#BookProgId}"; ValueType: string; ValueName: ""; ValueData: "水漫漫画"; Flags: uninsdeletekey; Tasks: fileassociations
Root: HKCU; Subkey: "Software\Classes\{#BookProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\ShuiMan.exe,0"; Tasks: fileassociations
Root: HKCU; Subkey: "Software\Classes\{#BookProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ShuiMan.exe"" {#LaunchArgs} ""%1"""; Tasks: fileassociations
#ifdef TestMode
Root: HKCU; Subkey: "Software\Classes\.shuiman-native-install-test\OpenWithProgids"; ValueType: string; ValueName: "{#BookProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: fileassociations
#else
Root: HKCU; Subkey: "Software\Classes\.zip\OpenWithProgids"; ValueType: string; ValueName: "{#BookProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: fileassociations
Root: HKCU; Subkey: "Software\Classes\.cbz\OpenWithProgids"; ValueType: string; ValueName: "{#BookProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: fileassociations
Root: HKCU; Subkey: "Software\Classes\.epub\OpenWithProgids"; ValueType: string; ValueName: "{#BookProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: fileassociations
Root: HKCU; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "{#BookProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: fileassociations
Root: HKCU; Subkey: "Software\Classes\.mobi\OpenWithProgids"; ValueType: string; ValueName: "{#BookProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: fileassociations
#endif

[Run]
Filename: "{app}\ShuiMan.exe"; Parameters: "{#LaunchArgs}"; Description: "打开水漫"; Flags: nowait postinstall skipifsilent

[Code]
function HasLegacyInstallMarker: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\.shuiman-install'));
end;

procedure InitializeWizard;
begin
  WizardForm.Font.Name := 'Microsoft YaHei UI';
  WizardForm.WelcomeLabel1.Font.Name := 'Microsoft YaHei UI';
  WizardForm.WelcomeLabel1.Font.Size := 22;
  WizardForm.FinishedHeadingLabel.Font.Name := 'Microsoft YaHei UI';
  WizardForm.FinishedHeadingLabel.Font.Size := 20;
  WizardForm.WizardBitmapImage.Visible := False;
  WizardForm.WizardBitmapImage2.Visible := False;
  WizardForm.WizardSmallBitmapImage.Visible := False;
  WizardForm.WelcomeLabel1.Left := ScaleX(36);
  WizardForm.WelcomeLabel1.Width := WizardForm.WelcomePage.ClientWidth - ScaleX(72);
  WizardForm.WelcomeLabel1.Top := ScaleY(44);
  WizardForm.WelcomeLabel1.Height := ScaleY(75);
  WizardForm.WelcomeLabel2.Left := ScaleX(38);
  WizardForm.WelcomeLabel2.Top := ScaleY(142);
  WizardForm.WelcomeLabel2.Width := WizardForm.WelcomePage.ClientWidth - ScaleX(76);
  WizardForm.WelcomeLabel2.Height := WizardForm.WelcomePage.ClientHeight - ScaleY(164);
  WizardForm.FinishedHeadingLabel.Left := ScaleX(36);
  WizardForm.FinishedHeadingLabel.Width := WizardForm.FinishedPage.ClientWidth - ScaleX(72);
  WizardForm.FinishedLabel.Left := ScaleX(38);
  WizardForm.FinishedLabel.Width := WizardForm.FinishedPage.ClientWidth - ScaleX(76);
  WizardForm.RunList.Left := ScaleX(38);
  WizardForm.RunList.Width := WizardForm.FinishedPage.ClientWidth - ScaleX(76);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\install-version.txt'), '{#AppVersion}' + #13#10, False);
end;

[UninstallDelete]
; Setup-created metadata only. Unknown files, original books and library folders are retained.
Type: files; Name: "{app}\install-version.txt"
