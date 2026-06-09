# Office Ingestion

OnlyRag supports current Office OpenXML files directly. Binary Office formats are not imported.

## Direct Extraction

The app imports DOCX, XLSX, and PPTX using .NET/OpenXML infrastructure in
[`src/OnlyRag.Infrastructure/Ingestion`](../src/OnlyRag.Infrastructure/Ingestion). Extracted text
is chunked and stored with document/page metadata before indexing.

## Unsupported Binary Office Formats

The app rejects `.doc`, `.xls`, and `.ppt` uploads. Save those files as DOCX, XLSX, or PPTX before
importing them.

## PDF Export

Translation export can produce PDF output through LibreOffice. TXT, Markdown, HTML, and DOCX
exports do not depend on LibreOffice.

## Troubleshooting

- Re-save `.doc`, `.xls`, or `.ppt` files as DOCX, XLSX, or PPTX before import.
- Install official LibreOffice for Windows only when translation PDF export is needed.
- Verify `soffice.exe` exists under `Program Files\LibreOffice\program` or configure
  `ONLYRAG_LIBREOFFICE_PATH`.
