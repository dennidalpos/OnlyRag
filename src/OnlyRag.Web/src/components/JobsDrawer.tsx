import { useRef } from "react";
import { JobsSection } from "./JobsSection";
import { useModalFocusTrap } from "./useModalFocusTrap";

type JobsDrawerProps = {
  isOpen: boolean;
  onClose: () => void;
  onJobsChanged?: () => void;
};

export function JobsDrawer({ isOpen, onClose, onJobsChanged }: JobsDrawerProps) {
  const drawerRef = useRef<HTMLDivElement | null>(null);
  useModalFocusTrap(drawerRef, isOpen);

  if (!isOpen) {
    return null;
  }

  return (
    <div className="jobs-drawer-backdrop" onClick={onClose}>
      <div
        className="jobs-drawer animate-fade-in"
        ref={drawerRef}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-label="Pannello operazioni in background"
      >
        <div className="jobs-drawer__header">
          <h2 className="jobs-drawer__title">Operazioni in background</h2>
          <button
            type="button"
            className="button-secondary"
            onClick={onClose}
            aria-label="Chiudi pannello operazioni"
            style={{ padding: "4px 10px" }}
          >
            ✕
          </button>
        </div>
        <div className="jobs-drawer__content">
          <JobsSection onJobsChanged={onJobsChanged} />
        </div>
      </div>
    </div>
  );
}
