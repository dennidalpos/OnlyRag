#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

#ifndef RuntimeIdentifier
#define RuntimeIdentifier "win-x64"
#endif

#ifndef PublishDir
#define PublishDir "..\artifacts\publish\OnlyRag\win-x64"
#endif

#ifndef OutputDir
#define OutputDir "..\artifacts\installer"
#endif

#ifndef AppIcon
#define AppIcon "..\src\OnlyRag.App\Assets\OnlyRag.ico"
#endif

#define AppName "OnlyRag"
#define AppExeName "OnlyRag.App.exe"

[Setup]
AppId={{04F5D8FC-C732-4F34-8C13-B7E2D6C09F47}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=OnlyRag
AppPublisherURL=https://github.com/
AppSupportURL=https://github.com/
AppUpdatesURL=https://github.com/
DefaultDirName={localappdata}\Programs\OnlyRag
DefaultGroupName=OnlyRag
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=OnlyRag-Setup-{#AppVersion}-{#RuntimeIdentifier}
SetupIconFile={#AppIcon}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
SetupLogging=yes
UninstallDisplayIcon={app}\{#AppExeName}
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\OnlyRag"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall OnlyRag"; Filename: "{uninstallexe}"
Name: "{autodesktop}\OnlyRag"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,OnlyRag}"; Flags: nowait postinstall skipifsilent

[Code]
function HasSharedFramework(FrameworkName: String): Boolean;
var
  BasePath: String;
  FindRec: TFindRec;
begin
  Result := False;
  BasePath := ExpandConstant('{commonpf64}') + '\dotnet\shared\' + FrameworkName;

  if FindFirst(BasePath + '\10.*', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function HasWebView2Runtime(): Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    DirExists(ExpandConstant('{commonpf}') + '\Microsoft\EdgeWebView\Application') or
    DirExists(ExpandConstant('{localappdata}') + '\Microsoft\EdgeWebView\Application');
end;

function InitializeSetup(): Boolean;
var
  Missing: String;
begin
  Missing := '';

  if not HasSharedFramework('Microsoft.NETCore.App') then
    Missing := Missing + '- .NET 10 Runtime x64' + #13#10;

  if not HasSharedFramework('Microsoft.WindowsDesktop.App') then
    Missing := Missing + '- .NET 10 Windows Desktop Runtime x64' + #13#10;

  if not HasSharedFramework('Microsoft.AspNetCore.App') then
    Missing := Missing + '- .NET 10 ASP.NET Core Runtime x64' + #13#10;

  if not HasWebView2Runtime() then
    Missing := Missing + '- Microsoft Edge WebView2 Runtime' + #13#10;

  if Missing <> '' then
  begin
    MsgBox(
      'OnlyRag cannot be installed because required runtime prerequisites are missing:' + #13#10 + #13#10 +
      Missing + #13#10 +
      'Install the missing Microsoft runtimes, then run this installer again.' + #13#10 + #13#10 +
      'Ollama is not bundled and must be installed/configured separately for model features. OCR/PaddleOCR runtime packages are optional and are not bundled by this installer.',
      mbError,
      MB_OK);
    Result := False;
  end
  else
    Result := True;
end;
