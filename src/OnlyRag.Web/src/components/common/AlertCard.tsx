import React, { useState } from "react";
import { AlertTriangle, Info, AlertCircle, X } from "lucide-react";
import { InfoTip } from "./InfoTip";

export type AlertCardVariant = "warning" | "error" | "info";

export interface AlertCardProps {
  id?: string;
  variant?: AlertCardVariant;
  title: string;
  detail?: string;
  actionLabel?: string;
  onAction?: () => void;
  isActionBusy?: boolean;
  dismissible?: boolean;
  onDismiss?: () => void;
  className?: string;
  style?: React.CSSProperties;
}

export const AlertCard: React.FC<AlertCardProps> = ({
  id,
  variant = "warning",
  title,
  detail,
  actionLabel,
  onAction,
  isActionBusy = false,
  dismissible = false,
  onDismiss,
  className = "",
  style
}) => {
  const storageKey = id ? `alert_dismissed_${id}` : null;
  const [dismissed, setDismissed] = useState<boolean>(() => {
    if (!storageKey) return false;
    try {
      return localStorage.getItem(storageKey) === "true";
    } catch {
      return false;
    }
  });

  if (dismissed) return null;

  const handleDismiss = () => {
    setDismissed(true);
    if (storageKey) {
      try {
        localStorage.setItem(storageKey, "true");
      } catch {
        // ignore localStorage errors
      }
    }
    if (onDismiss) onDismiss();
  };

  const getVariantStyles = () => {
    switch (variant) {
      case "error":
        return {
          bg: "rgba(239, 68, 68, 0.1)",
          border: "rgba(239, 68, 68, 0.3)",
          color: "#fca5a5",
          iconColor: "#ef4444",
          icon: <AlertCircle size={18} className="shrink-0" />
        };
      case "info":
        return {
          bg: "rgba(59, 130, 246, 0.1)",
          border: "rgba(59, 130, 246, 0.3)",
          color: "#93c5fd",
          iconColor: "#3b82f6",
          icon: <Info size={18} className="shrink-0" />
        };
      case "warning":
      default:
        return {
          bg: "rgba(245, 158, 11, 0.1)",
          border: "rgba(245, 158, 11, 0.3)",
          color: "#fde047",
          iconColor: "#f59e0b",
          icon: <AlertTriangle size={18} className="shrink-0" />
        };
    }
  };

  const styles = getVariantStyles();

  return (
    <div
      role={variant === "info" ? "status" : "alert"}
      aria-live="polite"
      className={`alert-card ${className}`}
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        flexWrap: "wrap",
        gap: "12px",
        padding: "10px 14px",
        borderRadius: "8px",
        background: styles.bg,
        border: `1px solid ${styles.border}`,
        color: styles.color,
        fontSize: "13px",
        lineHeight: "1.4",
        ...style
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: "8px", flex: "1 1 200px", minWidth: 0 }}>
        <span style={{ color: styles.iconColor, display: "inline-flex", flexShrink: 0 }}>
          {styles.icon}
        </span>
        <span style={{ fontWeight: 600, minWidth: 0, overflowWrap: "break-word", wordBreak: "break-word" }}>
          {title}
        </span>
        {detail && (
          <InfoTip label={title}>
            {detail}
          </InfoTip>
        )}
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: "8px", flexShrink: 0 }}>
        {actionLabel && onAction && (
          <button
            type="button"
            className="button-secondary"
            style={{
              minHeight: "28px",
              height: "28px",
              padding: "0 10px",
              fontSize: "12px",
              background: styles.bg,
              borderColor: styles.iconColor,
              color: styles.color,
              whiteSpace: "nowrap"
            }}
            onClick={onAction}
            disabled={isActionBusy}
          >
            {isActionBusy ? "In corso..." : actionLabel}
          </button>
        )}
        {dismissible && (
          <button
            type="button"
            aria-label="Chiudi avviso"
            style={{
              background: "transparent",
              border: "none",
              color: "inherit",
              opacity: 0.7,
              cursor: "pointer",
              padding: "2px",
              display: "inline-flex",
              alignItems: "center"
            }}
            onClick={handleDismiss}
          >
            <X size={16} />
          </button>
        )}
      </div>
    </div>
  );
};
