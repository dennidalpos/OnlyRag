import os
import shutil
import stat

def rimuovi_sola_lettura(func, path, excinfo):
    """
    Rimuove il flag di sola lettura dai file (come i file di Git) 
    che impediscono a Windows di cancellare la cartella.
    """
    os.chmod(path, stat.S_IWRITE)
    func(path)

# Lista di tutte le cartelle da eliminare sul tuo PC
cartelle_da_eliminare = [
    r"C:\Users\Utente\.paddleocr",
    r"C:\Users\Utente\.paddlex",
    r"C:\Users\Utente\.paddlehub",
    r"C:\Users\Utente\AppData\Local\pip\cache",
    r"C:\Users\Utente\AppData\Local\OnlyRag\ocr-python\.venv",
    r"C:\Users\Utente\AppData\Local\Programs\OnlyRag",
    r"C:\Users\Utente\AppData\Local\OnlyRag"
]

print("=== INIZIO PULIZIA TOTALE IN CORSO ===")

for percorso in cartelle_da_eliminare:
    if os.path.exists(percorso):
        try:
            print(f"Eliminazione di: {percorso}...")
            # shutil.rmtree elimina l'albero intero di directory
            # on_error gestisce i file protetti o bloccati in Windows
            shutil.rmtree(percorso, onerror=rimuovi_sola_lettura)
            print(f" OK: Eliminata con successo.")
        except Exception as e:
            print(f" ERRORE durante l'eliminazione di {percorso}: {e}")
    else:
        print(f" NOTA: {percorso} non esiste o è già stata rimossa.")

print("=== PULIZIA COMPLETATA ===")
