import { apiRequest, type LocalJob } from "./api";

type ExitContributor = {
  label: string;
  hasPendingChanges: boolean;
  hasActiveWork: boolean;
  prepareForExit?: () => Promise<void>;
};

export type AppExitState = {
  hasPendingChanges: boolean;
  hasActiveWork: boolean;
  activeJobCount: number;
  reasons: string[];
};

const contributors = new Map<string, ExitContributor>();

declare global {
  interface Window {
    __ONLYRAG_APP__?: {
      getExitState: () => Promise<AppExitState>;
      prepareForExit: () => Promise<AppExitState>;
    };
  }
}

export function setExitContributor(id: string, contributor: ExitContributor) {
  contributors.set(id, contributor);
}

export function clearExitContributor(id: string) {
  contributors.delete(id);
}

export function initializeAppLifecycleBridge() {
  window.__ONLYRAG_APP__ = {
    getExitState,
    prepareForExit
  };
}

async function getExitState(): Promise<AppExitState> {
  const tracked = Array.from(contributors.values());
  const activeJobCount = await getActiveJobCount();
  const reasons: string[] = [];

  for (const contributor of tracked) {
    if (contributor.hasPendingChanges) {
      reasons.push(`${contributor.label}: modifiche non salvate.`);
    }

    if (contributor.hasActiveWork) {
      reasons.push(`${contributor.label}: operazione in corso.`);
    }
  }

  if (activeJobCount > 0) {
    reasons.push(`Job locali attivi: ${activeJobCount}.`);
  }

  return {
    hasPendingChanges: tracked.some((contributor) => contributor.hasPendingChanges),
    hasActiveWork: tracked.some((contributor) => contributor.hasActiveWork),
    activeJobCount,
    reasons
  };
}

async function prepareForExit(): Promise<AppExitState> {
  const tracked = Array.from(contributors.values());

  for (const contributor of tracked) {
    if (!contributor.hasPendingChanges || !contributor.prepareForExit) {
      continue;
    }

    await contributor.prepareForExit();
  }

  return getExitState();
}

async function getActiveJobCount(): Promise<number> {
  try {
    const jobs = await apiRequest<LocalJob[]>("/api/jobs?limit=200");
    return jobs.filter((job) =>
      job.status === "Pending" || job.status === "Running" || job.status === "Paused"
    ).length;
  } catch {
    return 0;
  }
}
