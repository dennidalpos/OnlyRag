# Pipeline OCR (Architettura Dual-Engine)

OnlyRag include un'architettura Dual-Engine OCR:
1. **Motore Nativo C# DirectML ONNX OCR** ([`OnnxDirectMlOcrEngine.cs`](../src/OnlyRag.Infrastructure/Ocr/OnnxDirectMlOcrEngine.cs)): Motore OCR in-process nativo basato su ONNX Runtime DirectML con accelerazione GPU hardware. Non richiede alcuna dipendenza runtime Python esterna.
2. **Bridge Python PaddleOCR** ([`PaddleOcrEngine.cs`](../src/OnlyRag.Infrastructure/Ocr/PaddleOcrEngine.cs)): Subprocess bridge per PaddleOCR attivo quando i prerequisiti Python OCR (Python 3.10-3.13) sono installati nel sistema.

## Componenti

- [`src/OnlyRag.Infrastructure/Ocr/OnnxDirectMlOcrEngine.cs`](../src/OnlyRag.Infrastructure/Ocr/OnnxDirectMlOcrEngine.cs): Implementazione nativa C# DirectML ONNX OCR.
- [`src/OnlyRag.Infrastructure/Ocr/PaddleOcrEngine.cs`](../src/OnlyRag.Infrastructure/Ocr/PaddleOcrEngine.cs): Subprocess bridge Python per PaddleOCR.
- [`scripts/ocr/paddle_ocr_bridge.py`](../scripts/ocr/paddle_ocr_bridge.py): Script bridge Python.
- [`scripts/ocr/install_ocr_runtime.ps1`](../scripts/ocr/install_ocr_runtime.ps1): Script per il provisioning dell'ambiente runtime OCR Python.
- [`scripts/ocr/runtime-manifest.json`](../scripts/ocr/runtime-manifest.json): Manifest del runtime CPU/GPU per PaddleOCR.
- [`src/OnlyRag.Infrastructure/Ocr`](../src/OnlyRag.Infrastructure/Ocr): Factory dei motori OCR (`IOcrEngine`), repository di cache SQLite (`SqliteOcrCacheRepository`), policy di retry e store impostazioni.
- [`src/OnlyRag.Api/OcrProvisionRuntimeResolver.cs`](../src/OnlyRag.Api/OcrProvisionRuntimeResolver.cs): Risolutore per la selezione del runtime OCR.
- [`src/OnlyRag.Api/OcrRuntimeEnvironment.cs`](../src/OnlyRag.Api/OcrRuntimeEnvironment.cs): Gestore transazionale del ciclo di vita dell'ambiente virtuale.

## Selezione del Runtime

È supportato Python da 3.10 a 3.13. Il provisioning seleziona i pacchetti GPU NVIDIA solo quando vengono rilevati driver/runtime locali compatibili; in caso contrario viene utilizzato il fallback CPU. L'applicazione seleziona la GPU automaticamente dopo la verifica della Diagnostica.

## Ciclo di Vita dell'Ambiente e Ripristino

L'ambiente gestito dall'applicazione risiede in `%LOCALAPPDATA%\OnlyRag\ocr-python\.venv`. Il provisioning crea un ambiente staging fratello, installa le dipendenze verificate ed effettua il controllo del bridge prima di pubblicarlo.

La diagnostica classifica l'ambiente locale come `missing`, `incomplete`, `corrupt`, o `ready`. Per un ambiente `incomplete` o `corrupt`, la diagnostica avvia un tentativo di riparazione in background per sessione.

## Convalida

Verifica manifest:

```powershell
pwsh .\scripts\ocr\Test-OcrRuntimeManifest.ps1
```

Controllo del catalogo:

```powershell
pwsh .\scripts\ocr\Test-OcrRuntimeCatalog.ps1 -OutputPath .\artifacts\ocr-runtime-catalog\ocr-runtime-catalog.json
```

## Limiti

- L'OCR è opzionale; l'assenza di Python non blocca le funzionalità RAG o di indicizzazione del testo.
- L'accesso ad Internet è necessario durante il provisioning per scaricare i pacchetti Python.
