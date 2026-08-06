---
name: windows-packaging-signing
description: Technical skill for packaging, code-signing, and installer verification in OnlyRag. Covers NSIS installer creation, Windows SDK signtool.exe signing, enterprise certificate management, prerequisite testing, and release lifecycle verification.
---

# Windows Packaging, Signing & Release Verification Skill

This skill provides guidelines and canonical command flows for building, signing, and verifying OnlyRag Windows installer packages.

## 1. Official Documentation Sources

- **NSIS Documentation**: [nsis.sourceforge.io/Docs/](https://nsis.sourceforge.io/Docs/)
- **Microsoft SignTool Documentation**: [learn.microsoft.com/windows/win32/seccrypto/signtool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)
- **Microsoft Authenticode Code Signing**: [learn.microsoft.com/windows/win32/seccrypto/authenticode-digital-signatures](https://learn.microsoft.com/en-us/windows/win32/seccrypto/authenticode-digital-signatures)
- **.NET Application Publishing**: [learn.microsoft.com/dotnet/core/deploying](https://learn.microsoft.com/en-us/dotnet/core/deploying/)

## 2. Packaging Architecture & Files

- **NSIS Script**: [`packaging/OnlyRag.nsi`](file:///d:/GITHUB/OnlyRag/packaging/OnlyRag.nsi)
- **Bundled Qdrant Manifest**: [`packaging/qdrant/manifest.json`](file:///d:/GITHUB/OnlyRag/packaging/qdrant/manifest.json)
- **Build Output Directory**: `artifacts/publish/OnlyRag/win-x64`
- **Installer Output Directory**: `artifacts/installer`
- **Prerequisite Detection Logic**: [`scripts/Test-InstallerPrerequisites.ps1`](file:///d:/GITHUB/OnlyRag/scripts/Test-InstallerPrerequisites.ps1)

## 3. Canonical Packaging & Signing Workflows

### 1. Build Unsigned Installer Package
```powershell
pwsh .\scripts\Build-Installer.ps1 -Configuration Release
```
Compiles web UI assets, verifies bundled Qdrant payload, publishes self-contained `.NET 10 win-x64` binaries, checks native ONNX/DirectML runtime files, and invokes NSIS (`makensis.exe`).

### 2. Sign Release Installer
```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
```
Uses `signtool.exe` with RFC 3161 timestamping to digitally sign the generated installer setup executable.

### 3. Verify Signed Installer Lifecycle
```powershell
pwsh .\scripts\Test-InstallerRelease.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe -RequireSigned -RunInstallLifecycle
```
Performs non-invasive certificate chain verification, then optionally executes clean installation, launch check, and uninstallation verification on a test profile.

### 4. Export Enterprise Signing Certificate
```powershell
pwsh .\scripts\Export-EnterpriseSigningCertificate.ps1 -CertificateThumbprint <thumbprint>
```
Exports the public certificate (`.cer`) for enterprise deployment trust distribution without exposing private keys.

## 4. Operational & Security Rules

1. **No Private Keys in Repo**: PFX files and private keys must never be committed to source control or saved in repository folders.
2. **Signed Releases Only**: An unsigned installer or an installer without lifecycle verification evidence is strictly unready for production distribution.
3. **Self-Contained Runtime**: The installer must bundle all required .NET runtime binaries and local Qdrant executables so end users do not need a separate .NET installation.
