# Firma Digitale (Code Signing)

La firma degli installer di OnlyRag è gestita dagli script PowerShell 7 su Windows. Nessun materiale o chiave di firma privata deve essere conservato nel repository.

## Prerequisiti

- `signtool.exe` dal Windows 10/11 SDK.
- Un certificato di firma valido installato nello store o fornito come file PFX esterno al repository.
- NSIS 3.x per la compilazione dell'installer.

## Compilazione e Firma

Firma tramite file PFX esterno:

```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificatePath "C:\Path\To\OnlyRag-CodeSigning.pfx"
```

Firma tramite certificato installato:

```powershell
pwsh .\scripts\Sign-Release.ps1 -CertificateThumbprint <thumbprint>
```

## Esportazione Certificato per Trust Aziendale

Per esportare il certificato pubblico:

```powershell
pwsh .\scripts\Export-EnterpriseSigningCertificate.ps1 -InstallerPath .\artifacts\installer\OnlyRag-Setup-0.1.0-win-x64.exe
```

L'output predefinito è `certificates\app\OnlyRag-Enterprise-CodeSigning.cer`.
