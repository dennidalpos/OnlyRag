import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { resolveBackendBaseUrl, resolveBackendSessionToken } from "../apiClient";

export type JobProgressEvent = {
  jobId: string;
  jobType: string;
  progressPercent: number;
  status: string;
  stepMessage?: string | null;
};

export type JobCompletionEvent = {
  jobId: string;
  jobType: string;
};

export type JobFailureEvent = {
  jobId: string;
  jobType: string;
  error: string;
};

class SignalRService {
  private chatHubConnection: HubConnection | null = null;
  private jobHubConnection: HubConnection | null = null;

  public async getChatHubConnection(): Promise<HubConnection | null> {
    if (typeof import.meta !== "undefined" && import.meta.env?.MODE === "test") {
      return null;
    }

    if (this.chatHubConnection && this.chatHubConnection.state !== HubConnectionState.Disconnected) {
      return this.chatHubConnection;
    }

    const baseUrl = resolveBackendBaseUrl() || window.location.origin;
    const sessionToken = resolveBackendSessionToken();

    try {
      const builder = new HubConnectionBuilder()
        .withUrl(`${baseUrl}/hubs/chat`, {
          headers: sessionToken ? { [sessionToken.headerName]: sessionToken.token } : {}
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning);

      this.chatHubConnection = builder.build();
      await this.chatHubConnection.start();
      return this.chatHubConnection;
    } catch (error) {
      console.warn("[SignalRService] Failed to connect to ChatStreamHub:", error);
      return null;
    }
  }

  public async getJobHubConnection(): Promise<HubConnection | null> {
    if (typeof import.meta !== "undefined" && import.meta.env?.MODE === "test") {
      return null;
    }

    if (this.jobHubConnection && this.jobHubConnection.state !== HubConnectionState.Disconnected) {
      return this.jobHubConnection;
    }

    const baseUrl = resolveBackendBaseUrl() || window.location.origin;
    const sessionToken = resolveBackendSessionToken();

    try {
      const builder = new HubConnectionBuilder()
        .withUrl(`${baseUrl}/hubs/jobs`, {
          headers: sessionToken ? { [sessionToken.headerName]: sessionToken.token } : {}
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning);

      this.jobHubConnection = builder.build();
      await this.jobHubConnection.start();
      return this.jobHubConnection;
    } catch (error) {
      console.warn("[SignalRService] Failed to connect to JobProgressHub:", error);
      return null;
    }
  }

  public async stopAll(): Promise<void> {
    if (this.chatHubConnection) {
      await this.chatHubConnection.stop();
      this.chatHubConnection = null;
    }
    if (this.jobHubConnection) {
      await this.jobHubConnection.stop();
      this.jobHubConnection = null;
    }
  }
}

export const signalRService = new SignalRService();
