import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import { ErrorBoundary } from "./components/common/ErrorBoundary";
import "./styles.css";

if (import.meta.env.DEV) {
  const { default: axe } = await import("axe-core");
  setTimeout(() => {
    void axe.run().then(({ violations }) => {
      if (violations.length > 0) {
        console.error("[axe] accessibility violations:", violations);
      }
    });
  }, 1000);
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ErrorBoundary>
      <App />
    </ErrorBoundary>
  </StrictMode>
);
