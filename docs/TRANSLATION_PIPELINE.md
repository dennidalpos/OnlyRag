# Translation Pipeline

OnlyRag stores document translations as local SQLite records. A translation is split into
page-based units from indexed document text, queued as a local job, translated through an
installed Ollama model, and checkpointed after each unit.

Current stages:

1. Queue translation job in the local persistent job queue.
2. Resolve source document text from local storage.
3. Split text into translation-safe units.
4. Translate through local or LAN Ollama integration.
5. Persist machine output and editable translated text.
6. Allow manual correction per unit.
7. Capture source layout metadata for each translation unit.
8. Export translated text to local filesystem formats.

Manual corrections:

- `translation_units.machine_translated_text` keeps the latest generated text.
- `translation_units.translated_text` is the editable text shown to the user and used for export.
- `translation_units.manually_edited` is set when the user saves a correction.
- `translation_units.layout_metadata_json` records the source extension, page number, page row id,
  unit index, and unit kind used by layout-aware export renderers.
- Fresh SQLite schema initialization includes `machine_translated_text`, `translated_text`, and
  `manually_edited`; this repository does not migrate existing translation data.

Compare UI endpoints:

- `GET /api/translations/{id}/compare?page=N` returns original and translated units for one page.
- `PUT /api/translations/{id}/units/{unitId}` saves the corrected translated text for one unit.

Export endpoint:

- `POST /api/translations/{id}/export` with `{ "format": "txt" | "markdown" | "html" | "docx" | "pdf" }`
  writes a file under `%LOCALAPPDATA%\OnlyRag\documents\exports\` and returns `outputPath`
  plus `status`.
- File names are generated as `originalName_targetLanguage_timestamp.ext`, sanitized for
  Windows file names, and made unique instead of overwriting an existing export.
- TXT and Markdown preserve unit order and add page separators. Table units are grouped into
  readable table sections when the indexed source marked them as table cells.
- HTML export is self-contained and print-friendly. It preserves page sections with
  `data-source-page`, unit metadata with `data-unit-kind`, paragraph units, grouped table-cell
  units, escaped document text, and print page breaks without requiring an external renderer.
- DOCX export writes page headings with page-break-before after the first page, paragraph units,
  textbox styling, and simple tables for consecutive table-cell units.
- PDF export creates the same DOCX layout first, then converts it through the configured
  LibreOffice converter. It requires LibreOffice and inherits the same layout metadata and page
  break behavior as DOCX.

Layout fidelity acceptance criteria:

- Unit order must remain stable by `unit_index`.
- Page transitions must remain visible in TXT/Markdown and must produce HTML print page breaks and
  DOCX/PDF page-break-before markers.
- Table-cell runs must remain grouped in HTML, DOCX, and PDF output.
- Source layout metadata must be persisted with each translation unit so renderers can evolve
  without reindexing the source document.
- Export tests must verify HTML layout attributes and DOCX page break/table structure.
