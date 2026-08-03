import type { DiagnosticsResponse } from "../../api";
import {
  formatTelemetryBytes,
  formatTelemetryPercent
} from "../settings/SettingsSection.formatting";

export type SectionId = "chat" | "documents" | "images" | "translation" | "coding" | "settings";

type SidebarProps = {
  activeSection: SectionId;
  sections: Record<SectionId, string>;
  onSectionChange: (section: SectionId) => void;
  activeJobCount?: number;
  diagnostics?: DiagnosticsResponse | null;
};

type NavGroup = {
  title: string;
  items: SectionId[];
};

const navGroups: NavGroup[] = [
  {
    title: "Intelligenza",
    items: ["chat", "coding"]
  },
  {
    title: "Media & RAG",
    items: ["documents", "translation", "images"]
  },
  {
    title: "Sistema",
    items: ["settings"]
  }
];

function SectionIcon({ section }: { section: SectionId }) {
  switch (section) {
    case "chat":
      return (
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        </svg>
      );
    case "documents":
      return (
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z" />
          <polyline points="14 2 14 8 20 8" />
          <line x1="16" y1="13" x2="8" y2="13" />
          <line x1="16" y1="17" x2="8" y2="17" />
          <line x1="10" y1="9" x2="8" y2="9" />
        </svg>
      );
    case "images":
      return (
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
          <circle cx="8.5" cy="8.5" r="1.5" />
          <polyline points="21 15 16 10 5 21" />
        </svg>
      );
    case "translation":
      return (
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="m5 8 6 6" />
          <path d="m4 14 6-6 2 3" />
          <path d="M2 5h12" />
          <path d="M7 2h1" />
          <path d="m22 22-5-10-5 10" />
          <path d="M14 18h6" />
        </svg>
      );
    case "coding":
      return (
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <polyline points="16 18 22 12 16 6" />
          <polyline points="8 6 2 12 8 18" />
        </svg>
      );
    case "settings":
      return (
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <circle cx="12" cy="12" r="3" />
          <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z" />
        </svg>
      );
  }
}

const sectionShortcuts: Record<SectionId, string> = {
  chat: "Ctrl+1",
  coding: "Ctrl+2",
  documents: "Ctrl+3",
  translation: "Ctrl+4",
  images: "Ctrl+5",
  settings: "Ctrl+6"
};

export function Sidebar({
  activeSection,
  sections,
  onSectionChange,
  activeJobCount: _activeJobCount = 0,
  diagnostics = null
}: SidebarProps) {
  return (
    <aside className="sidebar">
      <div className="brand">
        <img className="brand-mark" src="/favicon.svg" alt="" aria-hidden="true" />
        <p>OnlyRag</p>
      </div>
      <nav className="navigation" aria-label="Sezioni principali">
        {navGroups.map((group) => (
          <div key={group.title} className="nav-group">
            <span className="nav-group__title">{group.title}</span>
            {group.items.map((section) => (
              <button
                className={section === activeSection ? "nav-item nav-item--active" : "nav-item"}
                key={section}
                type="button"
                aria-current={section === activeSection ? "page" : undefined}
                onClick={() => onSectionChange(section)}
                title={`${sections[section]} (${sectionShortcuts[section]})`}
              >
                <span className="nav-item__content">
                  <SectionIcon section={section} />
                  <span className="nav-item__label">{sections[section]}</span>
                </span>
                <span className="nav-shortcut-badge" aria-hidden="true">{sectionShortcuts[section]}</span>
              </button>
            ))}
          </div>
        ))}
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
          <span>Sistema</span>
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
  const ramUsedPercent = telemetry.memory.totalBytes > 0
    ? Math.round(((telemetry.memory.totalBytes - telemetry.memory.availableBytes) / telemetry.memory.totalBytes) * 100)
    : 0;

  return (
    <section className="sidebar-metrics" aria-label="Metriche sistema">
      <div className="sidebar-metrics__header">
        <span>Sistema</span>
        <small>live</small>
      </div>
      <div className="sidebar-metrics__grid">
        <MetricRow
          label="CPU"
          value={formatTelemetryPercent(telemetry.cpu.usagePercent)}
          percent={telemetry.cpu.usagePercent}
        />
        <MetricRow
          label="RAM"
          value={`${ramUsedPercent}%`}
          percent={ramUsedPercent}
        />
        {gpu && (
          <MetricRow
            label="GPU"
            value={formatTelemetryPercent(gpu.usagePercent)}
            percent={gpu.usagePercent}
          />
        )}
        {hasNvidiaContext && (
          <MetricRow
            label="CUDA Paddle"
            value={cudaValue}
          />
        )}
        <MetricRow
          label={`Disco ${telemetry.systemDisk.name}`}
          value={`${formatTelemetryBytes(telemetry.systemDisk.availableBytes)} liberi`}
        />
      </div>
    </section>
  );
}

function MetricRow({
  label,
  value,
  percent
}: {
  label: string;
  value: string;
  percent?: number | null;
}) {
  return (
    <div className="sidebar-metric">
      <span>{label}</span>
      <strong>{value}</strong>
      {percent != null && (
        <div className="sidebar-metric__bar-bg">
          <div
            className="sidebar-metric__bar-fill"
            style={{ width: `${Math.min(100, Math.max(0, percent))}%`, background: percent > 85 ? "#ef4444" : "var(--primary-gradient)" }}
          />
        </div>
      )}
    </div>
  );
}

