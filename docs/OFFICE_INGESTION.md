# Office Ingestion

OnlyRag supports current Office OpenXML files directly and legacy binary Office files through an
optional LibreOffice conversion path.

## Direct Extraction

The app imports DOCX, XLSX, and PPTX using .NET/OpenXML infrastructure in
[`src/OnlyRag.Infrastructure/Ingestion`](../src/OnlyRag.Infrastructure/Ingestion). Extracted text
is chunked and stored with document/page metadata before indexing.

## Legacy Office Conversion

Legacy `.doc`, `.xls`, and `.ppt` files require LibreOffice. The bootstrap and Settings UI detect
LibreOffice where possible. A custom path can be supplied with `ONLYRAG_LIBREOFFICE_PATH`.

If LibreOffice is unavailable, legacy Office conversion remains unavailable but other supported
formats still work.

## PDF Export

Translation export can produce PDF output when the required conversion path is available. TXT,
Markdown, HTML, and DOCX exports do not depend on the same PDF conversion path.

## Troubleshooting

- Install official LibreOffice for Windows when legacy Office ingestion is needed.
- Verify `soffice.exe` exists under `Program Files\LibreOffice\program` or configure
  `ONLYRAG_LIBREOFFICE_PATH`.
- Re-run bootstrap or use Settings diagnostics after installing LibreOffice.
