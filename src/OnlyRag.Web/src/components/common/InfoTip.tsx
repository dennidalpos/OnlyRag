import { useId } from "react";

type InfoTipProps = {
  label: string;
  children: string;
};

/** Keeps secondary guidance available without permanently expanding the layout. */
export function InfoTip({ label, children }: InfoTipProps) {
  const descriptionId = useId();

  return (
    <span className="info-tip">
      <button
        type="button"
        className="info-tip__trigger"
        aria-label="Informazioni aggiuntive"
        aria-describedby={descriptionId}
        title={label}
      >
        i
      </button>
      <span className="info-tip__content" id={descriptionId} role="tooltip">
        {children}
      </span>
    </span>
  );
}
