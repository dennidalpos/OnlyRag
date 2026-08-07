import { useId } from "react";

type InfoTipProps = {
  label: string;
  tooltip?: string;
  children?: React.ReactNode;
  align?: "left" | "right" | "center";
  placement?: "top" | "bottom";
};

/** Keeps secondary guidance available without permanently expanding the layout. */
export function InfoTip({
  label,
  tooltip,
  children,
  align = "left",
  placement = "top"
}: InfoTipProps) {
  const descriptionId = useId();
  const content = children ?? tooltip;

  if (!content) return null;

  const alignClass = `info-tip__content--align-${align}`;
  const placementClass = `info-tip__content--place-${placement}`;

  return (
    <span className="info-tip">
      <button
        type="button"
        className="info-tip__trigger"
        aria-label={label}
        title={label}
        aria-describedby={descriptionId}
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
      <span
        className={`info-tip__content ${alignClass} ${placementClass}`}
        id={descriptionId}
        role="tooltip"
      >
        {content}
      </span>
    </span>
  );
}

