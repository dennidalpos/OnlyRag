import React, { createContext, useContext, useEffect, useState } from "react";
import { HubConnection } from "@microsoft/signalr";
import { signalRService, JobProgressEvent, JobCompletionEvent, JobFailureEvent } from "../services/signalrService";

interface SignalRContextType {
  chatConnection: HubConnection | null;
  jobConnection: HubConnection | null;
  isConnected: boolean;
}

const SignalRContext = createContext<SignalRContextType>({
  chatConnection: null,
  jobConnection: null,
  isConnected: false
});

export const SignalRProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [chatConn, setChatConn] = useState<HubConnection | null>(null);
  const [jobConn, setJobConn] = useState<HubConnection | null>(null);
  const [isConnected, setIsConnected] = useState(false);

  useEffect(() => {
    let isMounted = true;

    async function initSignalR() {
      try {
        const chat = await signalRService.getChatHubConnection();
        const job = await signalRService.getJobHubConnection();

        if (isMounted) {
          setChatConn(chat);
          setJobConn(job);
          setIsConnected(!!(chat || job));
        }
      } catch {
        // Suppress connection failure in environments where SignalR server is not running (e.g. unit tests)
      }
    }

    void initSignalR();

    return () => {
      isMounted = false;
      void signalRService.stopAll();
    };
  }, []);

  return (
    <SignalRContext.Provider value={{ chatConnection: chatConn, jobConnection: jobConn, isConnected }}>
      {children}
    </SignalRContext.Provider>
  );
};

export function useSignalR(): SignalRContextType {
  return useContext(SignalRContext);
}

export function useJobProgress(
  onProgress?: (event: JobProgressEvent) => void,
  onCompleted?: (event: JobCompletionEvent) => void,
  onFailed?: (event: JobFailureEvent) => void
) {
  const { jobConnection } = useSignalR();

  useEffect(() => {
    if (!jobConnection) return;

    const handleProgress = (jobId: string, jobType: string, progressPercent: number, status: string, stepMessage?: string) => {
      onProgress?.({ jobId, jobType, progressPercent, status, stepMessage });
    };

    const handleCompleted = (jobId: string, jobType: string) => {
      onCompleted?.({ jobId, jobType });
    };

    const handleFailed = (jobId: string, jobType: string, error: string) => {
      onFailed?.({ jobId, jobType, error });
    };

    jobConnection.on("JobProgressUpdated", handleProgress);
    jobConnection.on("JobCompleted", handleCompleted);
    jobConnection.on("JobFailed", handleFailed);

    return () => {
      jobConnection.off("JobProgressUpdated", handleProgress);
      jobConnection.off("JobCompleted", handleCompleted);
      jobConnection.off("JobFailed", handleFailed);
    };
  }, [jobConnection, onProgress, onCompleted, onFailed]);
}
