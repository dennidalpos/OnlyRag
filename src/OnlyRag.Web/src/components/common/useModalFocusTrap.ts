import { useEffect, useRef, type RefObject } from "react";

type ModalFocusTrapOptions = {
  onEscape?: () => void;
  restoreFocus?: boolean;
};

export function useModalFocusTrap(
  modalRef: RefObject<HTMLElement | null>,
  isActive: boolean,
  { onEscape, restoreFocus = true }: ModalFocusTrapOptions = {}
) {
  const onEscapeRef = useRef(onEscape);
  onEscapeRef.current = onEscape;

  useEffect(() => {
    if (!isActive) {
      return;
    }

    const modal = modalRef.current;
    if (!modal) {
      return;
    }

    const activeModal = modal;
    const previouslyFocused = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    const focusable = getFocusableElements(activeModal);
    (focusable[0] ?? activeModal).focus();

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape" && onEscapeRef.current) {
        event.preventDefault();
        onEscapeRef.current();
        return;
      }

      if (event.key !== "Tab") {
        return;
      }

      const items = getFocusableElements(activeModal);
      if (items.length === 0) {
        event.preventDefault();
        activeModal.focus();
        return;
      }

      const first = items[0];
      const last = items[items.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      if (restoreFocus && previouslyFocused?.isConnected) {
        previouslyFocused.focus();
      }
    };
  }, [isActive, modalRef, restoreFocus]);
}

function getFocusableElements(root: HTMLElement): HTMLElement[] {
  return Array.from(
    root.querySelectorAll<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
    )
  ).filter((element) => {
    if (element.tabIndex < 0) {
      return false;
    }

    if ("disabled" in element && element.disabled) {
      return false;
    }

    return !element.getAttribute("aria-disabled");
  });
}
