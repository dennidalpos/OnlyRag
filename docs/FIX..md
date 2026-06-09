Il problema non è Alembic né ComfyUI-Manager. La riga decisiva è questa:

AssertionError: Torch not compiled with CUDA enabled

Significa: ComfyUI parte in modalità Nvidia/CUDA, ma nel venv hai installato PyTorch senza supporto CUDA. Il log lo conferma anche qui:

Found comfy_kitchen backend cuda: {'available': False, ... 'CUDA not available on this system'}

Per Nvidia, la documentazione ComfyUI consiglia di reinstallare PyTorch con build CUDA; cita anche esattamente questo errore come caso da risolvere disinstallando torch e reinstallandolo con il comando CUDA. PyTorch, su Windows, dice di scegliere la piattaforma CUDA adatta e verificare poi con torch.cuda.is_available().

Fix consigliato: reinstalla PyTorch CUDA nel venv di ComfyUI

Apri PowerShell e lancia:

cd C:\Users\Utente\ComfyUI-Installs\ComfyUI\ComfyUI

.\.venv\Scripts\python.exe -m pip uninstall -y torch torchvision torchaudio

.\.venv\Scripts\python.exe -m pip install --upgrade pip

.\.venv\Scripts\python.exe -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu128

Poi verifica:

.\.venv\Scripts\python.exe -c "import torch; print(torch.__version__); print(torch.version.cuda); print(torch.cuda.is_available())"

Devi ottenere qualcosa tipo:

2.x.x+cu128
12.8
True

Se torch.cuda.is_available() resta False, il problema non è più ComfyUI: è driver/GPU/PyTorch.

Se ComfyUI richiede la build CUDA più nuova

La README corrente di ComfyUI per Nvidia indica anche questa variante:

.\.venv\Scripts\python.exe -m pip install torch torchvision torchaudio --extra-index-url https://download.pytorch.org/whl/cu130

ComfyUI segnala che il portable Nvidia attuale usa PyTorch CUDA 13.0 e che può richiedere driver Nvidia aggiornati.

Se non hai una GPU Nvidia compatibile

Avvia ComfyUI in CPU mode:

cd C:\Users\Utente\ComfyUI-Installs\ComfyUI\ComfyUI
.\.venv\Scripts\python.exe main.py --cpu

ComfyUI supporta --cpu, ma sarà molto più lento. L’argomento --cpu è documentato come modalità CPU-only.

Nota pratica

La warning:

Could not autodetect AIMDO implementation, assuming Nvidia

non è il crash. Il crash è solo PyTorch CPU-only mentre ComfyUI prova a usare CUDA. Prima cosa da fare: reinstallare torch CUDA dentro quel venv, non globalmente.