import React, { useEffect, useState } from "react";
import { FileText, UploadCloud } from "lucide-react";

type GlobalDropzoneOverlayProps = {
  onFilesDropped: (files: FileList) => void;
};

export const GlobalDropzoneOverlay: React.FC<GlobalDropzoneOverlayProps> = ({ onFilesDropped }) => {
  const [isDragging, setIsDragging] = useState(false);

  useEffect(() => {
    let dragCounter = 0;

    const handleDragEnter = (e: DragEvent) => {
      e.preventDefault();
      if (e.dataTransfer?.types && Array.from(e.dataTransfer.types).includes("Files")) {
        dragCounter++;
        setIsDragging(true);
      }
    };

    const handleDragLeave = (e: DragEvent) => {
      e.preventDefault();
      dragCounter--;
      if (dragCounter <= 0) {
        setIsDragging(false);
        dragCounter = 0;
      }
    };

    const handleDragOver = (e: DragEvent) => {
      e.preventDefault();
    };

    const handleDrop = (e: DragEvent) => {
      e.preventDefault();
      setIsDragging(false);
      dragCounter = 0;
      if (e.dataTransfer?.files && e.dataTransfer.files.length > 0) {
        onFilesDropped(e.dataTransfer.files);
      }
    };

    window.addEventListener("dragenter", handleDragEnter);
    window.addEventListener("dragleave", handleDragLeave);
    window.addEventListener("dragover", handleDragOver);
    window.addEventListener("drop", handleDrop);

    return () => {
      window.removeEventListener("dragenter", handleDragEnter);
      window.removeEventListener("dragleave", handleDragLeave);
      window.removeEventListener("dragover", handleDragOver);
      window.removeEventListener("drop", handleDrop);
    };
  }, [onFilesDropped]);

  if (!isDragging) return null;

  return (
    <div
      className="global-dropzone-overlay"
      style={{
        position: "fixed",
        inset: 0,
        zIndex: 9999,
        background: "rgba(15, 23, 42, 0.92)",
        backdropFilter: "blur(10px)",
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        color: "#f8fafc",
        border: "3px dashed #38bdf8",
        margin: "12px",
        borderRadius: "16px",
        pointerEvents: "none"
      }}
    >
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          gap: "16px",
          textAlign: "center",
          padding: "32px",
          background: "rgba(30, 41, 59, 0.8)",
          borderRadius: "20px",
          border: "1px solid #334155",
          boxShadow: "0 20px 50px rgba(0,0,0,0.5)"
        }}
      >
        <div style={{ background: "rgba(56, 189, 248, 0.15)", padding: "20px", borderRadius: "50%", color: "#38bdf8" }}>
          <UploadCloud size={56} />
        </div>
        <h2 style={{ fontSize: "1.5rem", fontWeight: 700, margin: 0 }}>Rilascia i documenti qui</h2>
        <p style={{ color: "#94a3b8", fontSize: "0.95rem", maxWidth: "420px", margin: 0 }}>
          I file verranno importati. Potrai scegliere la modalità OCR prima dell'elaborazione.
        </p>
        <div style={{ display: "flex", gap: "8px", color: "#38bdf8", fontSize: "0.82rem", fontWeight: 600 }}>
          <FileText size={16} /> PDF, DOCX, XLSX, PPTX, TXT, MD, PNG, JPG...
        </div>
      </div>
    </div>
  );
};
