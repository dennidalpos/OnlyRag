export const UI_STRINGS = {
  common: {
    loading: "Caricamento...",
    saving: "Salvataggio...",
    processing: "Elaborazione in corso...",
    noResults: "Nessun risultato trovato",
    unsavedChanges: "Modifiche non salvate",
    cancel: "Annulla",
    save: "Salva",
    delete: "Elimina",
    confirm: "Conferma",
    close: "Chiudi",
    retry: "Riprova",
    refresh: "Aggiorna",
    clear: "Cancella",
    search: "Cerca...",
    actions: "Azioni",
    status: "Stato",
    details: "Dettagli"
  },
  chat: {
    newChat: "Nuova chat",
    noModelConfiguredTitle: "Nessun modello configurato in Ollama.",
    noModelConfiguredDetail: "Scarica o abilita almeno un modello LLM da Ollama prima di inviare messaggi.",
    ollamaOfflineTitle: "Ollama è offline.",
    ollamaOfflineDetail: "Avvia Ollama o verifica l'indirizzo nelle impostazioni per caricare i modelli.",
    emptyStateTitle: "Inizia una conversazione.",
    emptyStateText: "Poni domande sui tuoi documenti locali, analizza codice o genera contenuti con i modelli RAG 2.0.",
    searchPlaceholder: "Scrivi un messaggio o poni una domanda sui tuoi documenti... (Shift+Enter per a capo)"
  },
  documents: {
    title: "Gestione Documenti",
    ocrDialogTitle: "Modalità di lettura testo",
    ocrLanguageTitle: "Lingua documento",
    noPreviewTitle: "Anteprima non disponibile",
    noPagesTitle: "Nessuna pagina disponibile per questo documento.",
    confirmDeleteDocument: "Eliminare definitivamente questo documento dall'archivio locale?"
  },
  settings: {
    title: "Impostazioni di Sistema",
    ollamaOfflineAlertTitle: "Connessione a Ollama non riuscita.",
    ollamaOfflineAlertDetail: "Assicurati che Ollama sia in esecuzione localmente per caricare i modelli di Chat, Embedding, Traduzione e Coding.",
    rerankerMissingAlertTitle: "Modello di riclassificazione non installato. I risultati RAG saranno meno precisi.",
    rerankerMissingAlertDetail: "Senza il modello ONNX Cross-Encoder (Re-Ranker), la ricerca utilizzerà un fallback euristico.",
    ocrMissingAlertTitle: "Runtime OCR locale non configurato.",
    ocrMissingAlertDetail: "L'estrazione del testo da scansioni, PDF e file Office richiede il runtime OCR.",
    libreofficeMissingAlertTitle: "LibreOffice non configurato.",
    libreofficeMissingAlertDetail: "L'esportazione dei documenti e delle traduzioni in formato PDF richiede LibreOffice.",
    confirmLogsReset: "Cancellare ed azzerare tutti i log applicativi dal sistema?",
    confirmSettingsReset: "Ripristinare le impostazioni iniziali bilanciate senza eliminare documenti e dati locali?",
    confirmAppDataReset: "Pianificare il reset totale al prossimo avvio? Verranno eliminati documenti, indici, chat, cache, log, profilo WebView2 e impostazioni locali."
  },
  images: {
    title: "Generazione Immagini DirectML",
    gpuLabel: "Usa accelerazione GPU",
    gpuInfoTip: "Utilizza DirectML per accelerare l'inferenza della generazione immagini sulla scheda grafica Windows.",
    confirmDeleteImage: "Eliminare l'immagine selezionata dal catalogo locale?"
  },
  coding: {
    title: "Coding & Subagent Studio",
    approvalRequiredTitle: "Approvazione richiesta per esecuzione comando",
    confirmActionInfoTip: "Questa azione richiede conferma. Controlla i parametri prima di procedere."
  },
  sync: {
    title: "Sincronizzazione LAN & Dispositivi Autorizzati",
    subtitle: "Accoppiamento protetto e trasferimento cifrato AES-256 tra nodi locali"
  }
} as const;

export type UiStrings = typeof UI_STRINGS;
