import React, { useRef } from "react";
import { AlertTriangle } from "lucide-react";
import { useModalFocusTrap } from "./useModalFocusTrap";

export interface ConfirmDialogProps {
  isOpen: boolean;
  title?: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: "danger" | "warning" | "primary";
  onConfirm: () => void;
  onCancel: () => void;
}

export const ConfirmDialog: React.FC<ConfirmDialogProps> = ({
  isOpen,
  title = "Conferma operazione",
  message,
  confirmLabel = "Conferma",
  cancelLabel = "Annulla",
  variant = "danger",
  onConfirm,
  onCancel
}) => {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  useModalFocusTrap(dialogRef, isOpen, { onEscape: onCancel });

  if (!isOpen) return null;

  const confirmBtnClass = variant === "danger"
    ? "button-danger"
    : variant === "warning"
      ? "button-warning"
      : "button-primary";

  return (
    <div
      className="modal-backdrop"
      onClick={(e) => {
        if (e.target === e.currentTarget) onCancel();
      }}
    >
      <div
        ref={dialogRef}
        className="modal-content confirm-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-dialog-title"
        style={{
          maxWidth: "440px",
          width: "90%",
          padding: "20px",
          borderRadius: "12px",
          background: "var(--color-surface, #1e293b)",
          border: "1px solid var(--color-border, #334155)",
          boxShadow: "0 20px 40px rgba(0,0,0,0.5)",
          color: "var(--color-text, #f8fafc)"
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "10px", marginBottom: "12px" }}>
          <AlertTriangle size={22} style={{ color: variant === "danger" ? "#ef4444" : "#f59e0b" }} />
          <h3 id="confirm-dialog-title" style={{ margin: 0, fontSize: "1.1rem", fontWeight: 700 }}>
            {title}
          </h3>
        </div>

        <p style={{ margin: "0 0 20px 0", fontSize: "0.9rem", color: "#cbd5e1", lineHeight: "1.5" }}>
          {message}
        </p>

        <div style={{ display: "flex", justifyContent: "flex-end", gap: "10px" }}>
          <button type="button" className="button-secondary" onClick={onCancel}>
            {cancelLabel}
          </button>
          <button type="button" className={confirmBtnClass} onClick={onConfirm} autoFocus>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
};
