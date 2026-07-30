import { useState } from "react";
import type { AgentStepEvent } from "../api";

type ReasoningTraceVisualizerProps = {
  events: AgentStepEvent[];
  isStreaming?: boolean;
};

export function ReasoningTraceVisualizer({
  events,
  isStreaming = false
}: ReasoningTraceVisualizerProps) {
  const [isExpanded, setIsExpanded] = useState(true);
  const [activeTab, setActiveTab] = useState<"trace" | "tools" | "raw">("trace");

  if (!events || events.length === 0) return null;

  const thoughtEvents = events.filter((e) => e.type === "thought" || e.type === "thought_chunk");
  const toolEvents = events.filter((e) => e.type === "tool_proposed" || e.type === "tool_result");

  const fullThoughtText = thoughtEvents
    .map((e) => e.content || (e.type === "thought_chunk" ? e.content : ""))
    .join("");

  return (
    <div className="reasoning-trace-visualizer">
      <div className="reasoning-trace-visualizer__header">
        <button
          type="button"
          className="reasoning-trace-visualizer__toggle-btn"
          onClick={() => setIsExpanded(!isExpanded)}
        >
          <span className="reasoning-trace-visualizer__icon">
            {isStreaming ? "⚡" : "🧠"}
          </span>
          <span className="reasoning-trace-visualizer__title">
            Traccia di Ragionamento Agentico ({events.length} eventi)
          </span>
          {isStreaming && (
            <span className="reasoning-trace-visualizer__live-badge">
              IN STREAMING DAL VIVO...
            </span>
          )}
          <span className="reasoning-trace-visualizer__chevron">
            {isExpanded ? "▼" : "▶"}
          </span>
        </button>

        {isExpanded && (
          <div className="reasoning-trace-visualizer__tabs">
            <button
              type="button"
              className={`reasoning-trace-visualizer__tab ${activeTab === "trace" ? "reasoning-trace-visualizer__tab--active" : ""}`}
              onClick={() => setActiveTab("trace")}
            >
              💭 Flusso Pensiero ({thoughtEvents.length})
            </button>
            <button
              type="button"
              className={`reasoning-trace-visualizer__tab ${activeTab === "tools" ? "reasoning-trace-visualizer__tab--active" : ""}`}
              onClick={() => setActiveTab("tools")}
            >
              🛠️ Utensili Eseguiti ({toolEvents.length})
            </button>
            <button
              type="button"
              className={`reasoning-trace-visualizer__tab ${activeTab === "raw" ? "reasoning-trace-visualizer__tab--active" : ""}`}
              onClick={() => setActiveTab("raw")}
            >
              📄 Log Grezzo
            </button>
          </div>
        )}
      </div>

      {isExpanded && (
        <div className="reasoning-trace-visualizer__content">
          {activeTab === "trace" && (
            <div className="reasoning-trace-visualizer__thought-pane">
              <pre className="reasoning-trace-visualizer__thought-text">
                {fullThoughtText || "Pensiero dell'agente in elaborazione..."}
              </pre>
            </div>
          )}

          {activeTab === "tools" && (
            <div className="reasoning-trace-visualizer__tools-pane">
              {toolEvents.length === 0 ? (
                <div className="reasoning-trace-visualizer__empty">
                  Nessun strumento invocato finora.
                </div>
              ) : (
                toolEvents.map((evt, idx) => (
                  <div key={`tool_evt_${idx}`} className="reasoning-trace-visualizer__tool-item">
                    {evt.type === "tool_proposed" && evt.toolCall && (
                      <div className="reasoning-trace-visualizer__tool-proposed">
                        <span className="reasoning-trace-visualizer__tool-name">
                          ⚡ Proposal: <code>{evt.toolCall.toolName}</code>
                        </span>
                        {evt.toolCall.explanation && (
                          <p className="reasoning-trace-visualizer__tool-exp">
                            {evt.toolCall.explanation}
                          </p>
                        )}
                        <pre className="reasoning-trace-visualizer__tool-args">
                          {evt.toolCall.argumentsJson}
                        </pre>
                      </div>
                    )}

                    {evt.type === "tool_result" && evt.toolResult && (
                      <div className={`reasoning-trace-visualizer__tool-result ${evt.toolResult.success ? "reasoning-trace-visualizer__tool-result--success" : "reasoning-trace-visualizer__tool-result--error"}`}>
                        <div className="reasoning-trace-visualizer__tool-result-header">
                          <span>
                            {evt.toolResult.success ? "✅ Successo:" : "❌ Errore:"} <code>{evt.toolResult.toolName}</code>
                          </span>
                        </div>
                        {evt.toolResult.output && (
                          <pre className="reasoning-trace-visualizer__tool-output">
                            {evt.toolResult.output.slice(0, 1500)}
                            {evt.toolResult.output.length > 1500 ? "\n... [Output troncato nel visualizzatore]" : ""}
                          </pre>
                        )}
                        {evt.toolResult.error && (
                          <p className="reasoning-trace-visualizer__tool-err-msg">
                            {evt.toolResult.error}
                          </p>
                        )}
                      </div>
                    )}
                  </div>
                ))
              )}
            </div>
          )}

          {activeTab === "raw" && (
            <div className="reasoning-trace-visualizer__raw-pane">
              <pre className="reasoning-trace-visualizer__raw-text">
                {JSON.stringify(events, null, 2)}
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
