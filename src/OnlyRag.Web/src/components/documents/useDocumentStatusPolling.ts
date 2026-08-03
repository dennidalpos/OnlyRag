import { useEffect, useState, type Dispatch, type SetStateAction } from "react";
import {
  apiRequest,
  type DocumentEmbeddingStatus,
  type DocumentOcrStatus,
  type DocumentPipelineStatus,
  type ImportedDocument,
  type LocalJob
} from "../../api";
import {
  initialRefreshStatus,
  markRefreshFailure,
  markRefreshSuccess,
  type RefreshStatus
} from "../../pollingStatus";

export function useDocumentStatusPolling(selectedDocument: ImportedDocument | null) {
  const [selectedJob, setSelectedJob] = useState<LocalJob | null>(null);
  const [embeddingStatus, setEmbeddingStatus] = useState<DocumentEmbeddingStatus | null>(null);
  const [ocrStatus, setOcrStatus] = useState<DocumentOcrStatus | null>(null);
  const [pipelineStatus, setPipelineStatus] = useState<DocumentPipelineStatus | null>(null);
  const [detailRefreshStatus, setDetailRefreshStatus] = useState(initialRefreshStatus);

  useEffect(() => {
    let cancelled = false;
    const jobId = selectedDocument?.currentJobId;
    if (!jobId) {
      setSelectedJob(null);
      setDetailRefreshStatus(initialRefreshStatus);
      return;
    }

    const fetch = () => apiRequest<LocalJob>(`/api/jobs/${jobId}`)
      .then((job) => {
        if (!cancelled) {
          setSelectedJob(job);
          setDetailRefreshStatus(markRefreshSuccess());
        }
      })
      .catch((error) => {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : "Impossibile aggiornare il job selezionato.";
          setDetailRefreshStatus((current) => markRefreshFailure(current, message));
        }
      });

    void fetch();
    const interval = window.setInterval(fetch, 3000);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [selectedDocument?.currentJobId]);

  useEffect(() => {
    let cancelled = false;
    const id = selectedDocument?.id;
    if (!id) {
      setEmbeddingStatus(null);
      return;
    }

    const fetch = () => apiRequest<DocumentEmbeddingStatus>(`/api/documents/${id}/embedding-status`)
      .then((status) => {
        if (!cancelled) {
          setEmbeddingStatus(status);
          setDetailRefreshStatus(markRefreshSuccess());
        }
      })
      .catch((error) => updateDetailFailure(error, cancelled, setDetailRefreshStatus, "Impossibile aggiornare lo stato embedding."));

    void fetch();
    const interval = window.setInterval(fetch, 3000);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [selectedDocument?.id]);

  useEffect(() => {
    let cancelled = false;
    const id = selectedDocument?.id;
    if (!id) {
      setOcrStatus(null);
      return;
    }

    const fetch = () => apiRequest<DocumentOcrStatus>(`/api/documents/${id}/ocr-status`)
      .then((status) => {
        if (!cancelled) {
          setOcrStatus(status);
          setDetailRefreshStatus(markRefreshSuccess());
        }
      })
      .catch((error) => updateDetailFailure(error, cancelled, setDetailRefreshStatus, "Impossibile aggiornare lo stato OCR."));

    void fetch();
    const interval = window.setInterval(fetch, 3000);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [selectedDocument?.id]);

  useEffect(() => {
    let cancelled = false;
    const id = selectedDocument?.id;
    if (!id) {
      setPipelineStatus(null);
      return;
    }

    const fetch = () => apiRequest<DocumentPipelineStatus>(`/api/documents/${id}/pipeline-status`)
      .then((status) => {
        if (!cancelled) {
          setPipelineStatus(status);
          setDetailRefreshStatus(markRefreshSuccess());
        }
      })
      .catch((error) => updateDetailFailure(error, cancelled, setDetailRefreshStatus, "Impossibile aggiornare lo stato pipeline."));

    void fetch();
    const interval = window.setInterval(fetch, 4000);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [selectedDocument?.id]);

  return {
    detailRefreshStatus,
    embeddingStatus,
    ocrStatus,
    pipelineStatus,
    selectedJob,
    setDetailRefreshStatus
  };
}

function updateDetailFailure(
  error: unknown,
  cancelled: boolean,
  setDetailRefreshStatus: Dispatch<SetStateAction<RefreshStatus>>,
  fallbackMessage: string)
{
  if (cancelled) {
    return;
  }

  const message = error instanceof Error ? error.message : fallbackMessage;
  setDetailRefreshStatus((current) => markRefreshFailure(current, message));
}
