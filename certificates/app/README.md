# App Certificate Notes

Do not put private code-signing material in this repository directory.

Private `.pfx` files, passwords, recovery keys, and vendor portal exports must stay outside the
repository workspace. Use `scripts\Sign-Release.ps1 -CertificatePath` with an external PFX path, or
use `-CertificateThumbprint` for a certificate already installed in a Windows certificate store.

This directory may hold non-secret documentation placeholders and optional exported public `.cer`
files for enterprise trust distribution. Generated certificate files remain ignored by Git.
