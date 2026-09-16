#define MyAppName "PrinterWLAN"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "QIAOXIU LI"
#define MyAppExeName "PrinterWLAN.exe"

[Setup]
AppId={{62F5778D-3E45-4D84-95B4-A962935A7952}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\PrinterWLAN
DefaultGroupName=PrinterWLAN
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=PrinterWLAN-Setup-x64
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName=PrinterWLAN
VersionInfoVersion=1.1.0.0
VersionInfoProductName=PrinterWLAN
VersionInfoProductVersion=1.1.0
VersionInfoCompany=QIAOXIU LI
LicenseFile=..\LICENSE

[Dirs]
Name: "{commonappdata}\PrinterWLAN"
Name: "{commonappdata}\PrinterWLAN\Data"
Name: "{commonappdata}\PrinterWLAN\Temp"
Name: "{commonappdata}\PrinterWLAN\Logs"
Name: "{commonappdata}\PrinterWLAN\Diagnostics"
Name: "{commonappdata}\PrinterWLAN\Cache"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\third-party\runtime\LibreOffice\*"; DestDir: "{app}\third-party\LibreOffice"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "printerwlan-console.cmd"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PrinterWLAN 管理控制台"; Filename: "{app}\printerwlan-console.cmd"; WorkingDir: "{app}"
Name: "{group}\卸载 PrinterWLAN"; Filename: "{uninstallexe}"

[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Check: NeedsPathUpdate('{app}')

[Run]
Filename: "{sys}\icacls.exe"; Parameters: """{commonappdata}\PrinterWLAN"" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "stop PrinterWLAN"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete PrinterWLAN"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "create PrinterWLAN binPath= ""{app}\{#MyAppExeName}{code:GetServiceArguments}"" start= auto obj= LocalSystem DisplayName= ""PrinterWLAN"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description PrinterWLAN ""局域网网页打印服务"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""PrinterWLAN HTTP 8080"" dir=in action=allow protocol=TCP localport=8080 program=""{app}\{#MyAppExeName}"" enable=yes"; Flags: runhidden waituntilterminated
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /TN ""PrinterWLAN Management Console"" /TR ""{app}\printerwlan-console.cmd"" /SC ONLOGON /RL HIGHEST /F"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start PrinterWLAN"; Flags: runhidden waituntilterminated
Filename: "{app}\printerwlan-console.cmd"; Description: "打开 PrinterWLAN 管理控制台"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop PrinterWLAN"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete PrinterWLAN"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""PrinterWLAN HTTP 8080"""; Flags: runhidden waituntilterminated; RunOnceId: "DeleteFirewall"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""PrinterWLAN Management Console"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteTask"

[Code]
function HasCommandLineParameter(const Name: String): Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then begin Result := True; Exit; end;
end;

function GetServiceArguments(Param: String): String;
begin
  if HasCommandLineParameter('/FAKEPRINTER') then
    Result := ' --PrinterWLAN:UseFakePrinter=true'
  else
    Result := '';
end;

function NeedsPathUpdate(Param: String): Boolean;
var Paths: String;
begin
  if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', Paths) then Paths := '';
  Result := Pos(';' + Uppercase(ExpandConstant(Param)) + ';', ';' + Uppercase(Paths) + ';') = 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox('是否保留 C:\ProgramData\PrinterWLAN 中的用户配置和日志？' + #13#10 + #13#10 +
      '建议选择“是”，以后重新安装时可以继续使用。选择“否”将永久删除全部数据。',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDNO then
      DelTree(ExpandConstant('{commonappdata}\PrinterWLAN'), True, True, True);
end;
