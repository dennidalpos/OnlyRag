import { useState } from "react";
import { CheckCircle2, ChevronDown, ChevronUp, Info, Loader2 } from "lucide-react";

export type ProgressStep = {
  id?: string;
  label: string;
  detail?: string;
  status: "pending" | "in_progress" | "completed" | "error";
};

type ProgressIndicatorProps = {
  steps: ProgressStep[];
  currentPhaseLabel?: string;
  chunksUsedCount?: number;
  isStreaming?: boolean;
};

export function ProgressIndicator({
  steps,
  currentPhaseLabel,
  chunksUsedCount,
  isStreaming = false
}: ProgressIndicatorProps) {
  const [isDetailsExpanded, setIsDetailsExpanded] = useState(false);

  const activeStep = steps.find((s) => s.status === "in_progress") ?? steps[steps.length - 1];
  const displayLabel = currentPhaseLabel || activeStep?.label || (isStreaming ? "Elaborazione risposta AI..." : "Completato");

  return (
    <div className="progress-indicator-container" style={{ margin: "6px 0", fontSize: "0.85rem" }}>
      {/* Compact Active Phase Banner */}
      <div
        className="progress-indicator-banner"
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "6px 12px",
          background: "rgba(15, 23, 42, 0.75)",
          border: "1px solid rgba(56, 189, 248, 0.2)",
          borderRadius: 8,
          gap: 10
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8, flex: 1, overflow: "hidden" }}>
          {isStreaming ? (
            <Loader2 size={16} className="animate-spin" style={{ color: "#38bdf8", flexShrink: 0 }} />
          ) : (
            <CheckCircle2 size={16} style={{ color: "#34d399", flexShrink: 0 }} />
          )}

          <span style={{ fontWeight: 600, color: "#f8fafc", textOverflow: "ellipsis", overflow: "hidden", whiteSpace: "nowrap" }}>
            {displayLabel}
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: 8, flexShrink: 0 }}>
          {chunksUsedCount !== undefined && chunksUsedCount > 0 && (
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: 4,
                padding: "2px 6px",
                background: "rgba(56, 189, 248, 0.15)",
                color: "#38bdf8",
                borderRadius: 4,
                fontSize: "0.76rem",
                fontWeight: 600
              }}
              title={`${chunksUsedCount} frammenti di testo recuperati ed inviati al modello LLM`}
            >
              <Info size={12} /> {chunksUsedCount} chunk
            </span>
          )}

          {steps.length > 0 && (
            <button
              type="button"
              onClick={() => setIsDetailsExpanded((prev) => !prev)}
              style={{
                background: "none",
                border: "none",
                color: "#94a3b8",
                cursor: "pointer",
                display: "flex",
                alignItems: "center",
                gap: 4,
                fontSize: "0.75rem",
                padding: "2px 6px",
                borderRadius: 4
              }}
              title="Mostra/nascondi dettagli tecnici dell'elaborazione RAG"
            >
              Dettagli {isDetailsExpanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
            </button>
          )}
        </div>
      </div>

      {/* Collapsible Technical Details */}
      {isDetailsExpanded && steps.length > 0 && (
        <div
          className="progress-details-collapsible animate-fade-in"
          style={{
            marginTop: 6,
            padding: "8px 12px",
            background: "#090d16",
            borderRadius: 8,
            border: "1px solid #1e293b",
            display: "flex",
            flexDirection: "column",
            gap: 6
          }}
        >
          {steps.map((step, idx) => (
            <div key={step.id || idx} style={{ display: "flex", alignItems: "flex-start", gap: 8, fontSize: "0.8rem" }}>
              <span style={{ width: 14, flexShrink: 0, marginTop: 2 }}>
                {step.status === "completed" ? (
                  <CheckCircle2 size={13} style={{ color: "#34d399" }} />
                ) : step.status === "in_progress" ? (
                  <Loader2 size={13} className="animate-spin" style={{ color: "#38bdf8" }} />
                ) : (
                  <span style={{ display: "inline-block", width: 6, height: 6, borderRadius: "50%", background: "#475569", margin: "4px 4px" }} />
                )}
              </span>

              <div style={{ flex: 1 }}>
                <span style={{ color: step.status === "completed" ? "#cbd5e1" : step.status === "in_progress" ? "#38bdf8" : "#64748b", fontWeight: step.status === "in_progress" ? 600 : 400 }}>
                  {step.label}
                </span>
                {step.detail && <div style={{ fontSize: "0.74rem", color: "#64748b", fontFamily: "monospace", marginTop: 1 }}>{step.detail}</div>}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
