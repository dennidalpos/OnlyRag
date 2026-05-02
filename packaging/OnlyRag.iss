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

#ifndef WizardImage
#define WizardImage "..\assets\brand\setup\onlyrag-setup-wizard-image-164x314.bmp"
#endif

#ifndef WizardSmallImage
#define WizardSmallImage "..\assets\brand\setup\onlyrag-setup-wizard-small-55x55.bmp"
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
WizardImageFile={#WizardImage}
WizardSmallImageFile={#WizardSmallImage}
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
begin
  if not HasWebView2Runtime() then
  begin
    MsgBox(
      'OnlyRag cannot be installed because a required Windows runtime is missing:' + #13#10 + #13#10 +
      '- Software: Microsoft Edge WebView2 Runtime' + #13#10 +
      '- Minimum supported version: current Evergreen Runtime for Windows 10 1809 or newer / Windows 11' + #13#10 +
      '- Why it is required: OnlyRag is a WPF desktop app that renders its bundled React UI through Microsoft WebView2. Without WebView2 the app can install but cannot show the first window reliably.' + #13#10 +
      '- Install: download and install the official Microsoft Edge WebView2 Evergreen Runtime from https://developer.microsoft.com/microsoft-edge/webview2/' + #13#10 +
      '- Verify: open Settings > Apps and confirm "Microsoft Edge WebView2 Runtime" is listed, or check for msedgewebview2.exe under Program Files\Microsoft\EdgeWebView\Application.' + #13#10 + #13#10 +
      'After installing WebView2, run this OnlyRag setup again.' + #13#10 + #13#10 +
      'The OnlyRag installer includes the required .NET runtime components in the application package. Ollama, LibreOffice, and OCR/PaddleOCR Python packages are optional feature dependencies configured from the app settings.',
      mbError,
      MB_OK);
    Result := False;
  end
  else
    Result := True;
end;
