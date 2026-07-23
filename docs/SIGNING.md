# Signing

OnlyRag installer signing is handled by repository PowerShell scripts on Windows. Private signing
material must not be stored in the repository.

## Requirements

- Windows 10/11 SDK `signtool.exe`, or pass `-SignToolPath`.
- A trusted code-signing certificate, either installed in `CurrentUser\My` or supplied as an
  external PFX file outside the repository.
- NSIS 3.x for building the installer before signing.

## Build And Sign

Using an external PFX:

```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificatePath "C:\Path\To\OnlyRag-CodeSigning.pfx"
```

Using an installed certificate:

```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
```

`Sign-Release.ps1` builds the installer through
[`scripts/Build-Installer.ps1`](../scripts/Build-Installer.ps1), signs it, and runs non-invasive
signed release verification unless `-SkipReleaseVerification` is supplied. Temporarily imported
certificates are removed from `CurrentUser\My` unless `-KeepImportedCertificate` is supplied.

The script does not run the full install/launch/uninstall lifecycle. Run that separately on a
clean Windows verification machine:

```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
```

## Certificate Export For Enterprise Trust

To export the public certificate for enterprise trust distribution:

```powershell
pwsh .\scripts\Export-EnterpriseSigningCertificate.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe
```

The default output is `certificates\app\OnlyRag-Enterprise-CodeSigning.cer`. Do not export or
store private key material in the repository.

## Trust Check

```powershell
pwsh .\scripts\Test-EnterpriseSigningTrust.ps1 -CertificateThumbprint <thumbprint> -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe
```

This checks certificate store availability and installer signature/trust signals.

## Release Limit

An unsigned installer is a packaging artifact only. A signed installer is not production-ready
until full lifecycle verification passes on the target Windows verification environment.
