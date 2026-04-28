# App Signing

This document is the step-by-step release signing flow for OnlyRag on Windows.

## What You Need

- PowerShell 7.
- .NET 10 SDK.
- Node.js compatible with `src\OnlyRag.Web`.
- Inno Setup 6.
- Windows SDK with `signtool.exe`.
- A trusted code-signing certificate with private key, exported as `.pfx`, or already installed in `Cert:\CurrentUser\My` / `Cert:\LocalMachine\My`.

## Certificate Folder

Put local app signing certificates in:

```powershell
.\certificates\app\
```

Recommended file name:

```text
OnlyRag-CodeSigning.pfx
```

Certificate files are ignored by Git. Do not commit `.pfx`, `.cer`, passwords, recovery keys, or vendor portal exports.

## Option A: Sign From a PFX in the Repository Certificate Folder

1. Copy the PFX to `.\certificates\app\OnlyRag-CodeSigning.pfx`.

2. Start PowerShell 7 from the repository root.

3. Run the signing pipeline:

```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificatePath .\certificates\app\OnlyRag-CodeSigning.pfx -Version "0.1.0"
```

4. Enter the PFX password when prompted.

The script imports the certificate into `Cert:\CurrentUser\My`, builds the installer, signs it with SHA-256 and an RFC 3161 timestamp, verifies the signature, runs release verification with `-RequireSigned`, then removes the temporarily imported certificate.

## Option B: Sign With an Installed Certificate Thumbprint

1. Find the certificate thumbprint:

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Select-Object Subject, Thumbprint, NotAfter
```

2. Run the signing pipeline:

```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint "<SHA1 thumbprint>" -Version "0.1.0"
```

Use this option when the certificate is managed by Windows, a hardware token, or a CI runner certificate store.

## Non-Interactive Runs

For local automation, set the PFX password in the current PowerShell session before invoking the script:

```powershell
$env:ONLYRAG_CERT_PASSWORD = "<pfx password>"
pwsh .\scripts\Sign-Release.ps1 -CertificatePath .\certificates\app\OnlyRag-CodeSigning.pfx -Version "0.1.0"
Remove-Item Env:\ONLYRAG_CERT_PASSWORD
```

Do not store this value in repository files.

## Output

The signed installer is produced under:

```text
artifacts\installer\
```

The release verification evidence is produced under:

```text
artifacts\release-verification\
```

## Final Release Verification

Before publishing a release, run the lifecycle verification on a clean Windows release machine:

```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
```

This verifies the signature, install, launch, shortcuts, optional component status, uninstall cleanup, and evidence generation.

## Troubleshooting

- `signtool.exe was not found`: install the Windows SDK or pass `-SignToolPath`.
- `Inno Setup compiler was not found`: install Inno Setup 6 or pass `-InnoSetupCompiler`.
- `No .pfx certificate found`: put a single `.pfx` in `.\certificates\app\` or pass `-CertificatePath`.
- `Signature status is NotSigned`: rerun through `scripts\Sign-Release.ps1` and verify the certificate has a private key.
- `UnknownError` or timestamp failure: retry with network access or pass another RFC 3161 timestamp server through `-TimestampServer`.

