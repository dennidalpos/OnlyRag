import { useState } from "react";

export function useModalMaximize() {
  const [isMaximized, setIsMaximized] = useState(false);

  return {
    isMaximized,
    maximizedClassName: isMaximized ? " modal-frame--maximized" : "",
    toggleMaximized: () => setIsMaximized((current) => !current),
    maximizeLabel: isMaximized ? "Ripristina" : "Massimizza"
  };
}
