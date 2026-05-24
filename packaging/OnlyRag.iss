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

#ifndef AppName
#define AppName "OnlyRag"
#endif

#define AppExeName "OnlyRag.App.exe"
#define PublisherName "OnlyRag"
#define ProjectUrl "https://github.com/dennidalpos/OnlyRag"
#define AppCopyright "Copyright (c) 2026 OnlyRag"

[Setup]
AppId={{04F5D8FC-C732-4F34-8C13-B7E2D6C09F47}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#PublisherName}
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/issues
AppUpdatesURL={#ProjectUrl}/releases
AppCopyright={#AppCopyright}
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
UninstallDisplayName={#AppName}
VersionInfoCompany={#PublisherName}
VersionInfoCopyright={#AppCopyright}
VersionInfoDescription=OnlyRag Windows desktop setup
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\ocr\install_ocr_runtime.ps1"" -RuntimeTarget auto"; StatusMsg: "Preparing PaddleOCR runtime..."; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  InstallerMessageMaxLineLength = 86;

function Spaces(Count: Integer): String;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Count do
    Result := Result + ' ';
end;

function WrappedLine(Prefix: String; Text: String): String;
var
  Remaining: String;
  Word: String;
  CurrentLine: String;
  ContinuationPrefix: String;
  SpacePosition: Integer;
begin
  Result := '';
  CurrentLine := '';
  ContinuationPrefix := Spaces(Length(Prefix));
  Remaining := Text;

  while Length(Remaining) > 0 do
  begin
    SpacePosition := Pos(' ', Remaining);
    if SpacePosition = 0 then
    begin
      Word := Remaining;
      Remaining := '';
    end
    else
    begin
      Word := Copy(Remaining, 1, SpacePosition - 1);
      Delete(Remaining, 1, SpacePosition);
    end;

    if Word <> '' then
    begin
      if CurrentLine = '' then
        CurrentLine := Prefix + Word
      else if Length(CurrentLine) + 1 + Length(Word) <= InstallerMessageMaxLineLength then
        CurrentLine := CurrentLine + ' ' + Word
      else
      begin
        if Result = '' then
          Result := CurrentLine
        else
          Result := Result + #13#10 + CurrentLine;
        CurrentLine := ContinuationPrefix + Word;
      end;
    end;
  end;

  if CurrentLine <> '' then
  begin
    if Result = '' then
      Result := CurrentLine
    else
      Result := Result + #13#10 + CurrentLine;
  end;
end;

function BulletLine(LabelText: String; Text: String): String;
begin
  Result := WrappedLine('- ' + LabelText + ': ', Text);
end;

function ParagraphLine(Text: String): String;
begin
  Result := WrappedLine('', Text);
end;

function UnsupportedWindowsMessage(): String;
begin
  Result :=
    ParagraphLine('{#AppName} cannot be installed because this Windows version is not supported.') + #13#10 + #13#10 +
    BulletLine('Software', 'Microsoft Windows') + #13#10 +
    BulletLine('Minimum supported version', 'Windows 10 version 1809, build 17763, or Windows 11') + #13#10 +
    BulletLine('Why it is required', '{#AppName} is a modern Windows desktop app using WPF, WebView2, and a self-contained .NET Windows runtime payload validated for Windows 10 1809 or newer') + #13#10 +
    BulletLine('Install', 'Update Windows through Settings > Windows Update, or use a Windows 10/11 client that meets the minimum version') + #13#10 +
    BulletLine('Verify', 'Press Win+R, run winver, and confirm Windows 10 version 1809/build 17763 or newer, or Windows 11') + #13#10 + #13#10 +
    ParagraphLine('After updating Windows, run this {#AppName} setup again.');
end;

function MissingWebView2Message(): String;
begin
  Result :=
    ParagraphLine('{#AppName} cannot be installed because a required Windows runtime is missing.') + #13#10 + #13#10 +
    BulletLine('Software', 'Microsoft Edge WebView2 Runtime') + #13#10 +
    BulletLine('Minimum supported version', 'Current Evergreen Runtime for supported Windows versions') + #13#10 +
    BulletLine('Why it is required', '{#AppName} renders its bundled React UI through Microsoft WebView2') + #13#10 +
    BulletLine('Install', 'Download and install the official Microsoft Edge WebView2 Evergreen Runtime from https://developer.microsoft.com/microsoft-edge/webview2/') + #13#10 +
    BulletLine('Verify', 'Open Settings > Apps and confirm Microsoft Edge WebView2 Runtime is listed, or check for msedgewebview2.exe under Program Files\Microsoft\EdgeWebView\Application') + #13#10 + #13#10 +
    ParagraphLine('After installing WebView2, run this {#AppName} setup again.') + #13#10 + #13#10 +
    ParagraphLine('The installer includes the required .NET runtime components and OCR CPU/NVIDIA provisioning manifests. Setup automatically prepares PaddleOCR packages when compatible Python and Internet access are available. Ollama and LibreOffice remain user-confirmed external/manual installs.');
end;

function FindNvidiaSmiPath(): String;
var
  SystemCandidate: String;
begin
  SystemCandidate := ExpandConstant('{sys}') + '\nvidia-smi.exe';
  if FileExists(SystemCandidate) then
  begin
    Result := SystemCandidate;
    Exit;
  end;

  Result := FileSearch('nvidia-smi.exe', GetEnv('PATH'));
end;

function NvidiaGpuOcrMemo(): String;
begin
  if FindNvidiaSmiPath() <> '' then
  begin
    Result :=
      BulletLine('NVIDIA OCR', 'NVIDIA management tools were detected. Setup will try the compatible GPU runtime automatically and OnlyRag will select GPU after Diagnostics reports it usable, unless CPU was saved manually.');
  end
  else
  begin
    Result :=
      BulletLine('NVIDIA OCR', 'NVIDIA management tools were not detected. OCR provisioning will use the CPU runtime unless a compatible NVIDIA driver is installed later.');
  end;
end;

function IsSupportedWindowsVersion(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result :=
    (Version.Major > 10) or
    ((Version.Major = 10) and (Version.Minor > 0)) or
    ((Version.Major = 10) and (Version.Minor = 0) and (Version.Build >= 17763));
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
    DirExists(ExpandConstant('{commonpf32}') + '\Microsoft\EdgeWebView\Application') or
    DirExists(ExpandConstant('{localappdata}') + '\Microsoft\EdgeWebView\Application');
end;

function InitializeSetup(): Boolean;
begin
  if not IsSupportedWindowsVersion() then
  begin
    MsgBox(UnsupportedWindowsMessage(), mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if not HasWebView2Runtime() then
  begin
    MsgBox(MissingWebView2Message(), mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;

function UpdateReadyMemo(
  Space: String;
  NewLine: String;
  MemoUserInfoInfo: String;
  MemoDirInfo: String;
  MemoTypeInfo: String;
  MemoComponentsInfo: String;
  MemoGroupInfo: String;
  MemoTasksInfo: String): String;
begin
  Result := '';
  if MemoDirInfo <> '' then
    Result := Result + MemoDirInfo + NewLine + NewLine;
  if MemoGroupInfo <> '' then
    Result := Result + MemoGroupInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then
    Result := Result + MemoTasksInfo + NewLine + NewLine;

  Result := Result + 'Optional feature dependencies:' + NewLine + NvidiaGpuOcrMemo();
end;
