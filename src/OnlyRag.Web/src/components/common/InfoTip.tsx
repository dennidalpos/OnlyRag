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
        aria-label="Informazioni aggiuntive"
        aria-describedby={descriptionId}
        title={tooltip ?? label}
      >
        i
      </button>
      <span className="info-tip__content" id={descriptionId} role="tooltip">
        {children}
      </span>
    </span>
  );
}
