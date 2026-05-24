import { useEffect, useState } from "react";
import { apiRequest, type OllamaModelDetails } from "../api";

export function useSettingsModelDetails(modelName: string | null) {
  const [details, setDetails] = useState<OllamaModelDetails | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    if (!modelName) {
      setDetails(null);
      setIsLoading(false);
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    apiRequest<OllamaModelDetails>(`/api/ollama/models/details?name=${encodeURIComponent(modelName)}`)
      .then((modelDetails) => {
        if (!cancelled) {
          setDetails(modelDetails);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setDetails(null);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setIsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [modelName]);

  return { details, isLoading } as const;
}
