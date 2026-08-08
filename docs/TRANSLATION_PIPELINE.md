# Pipeline di Traduzione

OnlyRag supporta workflow di traduzione locale dei documenti indicizzati basati su Ollama.

## Flusso

1. L'utente seleziona un documento indicizzato e configura le impostazioni di traduzione.
2. Il backend crea un job di traduzione locale.
3. Il testo sorgente viene suddiviso in unità di traduzione basate sulla pagina.
4. Ollama riceve i prompt di traduzione per il modello configurato.
5. Gli output vengono validati per i placeholder richiesti. In caso di errore, il job tenta una riparazione dell'unità prima di segnarla come fallita.
6. Le unità tradotte vengono salvate localmente e possono essere modificate nell'interfaccia UI.
7. L'esportazione genera file TXT, Markdown, HTML, DOCX o PDF (via LibreOffice).

## Prerequisiti

- Endpoint Ollama raggiungibile e modello di traduzione configurato.
- Testo del documento sorgente indicizzato.
- LibreOffice installato per l'esportazione in formato PDF.
