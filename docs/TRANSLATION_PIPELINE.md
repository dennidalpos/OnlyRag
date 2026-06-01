# Translation Pipeline

OnlyRag supports local document translation workflows backed by Ollama.

## Flow

1. A user selects an indexed document and target translation settings.
2. The backend creates a local translation job.
3. Source text is processed into page-based translation units.
4. Ollama receives translation prompts for the configured model.
5. Outputs are validated for required placeholders. If validation fails, the job asks Ollama to
   repair the same unit before marking it failed.
6. Translation units are persisted locally and can be edited in the UI.
7. Export creates TXT, Markdown, HTML, DOCX, or PDF output.

Backend translation code lives in [`src/OnlyRag.Api`](../src/OnlyRag.Api), especially the
translation endpoints, prompt builder, output validator, and export services. Storage for
translation records lives under [`src/OnlyRag.Infrastructure/Storage`](../src/OnlyRag.Infrastructure/Storage).
Frontend translation UI code lives under [`src/OnlyRag.Web/src/components`](../src/OnlyRag.Web/src/components).

## Requirements

- A reachable Ollama endpoint and configured translation model.
- Indexed source document text.
- Optional LibreOffice/conversion support for PDF export paths.

## Limits

- Translation quality and context handling depend on the configured model and `num_ctx` settings.
- PDF export has more runtime prerequisites than TXT, Markdown, HTML, or DOCX export.
- Active translation jobs are local jobs and follow the same shutdown/cancellation behavior as
  other local work.
