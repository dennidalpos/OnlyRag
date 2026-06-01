import type { DiagnosticsResponse } from "../api";
import {
  formatTelemetryBytes,
  formatTelemetryPercent
} from "./SettingsSection.formatting";

export type SectionId = "chat" | "documents" | "jobs" | "translation" | "settings";

type SidebarProps = {
  activeSection: SectionId;
  sections: Record<SectionId, string>;
  onSectionChange: (section: SectionId) => void;
  activeJobCount?: number;
  diagnostics?: DiagnosticsResponse | null;
};

const sectionOrder: SectionId[] = ["chat", "documents", "jobs", "translation", "settings"];

export function Sidebar({
  activeSection,
  sections,
  onSectionChange,
  activeJobCount = 0,
  diagnostics = null
}: SidebarProps) {
  return (
    <aside className="sidebar">
      <div className="brand">
        <img className="brand-mark" src="/favicon.svg" alt="" aria-hidden="true" />
        <p>OnlyRag</p>
      </div>
      <nav className="navigation" aria-label="Sezioni principali">
        {sectionOrder.map((section) => {
          const badge = section === "jobs" && activeJobCount > 0 ? activeJobCount : null;
          return (
            <button
              className={section === activeSection ? "nav-item nav-item--active" : "nav-item"}
              key={section}
              type="button"
              aria-current={section === activeSection ? "page" : undefined}
              onClick={() => onSectionChange(section)}
            >
              <span className="nav-item__label">{sections[section]}</span>
              {badge !== null && (
                <span className="nav-badge" aria-label={`${badge} operazioni attive`}>
                  {badge}
                </span>
              )}
            </button>
          );
        })}
      </nav>
      <SidebarMetrics diagnostics={diagnostics} />
    </aside>
  );
}

function SidebarMetrics({ diagnostics }: { diagnostics: DiagnosticsResponse | null }) {
  if (!diagnostics) {
    return (
      <section className="sidebar-metrics" aria-label="Metriche sistema">
        <div className="sidebar-metrics__header">
          <span>Metriche</span>
          <small>in attesa</small>
        </div>
      </section>
    );
  }

  const telemetry = diagnostics.systemTelemetry;
  const gpu = telemetry.gpu;
  const hasNvidiaContext = diagnostics.ocrGpuCapability.capabilityStatus !== "no_nvidia_gpu" || Boolean(gpu);
  const cudaValue = diagnostics.ocrGpuCapability.compiledWithCuda === null
    ? "n/d"
    : diagnostics.ocrGpuCapability.compiledWithCuda ? "Sì" : "No";

  return (
    <section className="sidebar-metrics" aria-label="Metriche sistema">
      <div className="sidebar-metrics__header">
        <span>Metriche</span>
        <small>live</small>
      </div>
      <div className="sidebar-metrics__grid">
        <MetricRow
          label="CPU"
          value={formatTelemetryPercent(telemetry.cpu.usagePercent)}
          detail={`${telemetry.cpu.logicalProcessorCount} thread`}
        />
        <MetricRow
          label="RAM"
          value={formatTelemetryBytes(telemetry.memory.availableBytes)}
          detail={`liberi di ${formatTelemetryBytes(telemetry.memory.totalBytes)}`}
        />
        <MetricRow
          label={`Disco ${telemetry.systemDisk.name}`}
          value={formatTelemetryBytes(telemetry.systemDisk.availableBytes)}
          detail={`liberi di ${formatTelemetryBytes(telemetry.systemDisk.totalBytes)}`}
        />
        {hasNvidiaContext && (
          <>
            <MetricRow
              label="GPU"
              value={gpu ? formatTelemetryPercent(gpu.usagePercent) : "n/d"}
              detail={gpu ? `${gpu.name} ${gpu.driverVersion}` : "NVIDIA non disponibile"}
            />
            <MetricRow
              label="VRAM"
              value={gpu?.memoryAvailableBytes != null ? formatTelemetryBytes(gpu.memoryAvailableBytes) : "n/d"}
              detail={gpu?.memoryTotalBytes != null ? `liberi di ${formatTelemetryBytes(gpu.memoryTotalBytes)}` : "memoria non disponibile"}
            />
            <MetricRow
              label="CUDA Paddle"
              value={cudaValue}
              detail={`${diagnostics.ocrGpuCapability.cudaDeviceCount ?? 0} dispositivi, ${diagnostics.ocrGpuCapability.activeDevice ?? "nessuno"}`}
            />
          </>
        )}
      </div>
    </section>
  );
}

function MetricRow({
  label,
  value,
  detail
}: {
  label: string;
  value: string;
  detail: string;
}) {
  return (
    <div className="sidebar-metric">
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </div>
  );
}
