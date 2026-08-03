import { useEffect, useState } from "react";
import { ExportPreviewResponse, getExportPreview } from "../../apiClient";
import { Eye, FileText, Download, X } from "lucide-react";

type ExportPreviewModalProps = {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  messages: Array<{
    role: string;
    text: string;
    citations?: Array<{ documentName: string; pageStart?: number; snippet: string }>;
  }>;
  onConfirmExport: (format: "Pdf" | "Docx", includeCitations: boolean, notes?: string) => Promise<void>;
};

export function ExportPreviewModal({
  isOpen,
  onClose,
  title,
  messages,
  onConfirmExport
}: ExportPreviewModalProps) {
  const [format, setFormat] = useState<"Pdf" | "Docx">("Pdf");
  const [includeCitations, setIncludeCitations] = useState(true);
  const [notes, setNotes] = useState("");
  const [preview, setPreview] = useState<ExportPreviewResponse | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isExporting, setIsExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!isOpen) return;

    async function loadPreview() {
      try {
        setIsLoading(true);
        setError(null);
        const res = await getExportPreview({
          title,
          format,
          messages,
          includeCitations,
          notes: notes.trim() || undefined
        });
        setPreview(res);
      } catch (err) {
        setError((err as Error).message);
      } finally {
        setIsLoading(false);
      }
    }

    void loadPreview();
  }, [isOpen, title, format, messages, includeCitations, notes]);

  if (!isOpen) return null;

  async function handleExport() {
    try {
      setIsExporting(true);
      await onConfirmExport(format, includeCitations, notes.trim() || undefined);
      onClose();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setIsExporting(false);
    }
  }

  return (
    <div
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(15, 23, 42, 0.65)",
        backdropFilter: "blur(4px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
        padding: "20px"
      }}
    >
      <div
        className="card"
        style={{
          width: "100%",
          maxWidth: "850px",
          maxHeight: "90vh",
          display: "flex",
          flexDirection: "column",
          background: "#ffffff",
          borderRadius: "12px",
          overflow: "hidden",
          boxShadow: "0 20px 25px -5px rgba(0,0,0,0.1), 0 10px 10px -5px rgba(0,0,0,0.04)"
        }}
      >
        <div
          style={{
            padding: "16px 20px",
            borderBottom: "1px solid #e2e8f0",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            background: "#f8fafc"
          }}
        >
          <h3 style={{ margin: 0, display: "flex", alignItems: "center", gap: "8px", color: "#0f172a" }}>
            <Eye className="icon" style={{ color: "#3b82f6" }} /> Anteprima Visiva Report prima dell'Esportazione
          </h3>
          <button className="button button--ghost button--icon" onClick={onClose}>
            <X className="icon" />
          </button>
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "260px 1fr", height: "550px" }}>
          <div style={{ padding: "16px", borderRight: "1px solid #e2e8f0", background: "#fafafa", overflowY: "auto" }}>
            <h4 style={{ margin: "0 0 12px 0", fontSize: "0.95rem" }}>Opzioni Esportazione</h4>

            <div style={{ marginBottom: "16px" }}>
              <label style={{ display: "block", fontSize: "0.85rem", fontWeight: 600, marginBottom: "6px" }}>Formato Documento:</label>
              <div style={{ display: "flex", gap: "8px" }}>
                <button
                  className={`button button--sm ${format === "Pdf" ? "button--primary" : "button--secondary"}`}
                  onClick={() => setFormat("Pdf")}
                  style={{ flex: 1 }}
                >
                  <FileText className="icon icon--sm" /> PDF
                </button>
                <button
                  className={`button button--sm ${format === "Docx" ? "button--primary" : "button--secondary"}`}
                  onClick={() => setFormat("Docx")}
                  style={{ flex: 1 }}
                >
                  <FileText className="icon icon--sm" /> DOCX
                </button>
              </div>
            </div>

            <div style={{ marginBottom: "16px" }}>
              <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "0.85rem", cursor: "pointer" }}>
                <input
                  type="checkbox"
                  checked={includeCitations}
                  onChange={(e) => setIncludeCitations(e.target.checked)}
                />
                Includi Fonti e Citazioni
              </label>
            </div>

            <div style={{ marginBottom: "16px" }}>
              <label style={{ display: "block", fontSize: "0.85rem", fontWeight: 600, marginBottom: "6px" }}>Note Intestazione:</label>
              <textarea
                className="input"
                rows={3}
                placeholder="Aggiungi una nota o descrizione per il report..."
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                style={{ width: "100%", fontSize: "0.85rem", resize: "vertical" }}
              />
            </div>

            {preview && (
              <div style={{ background: "#edf2f7", padding: "10px", borderRadius: "6px", fontSize: "0.8rem" }}>
                <div>Pagine stimate: <strong>{preview.estimatedPageCount}</strong></div>
                <div>Messaggi totali: <strong>{preview.totalMessageCount}</strong></div>
                <div>Citazioni incluse: <strong>{preview.totalCitationCount}</strong></div>
              </div>
            )}
          </div>

          <div style={{ padding: "16px", overflowY: "auto", background: "#ffffff" }}>
            {error && <div className="feedback-banner feedback-banner--error">{error}</div>}

            {isLoading ? (
              <div style={{ display: "flex", height: "100%", alignItems: "center", justifyContent: "center", color: "#64748b" }}>
                Generazione anteprima visiva in corso...
              </div>
            ) : preview ? (
              <iframe
                title="Anteprima Documento"
                srcDoc={preview.htmlPreview}
                style={{
                  width: "100%",
                  height: "100%",
                  border: "1px solid #cbd5e1",
                  borderRadius: "6px"
                }}
              />
            ) : null}
          </div>
        </div>

        <div style={{ padding: "12px 20px", borderTop: "1px solid #e2e8f0", display: "flex", justifyContent: "flex-end", gap: "10px", background: "#f8fafc" }}>
          <button className="button button--ghost" onClick={onClose}>
            Annulla
          </button>
          <button disabled={isExporting || isLoading} className="button button--primary" onClick={() => void handleExport()}>
            <Download className="icon icon--sm" /> {isExporting ? "Esportazione..." : `Conferma ed Esporta in ${format}`}
          </button>
        </div>
      </div>
    </div>
  );
}
