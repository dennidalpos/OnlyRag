# Office Ingestion

OnlyRag extracts modern Office Open XML files (`.docx`, `.xlsx`, `.pptx`) directly first.
Legacy Office files (`.doc`, `.xls`, `.ppt`) and modern Office files that cannot be read
directly are converted through LibreOffice headless when the converter is available.

## Converter

LibreOffice is an optional dependency. OnlyRag does not install it automatically.

Detection order:

1. Path configured in Settings.
2. `ONLYRAG_LIBREOFFICE_PATH`.
3. Default Windows install paths under `Program Files`.
4. `soffice.exe` on `PATH`.

The configured path can point to `soffice.exe`, the LibreOffice install directory, or the
`program` directory.

## Runtime Behavior

Conversion runs inside the document ingestion job. Temporary files are created under
`%LOCALAPPDATA%\OnlyRag\temp\office-conversion` and deleted after the converted PDF has been
processed by the existing PDF pipeline. The conversion has a configurable timeout, defaults to
120 seconds, and is checkpointed before conversion and during the downstream PDF ingestion.

If LibreOffice is not available, the document is marked as `RequiresAdditionalComponent` and
the UI shows a simple message that LibreOffice is required. Conversion errors are logged in
`%LOCALAPPDATA%\OnlyRag\logs\backend.log`.

Packaging, installer delivery, upgrade, uninstall, and signing for LibreOffice are not part of
this repository. Any bundled or consent-based installer flow must be designed separately.
