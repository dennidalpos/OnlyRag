type ProgressBarProps = {
  label: string;
  value: number;
  indeterminate?: boolean;
};

export function ProgressBar({ label, value, indeterminate = false }: ProgressBarProps) {
  const normalizedValue = Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
  const roundedValue = Math.round(normalizedValue);

  return (
    <div
      className={indeterminate ? "progress-track progress-track--indeterminate" : "progress-track"}
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={indeterminate ? undefined : roundedValue}
      aria-valuetext={indeterminate ? "In corso" : `${roundedValue}%`}
    >
      {!indeterminate && <div className="progress-fill" style={{ width: `${normalizedValue}%` }} />}
    </div>
  );
}
