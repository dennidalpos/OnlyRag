import type { BackendStatus } from "../App";

type StatusBadge = {
  label: string;
  value: string;
  tone: "online" | "offline" | "warning";
};

type AppHeaderProps = {
  currentSection: string;
  backendStatus: BackendStatus;
};

export function AppHeader({ currentSection, backendStatus }: AppHeaderProps) {
  const activeJobs = parseInt(backendStatus.jobsValue, 10);
  const statusBadges: StatusBadge[] = [
    { label: "Backend", value: backendStatus.backendValue, tone: backendStatus.backendTone },
    { label: "Ollama", value: backendStatus.ollamaValue, tone: backendStatus.ollamaTone },
    ...(activeJobs > 0
      ? [{ label: "Operazioni", value: `${activeJobs} in corso`, tone: backendStatus.jobsTone }]
      : [])
  ];

  return (
    <header className="app-header">
      <h1>{currentSection}</h1>
      <div className="status-row" aria-label="Stato applicazione">
        {statusBadges.map((badge) => (
          <span className={`status-badge status-badge--${badge.tone}`} key={badge.label}>
            <span>{badge.label}</span>
            <strong>{badge.value}</strong>
          </span>
        ))}
      </div>
    </header>
  );
}
