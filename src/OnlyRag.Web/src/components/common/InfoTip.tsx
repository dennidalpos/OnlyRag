import { useId } from "react";

type InfoTipProps = {
  label: string;
  tooltip?: string;
  children: React.ReactNode;
};

/** Keeps secondary guidance available without permanently expanding the layout. */
export function InfoTip({ label, tooltip, children }: InfoTipProps) {
  const descriptionId = useId();

  return (
    <span className="info-tip">
      <button
        type="button"
        className="info-tip__trigger"
        aria-label={label}
        aria-describedby={descriptionId}
        title={tooltip ?? label}
      >
        <svg
          width="10"
          height="10"
          viewBox="0 0 10 10"
          fill="currentColor"
          aria-hidden="true"
        >
          <text
            x="5"
            y="8"
            textAnchor="middle"
            fontSize="9"
            fontWeight="800"
            fontFamily="system-ui, sans-serif"
          >
            i
          </text>
        </svg>
      </button>
      <span className="info-tip__content" id={descriptionId} role="tooltip">
        {children}
      </span>
    </span>
  );
}
