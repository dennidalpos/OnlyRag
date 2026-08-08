# Generazione Immagini

OnlyRag utilizza un provider di generazione immagini ONNX integrato e 100% locale. Non è necessario installare strumenti di terze parti e i prompt non vengono inviati ad endpoint remoti.

## Modelli

La sezione Immagini elenca i modelli del catalogo locale con:
- ID e nome visualizzato del modello;
- profilo GPU/CPU raccomandato;
- URL di download modificabile (es. repository Hugging Face);
- licenza e dimensione attesa;
- file locali richiesti e metadati di verifica SHA256.

Il catalogo built-in include l'entry ONNX DirectML/CPU:
- `lcm-sdxl-olive-onnx` (`LCM SDXL Olive ONNX`), ottimizzato per l'esecuzione su Windows.

I file dei modelli sono memorizzati in:

```powershell
%LOCALAPPDATA%\OnlyRag\models\images
```

I download richiedono il consenso esplicito dell'utente nell'interfaccia UI prima di avviare il caricamento.

## Runtime

OnlyRag utilizza la pipeline ONNX Stable Diffusion / SDXL. Su Windows preferisce l'accelerazione GPU DirectML (`Microsoft.ML.OnnxRuntime.DirectML`) e passa automaticamente all'esecuzione CPU in caso di mancata inizializzazione della GPU.

Le immagini generate sono salvate in:

```powershell
%LOCALAPPDATA%\OnlyRag\images\generated
```

L'editor integrato nell'interfaccia consente di ritagliare le immagini, ruotarle, aggiungere testo overlay ed esportare le modifiche.

