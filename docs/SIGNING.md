# App Signing

This document is the step-by-step release signing flow for OnlyRag on Windows.

## What You Need

- PowerShell 7.
- .NET 10 SDK.
- Node.js compatible with `src\OnlyRag.Web`.
- Inno Setup 6.
- Windows SDK with `signtool.exe`.
- A trusted code-signing certificate with private key, exported as `.pfx`, or already installed in `Cert:\CurrentUser\My` / `Cert:\LocalMachine\My`.

## Private Certificate Storage

Keep private signing material outside the repository workspace. Do not place `.pfx` files,
passwords, recovery keys, or vendor portal exports under `certificates\app` or any other
repository path.

`certificates\app` is reserved for non-secret documentation placeholders and optional exported
public `.cer` files used for enterprise trust distribution.

## Option A: Sign From an External PFX

1. Store the PFX outside the repository, for example under a secured operator-controlled folder.

2. Start PowerShell 7 from the repository root.

3. Run the signing pipeline with the external path:

```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificatePath "D:\SecureSigning\OnlyRag-CodeSigning.pfx" -Version "0.1.0"
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

## Enterprise Self-Signed Distribution

For public distribution, use a CA-trusted code-signing certificate. A self-signed certificate is
acceptable only for controlled enterprise deployment where target Windows machines receive trust
through Group Policy or an equivalent device-management policy.

Do not distribute the PFX or its password. Export and distribute only the public `.cer`:

```powershell
pwsh .\scripts\Export-EnterpriseSigningCertificate.ps1 `
  -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe `
  -OutputPath .\certificates\app\OnlyRag-Enterprise-CodeSigning.cer
```

Deploy the exported `.cer` through Group Policy to both stores:

- `Computer Configuration > Policies > Windows Settings > Security Settings > Public Key Policies > Trusted Root Certification Authorities`
- `Computer Configuration > Policies > Windows Settings > Security Settings > Public Key Policies > Trusted Publishers`

After Group Policy applies on a target machine, verify enterprise trust:

```powershell
pwsh .\scripts\Test-EnterpriseSigningTrust.ps1 `
  -CertificateThumbprint "1E4A238A06A117710F11816DAB0C1833AC775712" `
  -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe
```

Then run the installer release verification:

```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 `
  -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe `
  -RequireSigned
```

For release sign-off, repeat lifecycle verification on a clean domain-joined machine after the GPO
has applied:

```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 `
  -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe `
  -RequireSigned `
  -RunInstallLifecycle
```

## Non-Interactive Runs

For local automation, pass the PFX password as a `SecureString` value created outside repository
files:

```powershell
$certificatePassword = Read-Host "PFX password" -AsSecureString
pwsh .\scripts\Sign-Release.ps1 `
  -CertificatePath "D:\SecureSigning\OnlyRag-CodeSigning.pfx" `
  -CertificatePassword $certificatePassword `
  -Version "0.1.0"
```

Do not store this value in repository files, command history, logs, or environment variables.

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
- `Pass -CertificatePath for an external .pfx file or -CertificateThumbprint`: provide an installed certificate thumbprint or a PFX path outside the repository.
- `Refusing to import a private signing certificate from inside the repository`: move the PFX outside the repository and pass that path.
- `Signature status is NotSigned`: rerun through `scripts\Sign-Release.ps1` and verify the certificate has a private key.
- `UnknownError` or timestamp failure: retry with network access or pass another RFC 3161 timestamp server through `-TimestampServer`.
